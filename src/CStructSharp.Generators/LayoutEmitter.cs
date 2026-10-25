namespace CStructSharp.Generators;

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using Microsoft.CodeAnalysis;
using static CStructSharp.Generators.Emit;

/// <summary>
///     Emits the C# for one compiled layout into the attributed <c>static partial</c> class. The emitter is
///     organized by what the runtime does for the same layout: this file holds the class frame (the definition
///     text, the lazily built runtime <see cref="CStruct"/>, the root name); the partial files add the types and
///     the operations.
/// </summary>
internal sealed partial class LayoutEmitter
{
    private readonly LayoutRequest request;
    private readonly LayoutCompilation compilation;
    private readonly string definition;
    private readonly string rootName;
    private readonly List<Diagnostic> diagnostics = new();
    private GeneratedModel model = null!;
    private bool hasErrors;

    // Whether the views carry the caller's variables, decided once from the placement of every view (null until asked).
    private bool? viewsCarryVariables;

    /// <summary>Prepares one attributed class; call <see cref="Validate"/> before <c>Emit</c>.</summary>
    /// <param name="request">The attributed class's configuration and source locations.</param>
    /// <param name="compilation">The compiled layout the generated members describe.</param>
    /// <param name="definition">The layout text, embedded in the generated class.</param>
    /// <param name="rootName">The declaration the plain Parse and Serialize methods read and write.</param>
    public LayoutEmitter(LayoutRequest request, LayoutCompilation compilation, string definition, string rootName)
    {
        this.request = request;
        this.compilation = compilation;
        this.definition = definition;
        this.rootName = rootName;
    }

    /// <summary>Gets a value indicating whether validation reported an error; no source is then emitted.</summary>
    public bool HasErrors => this.hasErrors;

    /// <summary>The defines the compilation folded to values.</summary>
    public IReadOnlyDictionary<string, Syntax.Expr> StaticVariables => this.compilation.StaticLayoutVariables;

    /// <summary>The layout's <c>#define</c>s by name (for the ones that depend on caller variables).</summary>
    public IReadOnlyDictionary<string, Syntax.Defines> Definitions => this.definitions ??= this.CollectDefinitions();

    private Dictionary<string, Syntax.Defines>? definitions;

    private Dictionary<string, Syntax.Defines> CollectDefinitions()
    {
        var result = new Dictionary<string, Syntax.Defines>(System.StringComparer.Ordinal);
        foreach (KeyValuePair<string, Syntax.CStructElement> element in this.compilation.CStructElements)
        {
            if (element.Value is Syntax.Defines define)
            {
                result[element.Key] = define;
            }
        }

        return result;
    }

    /// <summary>The members every generated class has, which no generated type or the class itself may be named like.</summary>
    private static readonly string[] ReservedMembers =
    [
        "Layout", "RootName", "Definition", "Parse", "ParseAsync", "TryParse", "Serialize", "Write", "WriteAsync", "ParseWithDebug", "ResolveAddress", "GetArrayLength", "UpdatePath",
        "Sizes", "Offsets", "Update", "CreateCodecs", "CodecInstances", "LayoutInstance", "CreateLayout", "ToStructValue", "ToMapped", "SerializeMapped", "ParseMapped", "ReadValue", "TryReadValue", "Records", "RecordsAsync",
    ];

    private static string GeneratorVersion => typeof(LayoutEmitter).Assembly.GetName().Version?.ToString() ?? "0.0.0.0";

    /// <summary>Builds the type model, which assigns every generated name, and reports CSG003 for collisions.</summary>
    /// <returns>The diagnostics found, empty when the class can be generated.</returns>
    public IReadOnlyList<Diagnostic> Validate()
    {
        // Struct asks for value types on a surface that has types; Auto asks only once the layout is known to qualify (below).
        this.usesValueTypes = this.request.Shape == GenerationShape.Struct && this.request.Surface is GenerationSurface.Reader or GenerationSurface.Parser;
        this.BuildModel();
        if (this.request.Shape == GenerationShape.Auto && !this.hasErrors && this.ValueTypeProblem() is null)
        {
            // The layout qualifies, so the value shape is taken, unless the one name it reserves in addition (the inline array container)
            // collides with the layout's own: then the class is kept, without a report. The model is the same for both shapes apart
            // from that reserved name, so it is built again only when a name could collide.
            this.usesValueTypes = !this.NamesInlineArraysContainer();
        }

        if (this.request.Shape == GenerationShape.Struct && this.ValueTypeProblem() is { } problem)
        {
            this.hasErrors = true;
            this.diagnostics.Add(Diagnostic.Create(GeneratorDiagnostics.ValueShapeUnsupported, this.request.AttributeSpan.ToLocation(), this.request.ClassName, problem));
        }

        if (this.request.Shape == GenerationShape.Scalar && this.ScalarProblem() is { } scalarProblem)
        {
            this.hasErrors = true;
            this.diagnostics.Add(Diagnostic.Create(GeneratorDiagnostics.ScalarShapeUnsupported, this.request.AttributeSpan.ToLocation(), this.request.ClassName, scalarProblem));
        }

        this.SuggestShape();
        return this.diagnostics;
    }

    /// <summary>
    ///     Suggests the cheapest shape for a layout generated as classes: <c>Scalar</c> when the layout is one number (CSG021), else
    ///     <c>Struct</c> when every struct could be a value type (CSG022). Only a Reader or Parser surface can take either shape, and a
    ///     layout that chose a shape is not second-guessed.
    /// </summary>
    private void SuggestShape()
    {
        if (this.hasErrors || this.request.Shape != GenerationShape.Class || this.request.Surface is not (GenerationSurface.Reader or GenerationSurface.Parser))
        {
            return;
        }

        if (this.ScalarProblem() is null)
        {
            CompiledField member = this.model.Composites[0].Members[0].Field;
            this.diagnostics.Add(Diagnostic.Create(GeneratorDiagnostics.QualifiesForScalar, this.request.AttributeSpan.ToLocation(), this.request.ClassName, member.DisplayTypeSpelling));
        }
        else if (this.ValueTypeProblem() is null)
        {
            this.diagnostics.Add(Diagnostic.Create(GeneratorDiagnostics.QualifiesForStruct, this.request.AttributeSpan.ToLocation(), this.request.ClassName));
        }
    }

    /// <summary>Whether the class or a generated type of the model carries the name a value-type class reserves for its inline array container.</summary>
    /// <returns><see langword="true"/> when a value-type class could not be generated without a name collision.</returns>
    private bool NamesInlineArraysContainer()
        => this.request.ClassName == InlineArraysClass
           || this.model.Composites.Any(composite => composite.Name == InlineArraysClass)
           || this.model.Enums.Any(generatedEnum => generatedEnum.Name == InlineArraysClass);

    /// <summary>Builds the type model for the current shape and reports the name collisions it finds.</summary>
    private void BuildModel()
    {
        this.scalarProblemKnown = false;
        var taken = new Dictionary<string, string>(System.StringComparer.Ordinal)
        {
            [this.request.ClassName] = "the containing class",
        };
        foreach (string reserved in ReservedMembers)
        {
            taken[reserved] = "the generated " + reserved + " member";
        }

        // The inline-array container exists only in a value-type class, so only such a class keeps the name for itself.
        bool reservesInlineArrays = this.UsesValueTypes;
        if (reservesInlineArrays)
        {
            taken[InlineArraysClass] = "the generated " + InlineArraysClass + " member";
        }

        // A C# type cannot have a member with its own name: a class named like a generated member cannot be generated into.
        if (System.Array.IndexOf(ReservedMembers, this.request.ClassName) >= 0 || (reservesInlineArrays && this.request.ClassName == InlineArraysClass))
        {
            this.Error(GeneratorDiagnostics.NameCollision, "The class '" + this.request.ClassName + "' is named like the generated " + this.request.ClassName + " member, which C# does not allow; rename the class.");
        }

        // A #define with a fixed integer value becomes a constant of the class, so its name is claimed before the types' names.
        if (this.request.Shape != GenerationShape.Scalar)
        {
            foreach (DefineConstant constant in this.DefineConstants())
            {
                if (taken.TryGetValue(constant.Name, out string? owner))
                {
                    this.Error(GeneratorDiagnostics.NameCollision, "The #define '" + constant.LayoutName + "' would be generated as the constant '" + constant.Name + "', which is already " + owner + "; rename the #define or the declaration in the layout.");
                }
                else
                {
                    taken[constant.Name] = "generated for the #define '" + constant.LayoutName + "'";
                }
            }
        }

        this.model = GeneratedModel.Build(this.compilation, this.request.KeepNames, taken, this.request.Views);
        foreach (string collision in this.model.Collisions)
        {
            this.Error(GeneratorDiagnostics.NameCollision, collision);
        }

        this.ReportMatchesCollisions();
    }

    /// <summary>
    ///     Reports a generated type (or the class itself) named like a <c>Matches</c> method the layout generates, which C# would
    ///     reject inside the generated file: <c>Matches</c> for the root and <c>Matches&lt;Name&gt;</c> for each struct with conditions.
    /// </summary>
    private void ReportMatchesCollisions()
    {
        if (this.request.Shape == GenerationShape.Scalar)
        {
            return;
        }

        var typeNames = new HashSet<string>(this.model.Composites.Select(composite => composite.Name).Concat(this.model.Enums.Select(generatedEnum => generatedEnum.Name)), System.StringComparer.Ordinal)
        {
            this.request.ClassName,
        };
        foreach (GeneratedComposite composite in this.model.Composites)
        {
            if (!composite.IsDeclared || composite.IsUnion || !composite.Composite.HasConstraints)
            {
                continue;
            }

            string method = "Matches" + composite.Name;
            bool isRoot = composite.LayoutName == this.rootName;
            if (typeNames.Contains(method) || (isRoot && typeNames.Contains("Matches")))
            {
                this.Error(GeneratorDiagnostics.NameCollision, "'" + composite.LayoutName + "' has conditions, so a method '" + (typeNames.Contains(method) ? method : "Matches") + "' is generated, which a type of the layout is also named; rename it or use [CStructLayout(KeepNames = ...)].");
            }
        }
    }

    /// <summary>The complete generated file.</summary>
    /// <returns>The C# source of the partial class, ready for <c>AddSource</c>.</returns>
    public string Emit()
    {
        var writer = new SourceWriter();
        FileHeader(writer, "the [CStructLayout] attribute on " + this.request.ClassName);
        if (this.request.Namespace is not null)
        {
            writer.Line("namespace " + this.request.Namespace);
            writer.Line("{");
            writer.Indent();
        }

        foreach (ContainingType container in this.request.ContainingTypes)
        {
            writer.Open("partial " + container.Keyword + " " + container.Name);
        }

        writer.Line("[global::System.CodeDom.Compiler.GeneratedCode(\"CStructSharp\", " + SourceWriter.Literal(GeneratorVersion) + ")]");
        writer.Open(this.request.Accessibility + " static partial class " + this.request.ClassName);
        this.EmitFrame(writer);
        bool scalar = this.UsesScalarShape;
        if (scalar)
        {
            this.EmitScalar(writer);
        }
        else
        {
            this.EmitTypes(writer);
            if (this.request.Surface == GenerationSurface.Views)
            {
                this.EmitSettingConstants(writer);
                this.EmitViewMatches(writer);
            }
            else
            {
                this.EmitReaders(writer);
            }
        }

        if (this.request.Surface == GenerationSurface.Full)
        {
            this.EmitWriters(writer);
        }

        this.EmitOperations(writer);
        if (!scalar)
        {
            this.EmitViews(writer);
        }

        writer.Close();

        for (int index = 0; index < this.request.ContainingTypes.Count; index++)
        {
            writer.Close();
        }

        if (this.request.Namespace is not null)
        {
            writer.Outdent();
            writer.Line("}");
        }

        return writer.ToString();
    }

    private void Error(DiagnosticDescriptor descriptor, string message)
    {
        this.hasErrors = true;
        this.diagnostics.Add(Diagnostic.Create(descriptor, this.request.AttributeSpan.ToLocation(), message));
    }

    /// <summary>The members every generated class has: the definition, the runtime layout, and the root name.</summary>
    private void EmitFrame(SourceWriter writer)
    {
        this.EmitDefineConstants(writer);
        if (this.request.Surface != GenerationSurface.Parser)
        {
            writer.Line("/// <summary>The layout text the members of this class were generated from.</summary>");
            writer.Line("public const string Definition = " + SourceWriter.Literal(this.definition) + ";");
            writer.Line();
            writer.Line(this.request.Surface switch
            {
                GenerationSurface.Full => "/// <summary>The declaration the plain <see cref=\"Parse\"/> and <see cref=\"Serialize\"/> methods operate on.</summary>",
                GenerationSurface.Reader => "/// <summary>The declaration the plain <see cref=\"Parse\"/> methods operate on.</summary>",
                _ => "/// <summary>The declaration whose view the generated <c>Offsets</c> constants describe.</summary>",
            });
            writer.Line("public const string RootName = " + SourceWriter.Literal(this.rootName) + ";");
            writer.Line();
        }

        if (this.request.Surface == GenerationSurface.Full)
        {
            this.EmitLayoutProperty(writer);
        }

        if (this.request.Settings.Codecs.Count > 0)
        {
            this.EmitCodecInstances(writer);
        }

        if (this.request.Surface == GenerationSurface.Full)
        {
            this.EmitCreateLayout(writer);
        }
    }

    /// <summary>The lazily built runtime <c>CStruct</c> and the <c>Layout</c> property that exposes it (the full surface only).</summary>
    private void EmitLayoutProperty(SourceWriter writer)
    {
        writer.Line("private static readonly global::System.Lazy<global::CStructSharp.CStruct> LayoutInstance = new(CreateLayout, global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);");
        writer.Line();
        writer.Line("/// <summary>The runtime <see cref=\"global::CStructSharp.CStruct\"/> for the same definition and options, built on first use: introspection, JSON, memory images, and the parity oracle.</summary>");
        writer.Line("public static global::CStructSharp.CStruct Layout => LayoutInstance.Value;");
        writer.Line();
    }

    /// <summary>The method that builds the runtime <c>CStruct</c> with the attribute's options (the full surface only).</summary>
    private void EmitCreateLayout(SourceWriter writer)
    {
        writer.Open("private static global::CStructSharp.CStruct CreateLayout()");
        writer.Line("var options = new global::CStructSharp.CStructCompilationOptions");
        writer.Line("{");
        writer.Indent();
        if (this.request.Settings.Codecs.Count > 0)
        {
            writer.Line("Codecs = CodecInstances.Value,");
        }

        writer.Line("CLongWidth = " + this.request.Settings.CLongWidth.ToString(CultureInfo.InvariantCulture) + ",");
        writer.Line("BitfieldPacking = global::CStructSharp.BitfieldPacking." + this.request.Settings.BitfieldPacking + ",");
        writer.Line("BitfieldAllocation = global::CStructSharp.BitfieldAllocation." + this.request.Settings.BitfieldAllocation + ",");
        if (this.request.Settings.DefaultEnumStorage is not null)
        {
            writer.Line("DefaultEnumStorage = " + SourceWriter.Literal(this.request.Settings.DefaultEnumStorage) + ",");
        }

        if (this.request.Settings.Defined.Count > 0)
        {
            writer.Write("Defined = new global::System.Collections.Generic.HashSet<string>(global::System.StringComparer.Ordinal) { ");
            foreach (string symbol in this.request.Settings.Defined)
            {
                writer.Write(SourceWriter.Literal(symbol) + ", ");
            }

            writer.Line("},");
        }

        writer.Outdent();
        writer.Line("};");
        writer.Line(
            "return new global::CStructSharp.CStruct(Definition, pointerSize: " + this.request.Settings.PointerSize.ToString(CultureInfo.InvariantCulture) +
            ", aligned: " + (this.request.Settings.Aligned ? "true" : "false") +
            ", isLittleEndian: " + (this.request.Settings.LittleEndian ? "true" : "false") + ", compilationOptions: options);");
        writer.Close();
    }

    /// <summary>
    ///     The custom codec instances: the class implements <c>CreateCodecs()</c>; the first use checks every instance
    ///     against the attribute's declaration (name, fixed size, alignment) because the generated placement relied
    ///     on those facts at build time.
    /// </summary>
    private void EmitCodecInstances(SourceWriter writer)
    {
        const string CodecType = "global::CStructSharp.Codecs.ICustomCodec";
        writer.Line("/// <summary>Supplies the custom codec instances the layout declares (<c>" + string.Join(", ", this.request.Settings.Codecs) + "</c>), one per declaration and in that order.</summary>");
        writer.Line("/// <returns>The codec instances.</returns>");
        writer.Line("private static partial global::System.Collections.Generic.IReadOnlyList<" + CodecType + "> CreateCodecs();");
        writer.Line();
        writer.Line("private static readonly global::System.Lazy<" + CodecType + "[]> CodecInstances = new(ResolveCodecs, global::System.Threading.LazyThreadSafetyMode.ExecutionAndPublication);");
        writer.Line();
        writer.Open("private static " + CodecType + "[] ResolveCodecs()");
        writer.Line("global::System.Collections.Generic.IReadOnlyList<" + CodecType + "> supplied = CreateCodecs() ?? throw new global::System.InvalidOperationException(\"CreateCodecs() returned null; return one ICustomCodec per declared codec.\");");
        writer.Line("string[] expected = { " + string.Join(", ", this.request.Settings.Codecs.Select(declaration => SourceWriter.Literal(Expected(declaration)))) + " };");
        writer.Open("if (supplied.Count != expected.Length)");
        writer.Line("throw new global::System.InvalidOperationException(\"CreateCodecs() returned \" + supplied.Count.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \" codecs; the layout declares \" + expected.Length.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \".\");");
        writer.Close();
        writer.Line("var instances = new " + CodecType + "[expected.Length];");
        writer.Open("for (int index = 0; index < expected.Length; index++)");
        writer.Line(CodecType + " codec = supplied[index] ?? throw new global::System.InvalidOperationException(\"CreateCodecs() returned null for codec \" + expected[index] + \".\");");
        writer.Line("string actual = codec.Name + \":\" + (codec.FixedSize is { } size ? size.ToString(global::System.Globalization.CultureInfo.InvariantCulture) : \"*\") + \":\" + codec.Alignment.ToString(global::System.Globalization.CultureInfo.InvariantCulture);");
        writer.Open("if (actual != expected[index])");
        writer.Line("throw new global::System.InvalidOperationException(\"Custom codec \" + expected[index] + \" is declared in [CStructLayout(Codecs = ...)] but the instance from CreateCodecs() reports \" + actual + \" (name:size:alignment); the generated placement depends on the declaration.\");");
        writer.Close();
        writer.Line("instances[index] = codec;");
        writer.Close();
        writer.Line("return instances;");
        writer.Close();
        writer.Line();
    }

    /// <summary>A declaration in its canonical <c>name:size:alignment</c> spelling (the generated check compares against it).</summary>
    private static string Expected(string declaration)
        => CustomCodecDeclaration.TryParse(declaration, out CustomCodecDescriptor descriptor) ? CustomCodecDeclaration.Describe(descriptor) : declaration;

    /// <summary>The index of a custom codec's instance in <c>CodecInstances</c>: the position of its declaration.</summary>
    private int CodecIndex(string typeName)
    {
        for (int index = 0; index < this.request.Settings.Codecs.Count; index++)
        {
            if (CustomCodecDeclaration.TryParse(this.request.Settings.Codecs[index], out CustomCodecDescriptor descriptor) && descriptor.Name == typeName)
            {
                return index;
            }
        }

        throw new InvalidOperationException("No declared custom codec named " + typeName);
    }
}
