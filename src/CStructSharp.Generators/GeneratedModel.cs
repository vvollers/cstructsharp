namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Syntax;

/// <summary>
///     The C# view of a compiled layout: one <see cref="GeneratedEnum"/> per layout enum, one
///     <see cref="GeneratedComposite"/> per struct or union that gets a class (top-level declarations under their
///     alias name when a typedef aliases the tag, inline composites under <c>&lt;Parent&gt;&lt;Field&gt;</c>), and
///     per composite the members a reader fills - anonymous promoted composites spliced into their parent as the
///     runtime splices them into the parent's <c>StructValue</c>.
/// </summary>
internal sealed class GeneratedModel
{
    private readonly Dictionary<CompiledCompositeType, GeneratedComposite> compositesByType = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<CompiledEnumType, GeneratedEnum> enumsByType = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<string, string> takenNames;
    private readonly bool keepNames;
    private readonly List<string> collisions = new();

    private GeneratedModel(bool keepNames, Dictionary<string, string> takenNames)
    {
        this.keepNames = keepNames;
        this.takenNames = takenNames;
    }

    public List<GeneratedEnum> Enums { get; } = new();

    public List<GeneratedComposite> Composites { get; } = new();

    public IReadOnlyList<string> Collisions => this.collisions;

    /// <summary>Builds the model; <paramref name="takenNames"/> holds the names the class frame already uses (updated with every type name).</summary>
    public static GeneratedModel Build(LayoutCompilation compilation, bool keepNames, Dictionary<string, string> takenNames)
    {
        var model = new GeneratedModel(keepNames, takenNames);
        CompiledLayoutModel compiled = compilation.CompiledModel;

        // A typedef that aliases a struct tag names the class; the tag itself gets no class of its own.
        var aliasOf = new Dictionary<Struct, string>(ReferenceEqualityComparer.Instance);
        foreach (KeyValuePair<string, CStructElement> declaration in compiled.OrderedDeclarations)
        {
            if (declaration.Value is Typedef { Struct: { } aliased } && aliased.Name.Name != declaration.Key && !aliasOf.ContainsKey(aliased))
            {
                aliasOf[aliased] = declaration.Key;
            }
        }

        foreach (KeyValuePair<string, CStructElement> declaration in compiled.OrderedDeclarations)
        {
            switch (declaration.Value)
            {
            case Syntax.Enum when compiled.Symbols.TryGetValue(declaration.Key, out CompiledTypeReference enumReference) && enumReference.Symbol.Definition is CompiledEnumType compiledEnum:
                model.AddEnum(declaration.Key, compiledEnum);
                break;
            case Struct structDeclaration when !aliasOf.ContainsKey(structDeclaration) && compiled.Composites.TryGetValue(structDeclaration, out CompiledTypeSymbol? symbol) && symbol.Definition is CompiledCompositeType composite:
                model.AddComposite(declaration.Key, composite, isDeclared: true);
                break;
            case Typedef { Struct: { } aliasedStruct } when (!aliasOf.TryGetValue(aliasedStruct, out string? alias) || alias == declaration.Key) && compiled.Composites.TryGetValue(aliasedStruct, out CompiledTypeSymbol? aliasedSymbol) && aliasedSymbol.Definition is CompiledCompositeType aliasedComposite:
                // `typedef struct _X {...} X;` names the class X; `typedef struct {...} Anon;` is the struct itself.
                model.AddComposite(declaration.Key, aliasedComposite, isDeclared: true);
                break;
            }
        }

        // Members are resolved after every declared type has its name, so a field can refer to a later declaration.
        for (int index = 0; index < model.Composites.Count; index++)
        {
            model.ResolveMembers(model.Composites[index], compiled);
        }

        return model;
    }

    public GeneratedComposite? Find(CompiledCompositeType composite) => this.compositesByType.TryGetValue(composite, out GeneratedComposite? generated) ? generated : null;

    public GeneratedEnum? Find(CompiledEnumType compiledEnum) => this.enumsByType.TryGetValue(compiledEnum, out GeneratedEnum? generated) ? generated : null;

    /// <summary>The C# storage type of an enum's backing integer.</summary>
    public static string EnumUnderlyingType(EnumIntegerCodec integer)
    {
        return (integer.BitWidth, integer.IsSigned) switch
        {
            (8, true) => "sbyte",
            (8, false) => "byte",
            (16, true) => "short",
            (16, false) => "ushort",
            (32, true) => "int",
            (32, false) => "uint",
            (64, true) => "long",
            _ => "ulong",
        };
    }

    /// <summary>The C# type a primitive codec decodes to.</summary>
    public static string PrimitiveTypeName(PrimitiveCodecKind kind)
    {
        return kind switch
        {
            PrimitiveCodecKind.UInt8 or PrimitiveCodecKind.Latin1 or PrimitiveCodecKind.Cp437 or PrimitiveCodecKind.Utf8Unit or PrimitiveCodecKind.Utf16LeUnit or PrimitiveCodecKind.Utf16BeUnit => "byte",
            PrimitiveCodecKind.Int8 => "sbyte",
            PrimitiveCodecKind.Bool => "bool",
            PrimitiveCodecKind.Char or PrimitiveCodecKind.WChar => "char",
            PrimitiveCodecKind.Int16 => "short",
            PrimitiveCodecKind.UInt16 => "ushort",
            PrimitiveCodecKind.Int24 or PrimitiveCodecKind.Int32 or PrimitiveCodecKind.SLeb128_32 => "int",
            PrimitiveCodecKind.UInt24 or PrimitiveCodecKind.UInt32 or PrimitiveCodecKind.ULeb128_32 => "uint",
            PrimitiveCodecKind.Int48 or PrimitiveCodecKind.Int64 or PrimitiveCodecKind.SLeb128_64 => "long",
            PrimitiveCodecKind.UInt48 or PrimitiveCodecKind.UInt64 or PrimitiveCodecKind.ULeb128_64 => "ulong",
            PrimitiveCodecKind.Int128 => "global::System.Int128",
            PrimitiveCodecKind.UInt128 => "global::System.UInt128",
            PrimitiveCodecKind.Float16 => "global::System.Half",
            PrimitiveCodecKind.Float32 => "float",
            PrimitiveCodecKind.Float64 => "double",
            PrimitiveCodecKind.Fixed16_16 or PrimitiveCodecKind.UFixed16_16 or PrimitiveCodecKind.Fixed2_30 or PrimitiveCodecKind.UFixed8_8 => "double",
            PrimitiveCodecKind.Uuid or PrimitiveCodecKind.Guid => "global::System.Guid",
            PrimitiveCodecKind.TerminatedAscii or PrimitiveCodecKind.TerminatedUtf8 or PrimitiveCodecKind.TerminatedUtf16 => "string",
            _ => "object?",
        };
    }

    private void AddEnum(string layoutName, CompiledEnumType compiledEnum)
    {
        string name = this.Claim(layoutName, $"the enum '{layoutName}'");
        var members = new List<GeneratedEnumMember>();
        var memberNames = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (CompiledEnumMember member in compiledEnum.Members)
        {
            string memberName = Naming.ToCSharp(member.Name, this.keepNames);
            if (memberNames.TryGetValue(memberName, out string? other))
            {
                this.collisions.Add($"Enum members '{other}' and '{member.Name}' of '{layoutName}' would both be generated as '{memberName}'; use [CStructLayout(KeepNames = true)] or rename one in the layout.");
                continue;
            }

            memberNames[memberName] = member.Name;
            members.Add(new GeneratedEnumMember(memberName, member.Name, compiledEnum.Integer.FromRawBits(member.RawBits)));
        }

        var generated = new GeneratedEnum(name, layoutName, compiledEnum, EnumUnderlyingType(compiledEnum.Integer), members);
        this.Enums.Add(generated);
        this.enumsByType[compiledEnum] = generated;
    }

    private GeneratedComposite AddComposite(string layoutName, CompiledCompositeType composite, bool isDeclared, string? preferredName = null)
    {
        if (this.compositesByType.TryGetValue(composite, out GeneratedComposite? existing))
        {
            return existing;
        }

        string what = (composite.IsUnion ? "the union '" : "the struct '") + layoutName + "'";
        string name = this.Claim(preferredName ?? layoutName, what, preferredName is not null);
        var generated = new GeneratedComposite(name, layoutName, composite, isDeclared);
        this.Composites.Add(generated);
        this.compositesByType[composite] = generated;
        return generated;
    }

    /// <summary>Takes a C# name for a type, recording a CSG003 collision when it is already used.</summary>
    private string Claim(string layoutName, string what, bool alreadyCSharp = false)
    {
        string name = alreadyCSharp ? layoutName : Naming.ToCSharp(layoutName, this.keepNames);
        if (this.takenNames.TryGetValue(name, out string? owner))
        {
            this.collisions.Add($"{Capitalize(what)} would be generated as '{name}', which is already {owner}; use [CStructLayout(KeepNames = true)] or rename the declaration in the layout.");
            return name;
        }

        this.takenNames[name] = $"generated for {what}";
        return name;
    }

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

    private void ResolveMembers(GeneratedComposite generated, CompiledLayoutModel compiled)
    {
        var memberNames = new Dictionary<string, string>(StringComparer.Ordinal) { [generated.Name] = "the class itself" };
        if (generated.IsUnion)
        {
            memberNames["SelectedMember"] = "the generated SelectedMember property";
            memberNames["RawStorage"] = "the generated RawStorage property";
        }

        this.AppendMembers(generated, generated.Composite, compiled, memberNames);
    }

    /// <summary>Adds the members of <paramref name="composite"/> to <paramref name="owner"/>; a promoted anonymous composite contributes its own members in place.</summary>
    private void AppendMembers(GeneratedComposite owner, CompiledCompositeType composite, CompiledLayoutModel compiled, Dictionary<string, string> memberNames)
    {
        foreach (CompiledField field in composite.Fields)
        {
            if (field.IsZeroWidthBitfield || (field.IsUnnamed && !field.IsInlineComposite))
            {
                // A `: 0` separator and an anonymous bitfield have no value; padding named `_` is a runtime member and stays.
                continue;
            }

            CompiledCompositeType? inline = field.Declaration is Struct inlineDeclaration && compiled.Composites.TryGetValue(inlineDeclaration, out CompiledTypeSymbol? inlineSymbol)
                                                ? inlineSymbol.Definition as CompiledCompositeType
                                                : null;
            if (field.IsUnnamed && inline is not null)
            {
                // Promoted: the runtime splices the anonymous composite's members into the parent value.
                this.AppendMembers(owner, inline, compiled, memberNames);
                continue;
            }

            string propertyName = Naming.ToCSharp(field.Name, this.keepNames);
            if (memberNames.TryGetValue(propertyName, out string? other))
            {
                this.collisions.Add($"Members '{other}' and '{field.Name}' of '{owner.LayoutName}' would both be generated as '{propertyName}'; use [CStructLayout(KeepNames = true)] or rename one in the layout.");
                continue;
            }

            memberNames[propertyName] = field.Name;
            CompiledCompositeType? target = field.TargetComposite ?? inline;
            if (target is not null && this.Find(target) is null)
            {
                this.AddComposite(target.Name.Length == 0 ? field.Name : target.Name, target, isDeclared: false, preferredName: owner.Name + propertyName);
            }

            owner.Members.Add(this.Describe(field, propertyName));
        }
    }

    /// <summary>The C# shape of a field (its class, enum, and property type); the composites it refers to must already exist.</summary>
    public GeneratedMember Describe(CompiledField field, string propertyName)
    {
        CompiledCompositeType? target = field.TargetComposite;
        GeneratedComposite? memberComposite = target is null ? null : this.Find(target);
        GeneratedEnum? memberEnum = field.Type.Symbol.Definition is CompiledEnumType compiledEnum ? this.Find(compiledEnum) : null;
        return new GeneratedMember(field, propertyName, memberComposite, memberEnum, this.TypeNameOf(field, memberComposite, memberEnum));
    }

    /// <summary>The shape of a pointer field's value, for the pointer readers.</summary>
    public GeneratedMember DescribePointer(CompiledField field) => this.Describe(field, Naming.ToCSharp(field.Name.Length == 0 ? "target" : field.Name, this.keepNames));

    /// <summary>The C# property type per plan §1.4: pointers are <c>Pointer&lt;T&gt;</c>, arrays <c>T[]</c> (jagged for several dimensions), character arrays <c>string</c>, bitfields their declared integer type.</summary>
    private string TypeNameOf(CompiledField field, GeneratedComposite? composite, GeneratedEnum? memberEnum)
    {
        string element;
        if (field.PointerDepth > 0)
        {
            element = this.PointerTypeName(field, composite, memberEnum);
        }
        else if (composite is not null)
        {
            element = composite.Name;
        }
        else if (memberEnum is not null)
        {
            element = memberEnum.Name;
        }
        else if (field.Type.Symbol.IsCustomCodec)
        {
            element = "object?";
        }
        else
        {
            element = PrimitiveTypeName(field.Codec.Kind);
        }

        if (field.IsCharacterArray && field.Array.Kind != CompiledArrayKind.Scalar)
        {
            // char[N] and wchar[N] read as one string; a table char[R][N] as rows of strings.
            element = "string";
            return WrapArray(element, field.Array.Dimensions.Length - 1);
        }

        if (field.PointerDepth == 0 && field.Array.Kind != CompiledArrayKind.Scalar && BoundedTextCodec.IsType(field.TypeSpelling))
        {
            // An encoded text buffer (utf8[N], latin1[N], ...) reads as one string whatever its dimensions.
            return "string";
        }

        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            return element;
        }

        return WrapArray(element, field.Array.Dimensions.Length == 0 ? 1 : field.Array.Dimensions.Length);
    }

    private string PointerTypeName(CompiledField field, GeneratedComposite? composite, GeneratedEnum? memberEnum)
    {
        string target;
        if (composite is not null)
        {
            target = composite.Name;
        }
        else if (memberEnum is not null)
        {
            target = memberEnum.Name;
        }
        else if (field.IsCharElement || field.IsWideCharElement)
        {
            // The pointer-to-char shorthand reads a terminated string at the target.
            target = "string";
        }
        else if (field.Type.Symbol.Name == "void")
        {
            target = "object";
        }
        else
        {
            // A pointer field carries no codec of its own; its target type's canonical name resolves the codec.
            target = PrimitiveTypeName(PrimitiveCodec.Resolve(field.Type.Symbol.Name, field.LayoutLittleEndian).Kind);
        }

        for (int depth = 0; depth < field.PointerDepth; depth++)
        {
            target = "global::CStructSharp.Generated.Pointer<" + target + ">";
        }

        return target;
    }

    private static string WrapArray(string element, int dimensions)
    {
        for (int dimension = 0; dimension < dimensions; dimension++)
        {
            element += "[]";
        }

        return element;
    }
}

/// <summary>A layout enum and its C# enum.</summary>
internal sealed record GeneratedEnum(string Name, string LayoutName, CompiledEnumType Compiled, string UnderlyingType, IReadOnlyList<GeneratedEnumMember> Members);

/// <summary>One enum member with its value in the enum's own integer domain.</summary>
internal sealed record GeneratedEnumMember(string Name, string LayoutName, System.Numerics.BigInteger Value);

/// <summary>A struct or union and its C# class.</summary>
internal sealed class GeneratedComposite
{
    public GeneratedComposite(string name, string layoutName, CompiledCompositeType composite, bool isDeclared)
    {
        this.Name = name;
        this.LayoutName = layoutName;
        this.Composite = composite;
        this.IsDeclared = isDeclared;
    }

    public string Name { get; }

    public string LayoutName { get; }

    public CompiledCompositeType Composite { get; }

    /// <summary>Whether the composite is a top-level declaration (a root candidate) rather than an inline type.</summary>
    public bool IsDeclared { get; }

    public bool IsUnion => this.Composite.IsUnion;

    public List<GeneratedMember> Members { get; } = new();
}

/// <summary>One property of a generated class and the compiled field it holds.</summary>
internal sealed record GeneratedMember(CompiledField Field, string PropertyName, GeneratedComposite? Composite, GeneratedEnum? Enum, string TypeName)
{
    public string LayoutName => this.Field.Name;

    public bool IsConditional => this.Field.Declaration.Condition is not null;
}
