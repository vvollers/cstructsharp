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

    private readonly bool views;

    private GeneratedModel(bool keepNames, bool views, Dictionary<string, string> takenNames)
    {
        this.keepNames = keepNames;
        this.takenNames = takenNames;
        this.views = views;
    }

    /// <summary>Gets the C# enums, one per layout enum, in declaration order.</summary>
    public List<GeneratedEnum> Enums { get; } = new();

    /// <summary>
    ///     Gets the C# classes: declared composites in declaration order, followed by the inline composites their
    ///     members introduce.
    /// </summary>
    public List<GeneratedComposite> Composites { get; } = new();

    /// <summary>Gets one CSG003 message per generated name that clashes with another; empty when none do.</summary>
    public IReadOnlyList<string> Collisions => this.collisions;

    /// <summary>Builds the model; <paramref name="takenNames"/> holds the names the class frame already uses (updated with every type name); <paramref name="views"/> reserves the view members.</summary>
    /// <param name="compilation">The compiled layout whose declarations become C# types.</param>
    /// <param name="keepNames">Whether layout identifiers keep their spelling instead of becoming PascalCase.</param>
    /// <param name="takenNames">Maps each claimed C# name to a description of its owner; mutated by the build.</param>
    /// <param name="views">Whether the generated class includes the view types, whose names are then claimed.</param>
    /// <returns>The model with every type and member named; <see cref="Collisions"/> lists the clashes.</returns>
    public static GeneratedModel Build(LayoutCompilation compilation, bool keepNames, Dictionary<string, string> takenNames, bool views)
    {
        var model = new GeneratedModel(keepNames, views, takenNames);
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

        // The view types derived from a composite's name may not spell another generated type (a composite whose own
        // name already collided is left out: one diagnostic names that collision).
        if (views)
        {
            foreach (GeneratedComposite composite in model.Composites)
            {
                if (!takenNames.TryGetValue(composite.Name, out string? owner) || owner != "generated for " + Describe(composite.Composite, composite.LayoutName))
                {
                    continue;
                }

                string what = "the view of '" + composite.LayoutName + "'";
                model.Claim(composite.Name + "View", what, alreadyCSharp: true);
                if (composite.Composite.Symbol.FixedSize is not null)
                {
                    model.Claim(composite.Name + "ViewEnumerable", what, alreadyCSharp: true);
                    model.Claim(composite.Name + "ViewEnumerator", what, alreadyCSharp: true);
                }
            }
        }

        // Each declared composite's buffered forms use a private reader struct. Its name is an implementation detail,
        // so when a layout type already spells it the struct takes a numbered name instead of failing the layout.
        foreach (GeneratedComposite composite in model.Composites)
        {
            if (composite.IsDeclared)
            {
                composite.BufferedReaderName = model.ClaimFree(composite.Name + "BufferedReader", "the buffered reader of '" + composite.LayoutName + "'");
            }
        }

        return model;
    }

    /// <summary>The generated class of a compiled struct or union.</summary>
    /// <param name="composite">The compiled composite, matched by reference.</param>
    /// <returns>The class, or null when the composite has none (for example, a tag aliased by a typedef).</returns>
    public GeneratedComposite? Find(CompiledCompositeType composite) => this.compositesByType.TryGetValue(composite, out GeneratedComposite? generated) ? generated : null;

    /// <summary>The generated C# enum of a compiled layout enum.</summary>
    /// <param name="compiledEnum">The compiled enum, matched by reference.</param>
    /// <returns>The C# enum, or null when the layout enum has none.</returns>
    public GeneratedEnum? Find(CompiledEnumType compiledEnum) => this.enumsByType.TryGetValue(compiledEnum, out GeneratedEnum? generated) ? generated : null;

    /// <summary>The C# storage type of an enum's backing integer.</summary>
    /// <param name="integer">The enum's integer codec, which fixes the bit width and signedness.</param>
    /// <returns>The C# keyword of the matching integer type, from <c>sbyte</c> to <c>ulong</c>.</returns>
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
    /// <param name="kind">The primitive codec kind.</param>
    /// <returns>A C# keyword or a <c>global::</c>-qualified type name.</returns>
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

        string what = Describe(composite, layoutName);
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

    /// <summary>
    ///     Takes the first free C# name among <paramref name="name"/>, <paramref name="name"/>2, <paramref name="name"/>3,
    ///     and so on, for a private generated type whose exact name nothing outside the class depends on.
    /// </summary>
    /// <param name="name">The preferred C# name.</param>
    /// <param name="what">The owner recorded for the name, as collision messages describe it.</param>
    /// <returns>The name taken.</returns>
    private string ClaimFree(string name, string what)
    {
        string candidate = name;
        for (int suffix = 2; this.takenNames.ContainsKey(candidate); suffix++)
        {
            candidate = name + suffix.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        this.takenNames[candidate] = $"generated for {what}";
        return candidate;
    }

    private static string Describe(CompiledCompositeType composite, string layoutName) => (composite.IsUnion ? "the union '" : "the struct '") + layoutName + "'";

    private static string Capitalize(string text) => text.Length == 0 ? text : char.ToUpperInvariant(text[0]) + text.Substring(1);

    private void ResolveMembers(GeneratedComposite generated, CompiledLayoutModel compiled)
    {
        var memberNames = new Dictionary<string, string>(StringComparer.Ordinal) { [generated.Name] = "the class itself" };
        if (generated.IsUnion)
        {
            memberNames["SelectedMember"] = "the generated SelectedMember property";
            memberNames["RawStorage"] = "the generated RawStorage property";
        }

        this.AppendMembers(generated, generated.Composite, compiled, memberNames, conditional: false);
        if (!this.views)
        {
            return;
        }

        // The view has Bytes and ToObject of its own and a <Member>Bytes slice per fixed one-dimensional array; a member
        // the view exposes (statically placed, not conditional) may not spell one of those names.
        foreach (GeneratedMember member in generated.Members)
        {
            CompiledField field = member.Field;
            if (member.IsConditional || field.IsZeroWidthBitfield || (!generated.IsUnion && field.FixedOffset is null))
            {
                continue;
            }

            if (member.PropertyName is "Bytes" or "ToObject")
            {
                this.collisions.Add($"Member '{field.Name}' of '{generated.LayoutName}' would be generated as '{member.PropertyName}', which is already the view's {member.PropertyName} member; use [CStructLayout(KeepNames = true)] or Views = false, or rename the member in the layout.");
            }

            if (field.Array.Kind == CompiledArrayKind.Fixed && field.Array.Dimensions.Length == 1 && field.PointerDepth == 0 && member.Composite is null
                && memberNames.TryGetValue(member.PropertyName + "Bytes", out string? taken))
            {
                this.collisions.Add($"Member '{field.Name}' of '{generated.LayoutName}' needs a view slice '{member.PropertyName}Bytes', which member '{taken}' would also be generated as; use [CStructLayout(Views = false)] or KeepNames, or rename one in the layout.");
            }
        }
    }

    /// <summary>Adds the members of <paramref name="composite"/> to <paramref name="owner"/>; a promoted anonymous composite contributes its own members in place.</summary>
    private void AppendMembers(GeneratedComposite owner, CompiledCompositeType composite, CompiledLayoutModel compiled, Dictionary<string, string> memberNames, bool conditional)
    {
        foreach (CompiledField field in composite.Fields)
        {
            // A member is conditional when it sits in an arm itself or when the promoted composite that carries it does.
            bool memberConditional = conditional || field.ConditionalBranches.Length > 0;
            if (field.IsZeroWidthBitfield || (field.IsUnnamed && !field.IsInlineComposite))
            {
                // A `: 0` separator, an anonymous bitfield, and `_` padding (compiled without a name) have no slot in the runtime's value.
                continue;
            }

            CompiledCompositeType? inline = field.Declaration is Struct inlineDeclaration
                                                ? compiled.Composites.TryGetValue(inlineDeclaration, out CompiledTypeSymbol? inlineSymbol)
                                                      ? inlineSymbol.Definition as CompiledCompositeType
                                                      : field.Type.Symbol.Definition as CompiledCompositeType
                                                : null;
            if (field.IsUnnamed && inline is not null)
            {
                // Promoted: the runtime splices the anonymous composite's members into the parent value.
                this.AppendMembers(owner, inline, compiled, memberNames, memberConditional);
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

            GeneratedMember member = this.Describe(field, propertyName, memberConditional);
            if (memberConditional)
            {
                // The presence flag of a conditional member; it must not collide with another member's property.
                string flag = member.HasFlagName;
                if (memberNames.TryGetValue(flag, out string? taken))
                {
                    this.collisions.Add($"Member '{field.Name}' of '{owner.LayoutName}' needs a presence flag '{flag}', which member '{taken}' would also be generated as; use [CStructLayout(KeepNames = true)] or rename one in the layout.");
                    continue;
                }

                memberNames[flag] = field.Name;
            }

            owner.Members.Add(member);
        }
    }

    /// <summary>The C# shape of a field (its class, enum, and property type); the composites it refers to must already exist.</summary>
    /// <param name="field">The compiled field to describe.</param>
    /// <param name="propertyName">The C# property name already chosen for the field.</param>
    /// <param name="conditional">Whether the field is read only when its conditional arm is active.</param>
    /// <returns>The member with its class, enum and C# property type resolved.</returns>
    public GeneratedMember Describe(CompiledField field, string propertyName, bool conditional = false)
    {
        CompiledCompositeType? target = field.TargetComposite;
        GeneratedComposite? memberComposite = target is null ? null : this.Find(target);
        GeneratedEnum? memberEnum = field.Type.Symbol.Definition is CompiledEnumType compiledEnum ? this.Find(compiledEnum) : null;
        return new GeneratedMember(field, propertyName, memberComposite, memberEnum, this.TypeNameOf(field, memberComposite, memberEnum), conditional);
    }

    /// <summary>The shape of a pointer field's value, for the pointer readers.</summary>
    /// <param name="field">The pointer field; an unnamed one is described under the name <c>target</c>.</param>
    /// <returns>The member whose type is the field's <c>Pointer&lt;T&gt;</c> type.</returns>
    public GeneratedMember DescribePointer(CompiledField field) => this.Describe(field, Naming.ToCSharp(field.Name.Length == 0 ? "target" : field.Name, this.keepNames));

    /// <summary>The C# property type of a member: pointers are <c>Pointer&lt;T&gt;</c>, arrays <c>T[]</c> (jagged for several dimensions), character arrays <c>string</c>, bitfields their declared integer type.</summary>
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

    /// <summary>The <c>Pointer&lt;T&gt;</c> type of a pointer field: one wrapper per level around the target type, an array for a counted target.</summary>
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

        if (field.HasCountedTarget && target != "string")
        {
            // A @count target is an array of the pointed-to type; counted characters stay one string.
            target += "[]";
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
    /// <summary>Records the class chosen for one compiled struct or union; members are added afterwards.</summary>
    /// <param name="name">The C# class name.</param>
    /// <param name="layoutName">The declaration's name in the layout.</param>
    /// <param name="composite">The compiled struct or union.</param>
    /// <param name="isDeclared">Whether it is a top-level declaration rather than an inline type.</param>
    public GeneratedComposite(string name, string layoutName, CompiledCompositeType composite, bool isDeclared)
    {
        this.Name = name;
        this.LayoutName = layoutName;
        this.Composite = composite;
        this.IsDeclared = isDeclared;
    }

    /// <summary>Gets the C# class name.</summary>
    public string Name { get; }

    /// <summary>Gets the declaration's name in the layout, as diagnostics and runtime paths spell it.</summary>
    public string LayoutName { get; }

    /// <summary>Gets the compiled struct or union with its field offsets and sizes.</summary>
    public CompiledCompositeType Composite { get; }

    /// <summary>Whether the composite is a top-level declaration (a root candidate) rather than an inline type.</summary>
    public bool IsDeclared { get; }

    /// <summary>
    ///     Gets or sets the name of the private struct the buffered <c>Parse</c> forms of a declared composite run
    ///     (<c>IBufferedReader</c>); assigned once every type has its name, empty for an inline composite.
    /// </summary>
    public string BufferedReaderName { get; set; } = string.Empty;

    /// <summary>Gets a value indicating whether the composite is a union, whose members share offset 0.</summary>
    public bool IsUnion => this.Composite.IsUnion;

    /// <summary>Gets the properties a reader fills, in layout order, with promoted anonymous members inlined.</summary>
    public List<GeneratedMember> Members { get; } = new();
}

/// <summary>One property of a generated class and the compiled field it holds.</summary>
internal sealed record GeneratedMember(CompiledField Field, string PropertyName, GeneratedComposite? Composite, GeneratedEnum? Enum, string TypeName, bool IsConditional)
{
    /// <summary>Gets the member's name in the layout.</summary>
    public string LayoutName => this.Field.Name;

    /// <summary>The presence flag of a conditional member: <c>HasValue</c> for <c>value</c>.</summary>
    public string HasFlagName => "Has" + this.PropertyName.TrimStart('@');

    /// <summary>Whether the property is a reference type; a conditional one is then declared nullable and left null when its arm is inactive.</summary>
    public bool IsReferenceType => this.TypeName == "string" || this.TypeName.EndsWith("[]", System.StringComparison.Ordinal) || (this.Composite is not null && this.Field.PointerDepth == 0);
}
