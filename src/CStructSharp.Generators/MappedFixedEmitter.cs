namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;

/// <summary>
///     The direct members of a layout-bound mapped class (<c>ICStructFixedMapped&lt;TSelf&gt;</c>): for a
///     <c>[CStructMapped(Layout = "...")]</c> class whose layout struct has a build-time offset for every member,
///     <c>TryReadFixed</c> reads each property from its member's constant offset and <c>TryWriteFixed</c> stores it
///     there, and <c>FixedLayoutFingerprint</c> names the struct they were generated for. The runtime uses them only
///     for a layout with the same fingerprint, so they must produce exactly what the class's <c>ReadFrom</c> and
///     <c>WriteTo</c> produce over that struct: every property type and member kind below is one for which the
///     by-name conversion is the identity, and anything else leaves the class without direct members (or, for the
///     writer alone, with a writer that always declines).
/// </summary>
internal static class MappedFixedEmitter
{
    private const string Codec = "global::CStructSharp.Generated.Codec";
    private const string Mapped = "global::CStructSharp.MappedTypes";

    /// <summary>Plans the direct members of <paramref name="request"/> against <paramref name="composite"/>, or returns <see langword="null"/> when the class cannot have them.</summary>
    /// <param name="request">The mapped class.</param>
    /// <param name="composite">The layout struct its <c>Layout</c> names.</param>
    /// <returns>The plan, or <see langword="null"/>.</returns>
    public static FixedMappedPlan? Plan(MappedRequest request, CompiledCompositeType? composite)
    {
        if (composite is null || !IsFixed(composite, 0))
        {
            return null;
        }

        IReadOnlyList<string> names = composite.Shape.Names;
        var members = new List<FixedMappedMember>();
        foreach (MappedMember member in request.Members)
        {
            if (member.Kind != MappedMemberKind.Converted || CStructMappedGenerator.ResolveLayoutName(member, names) is not { } layoutName ||
                !TryFindMember(composite, layoutName, 0, out CompiledField? field, out int offset))
            {
                return null;
            }

            var planned = new FixedMappedMember(member, layoutName, field, offset);
            if (ReadExpression(planned, "source", out _) is null && !IsLocalRead(planned))
            {
                return null;
            }

            members.Add(planned);
        }

        // The writer must supply every member of the struct exactly once, in types it stores without a failure.
        bool writable = names.Where(name => name.Length > 0).All(name => members.Count(member => member.LayoutName == name) == 1) &&
                        members.All(CanWrite);
        return new FixedMappedPlan(composite.Symbol.FixedSize!.Value, LayoutFingerprint.Compute(composite), members, writable);
    }

    /// <summary>Emits <c>FixedLayoutFingerprint</c>, <c>TryReadFixed</c> and <c>TryWriteFixed</c> into the class body.</summary>
    /// <param name="writer">The generated source, inside the class.</param>
    /// <param name="request">The mapped class.</param>
    /// <param name="plan">Its plan.</param>
    public static void Emit(SourceWriter writer, MappedRequest request, FixedMappedPlan plan)
    {
        string name = request.ClassName;
        bool isReference = request.TypeKeyword is "class" or "record";
        writer.Line();
        writer.Line("/// <summary>Gets the fingerprint of the layout struct <c>" + request.Layout + "</c> the direct members below were generated for.</summary>");
        writer.Line("public static ulong FixedLayoutFingerprint => " + Hex(plan.Fingerprint) + ";");
        writer.Line();
        writer.Line("/// <summary>Reads an instance from the struct's " + Int(plan.Size) + " bytes, each property from its member's constant offset: what <see cref=\"ReadFrom\"/> reads from the parsed struct.</summary>");
        writer.Line("/// <param name=\"source\">The struct's bytes.</param>");
        writer.Line("/// <param name=\"trimFixedText\">Whether fixed-capacity text drops its trailing NUL padding.</param>");
        writer.Line("/// <param name=\"value\">The instance when the method returns <see langword=\"true\"/>.</param>");
        writer.Line("/// <returns><see langword=\"false\"/> when a nested mapped class cannot be read directly.</returns>");
        writer.Open("public static bool TryReadFixed(global::System.ReadOnlySpan<byte> source, bool trimFixedText, [global::System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] out " + name + " value)");
        writer.Line("value = default!;");
        writer.Open("if (source.Length < " + Int(plan.Size) + ")");
        writer.Line("return false;");
        writer.Close();
        var assignments = new List<string>();
        for (int index = 0; index < plan.Members.Count; index++)
        {
            FixedMappedMember member = plan.Members[index];
            string property = member.Member.PropertyName;
            if (ReadExpression(member, "source", out _) is { } expression)
            {
                assignments.Add(property + " = " + expression + ",");
                continue;
            }

            string local = "member" + Int(index);
            EmitLocalRead(writer, member, local);
            assignments.Add(property + " = " + local + ",");
        }

        writer.Line("value = new " + name);
        writer.Line("{");
        writer.Indent();
        foreach (string assignment in assignments)
        {
            writer.Line(assignment);
        }

        writer.Outdent();
        writer.Line("};");
        writer.Line("return true;");
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>Writes an instance into the struct's cleared bytes, each property at its member's constant offset: the bytes <see cref=\"WriteTo\"/> leads the writer to.</summary>");
        writer.Line("/// <param name=\"value\">The instance.</param>");
        writer.Line("/// <param name=\"target\">The struct's bytes, already zero.</param>");
        writer.Line("/// <returns><see langword=\"false\"/> when the instance must be written member by member (a null or wrongly sized member, a nested class without direct members" + (plan.Writable ? string.Empty : ", or a member this class cannot store directly") + ").</returns>");
        writer.Open("public static bool TryWriteFixed(" + name + " value, global::System.Span<byte> target)");
        if (!plan.Writable)
        {
            writer.Line("return false;");
            writer.Close();
            return;
        }

        writer.Open("if (" + (isReference ? "value is null || " : string.Empty) + "target.Length < " + Int(plan.Size) + ")");
        writer.Line("return false;");
        writer.Close();
        for (int index = 0; index < plan.Members.Count; index++)
        {
            EmitWrite(writer, plan.Members[index], "value." + plan.Members[index].Member.PropertyName, "member" + Int(index));
        }

        writer.Line("return true;");
        writer.Close();
    }

    /// <summary>
    ///     Whether a composite is fixed as the runtime's static plan requires: a struct of fixed size, each member at a
    ///     build-time offset, unconditional, neither a pointer nor a bitfield, with one-dimensional fixed arrays and
    ///     fixed nested structs. The runtime checks its own plan too; this only decides whether to emit.
    /// </summary>
    private static bool IsFixed(CompiledCompositeType composite, int depth)
    {
        if (depth > FixedLayoutRule.MaximumNestingDepth || !FixedLayoutRule.IsFixedComposite(composite))
        {
            return false;
        }

        foreach (CompiledField field in composite.Fields)
        {
            if (!FixedLayoutRule.IsFixedMember(field) ||
                (field.Composite is { } nested && !IsFixed(nested, depth + 1)) ||
                (field.Enum is { IsFlag: true }))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Finds a member by name, looking through anonymous promoted structs, with its offset from the composite's start.</summary>
    private static bool TryFindMember(CompiledCompositeType composite, string name, int depth, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CompiledField? found, out int offset)
    {
        foreach (CompiledField field in composite.Fields)
        {
            if (field.Name == name && field.FixedOffset is int own)
            {
                found = field;
                offset = own;
                return true;
            }

            if (depth < FixedLayoutRule.MaximumNestingDepth && composite.PromotedFields.Contains(field) && field.Composite is { IsUnion: false } promoted && field.FixedOffset is int outer &&
                TryFindMember(promoted, name, depth + 1, out found, out int inner))
            {
                offset = outer + inner;
                return true;
            }
        }

        found = null;
        offset = 0;
        return false;
    }

    /// <summary>
    ///     The expression reading a scalar member into its property, or <see langword="null"/> for a member read into a
    ///     local first (arrays and nested classes) or one the direct reader does not handle.
    /// </summary>
    private static string? ReadExpression(FixedMappedMember member, string source, out string? keyword)
    {
        CompiledField field = member.Field;
        string type = member.Member.FixedType;
        keyword = null;
        if (field.Array.Kind != CompiledArrayKind.Scalar || field.Composite is not null)
        {
            if (type == "s" && field.IsCharacterArray && !field.IsWideCharElement && field.Codec.Size == 1 && field.Array.FixedCount is int length && !member.Member.IsNullableValue)
            {
                // ReadFrom's Get<string> of a char[N]: one Latin-1 character per byte, TrimFixedText applied.
                return Codec + ".DecodeFixedText(" + source + ".Slice(" + Int(member.Offset) + ", " + Int(length) + "), trimFixedText)";
            }

            return null;
        }

        if (type.StartsWith("p:", StringComparison.Ordinal) && field.Enum is null && NaturalKeyword(field.Codec) == type.Substring(2))
        {
            keyword = type.Substring(2);
            return Decode(field.Codec, source, member.Offset);
        }

        if (type.StartsWith("e:", StringComparison.Ordinal) && field.Enum is { IsFlag: false } && NaturalKeyword(field.Codec) == type.Substring(2))
        {
            // ReadFrom's Get<TEnum> converts the stored number, whose type is the enum's underlying type, unchanged.
            return "(" + member.Member.TypeName + ")" + Decode(field.Codec, source, member.Offset);
        }

        return null;
    }

    /// <summary>Whether a member is read into a local before the object is built: a nested mapped class, or an array of numbers or of mapped classes.</summary>
    private static bool IsLocalRead(FixedMappedMember member)
    {
        CompiledField field = member.Field;
        string type = member.Member.FixedType;
        if (member.Member.IsNullableValue)
        {
            return false;
        }

        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            return (type is "c" or "cs") && field.Composite is not null;
        }

        if (field.Array.FixedCount is null || field.Enum is not null || field.IsCharacterArray)
        {
            return false;
        }

        if (type.StartsWith("ap:", StringComparison.Ordinal))
        {
            return field.Composite is null && NaturalKeyword(field.Codec) == type.Substring(3);
        }

        return (type.StartsWith("ac:", StringComparison.Ordinal) || type.StartsWith("acs:", StringComparison.Ordinal)) &&
               field.Composite is { } nested && field.FixedElementSize == nested.Symbol.FixedSize;
    }

    /// <summary>Emits the statements reading a nested class or an array member into <paramref name="local"/>.</summary>
    private static void EmitLocalRead(SourceWriter writer, FixedMappedMember member, string local)
    {
        CompiledField field = member.Field;
        string type = member.Member.FixedType;
        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            // A nested mapped class reads directly only when it was generated for this very nested struct.
            CompiledCompositeType nested = field.Composite!;
            writer.Open("if (!" + Mapped + ".TryReadFixed<" + member.Member.TypeName + ">(source.Slice(" + Int(member.Offset) + ", " + Int(nested.Symbol.FixedSize!.Value) + "), " + Hex(LayoutFingerprint.Compute(nested)) + ", trimFixedText, out " + member.Member.TypeName + (type == "c" ? "?" : string.Empty) + " " + local + "))");
            writer.Line("return false;");
            writer.Close();
            return;
        }

        int count = field.Array.FixedCount!.Value;
        if (type.StartsWith("ap:", StringComparison.Ordinal))
        {
            string keyword = type.Substring(3);
            writer.Line("var " + local + " = new " + keyword + "[" + Int(count) + "];");
            string bytes = "source.Slice(" + Int(member.Offset) + ", " + Int(count * field.Codec.Size) + ")";
            writer.Line(field.Codec.Kind switch
            {
                PrimitiveCodecKind.UInt8 => bytes + ".CopyTo(" + local + ");",
                PrimitiveCodecKind.Int8 => bytes + ".CopyTo(global::System.Runtime.InteropServices.MemoryMarshal.AsBytes<sbyte>(new global::System.Span<sbyte>(" + local + ")));",
                PrimitiveCodecKind.Bool => Codec + ".DecodeBooleans(" + bytes + ", " + local + ");",
                PrimitiveCodecKind.Int24 => Codec + ".DecodeInt24(" + bytes + ", " + local + ", " + Bool(field.Codec.LittleEndian) + ");",
                PrimitiveCodecKind.UInt24 => Codec + ".DecodeUInt24(" + bytes + ", " + local + ", " + Bool(field.Codec.LittleEndian) + ");",
                _ => Codec + ".DecodeIntegers<" + keyword + ">(" + bytes + ", " + local + ", " + Bool(field.Codec.LittleEndian) + ");",
            });
            return;
        }

        string element = type.Substring(type.IndexOf(':') + 1);
        CompiledCompositeType elementComposite = field.Composite!;
        int stride = field.FixedElementSize!.Value;
        writer.Line("var " + local + " = new " + element + "[" + Int(count) + "];");
        writer.Open("for (int index = 0; index < " + Int(count) + "; index++)");
        writer.Open("if (!" + Mapped + ".TryReadFixed<" + element + ">(source.Slice(" + Int(member.Offset) + " + index * " + Int(stride) + ", " + Int(stride) + "), " + Hex(LayoutFingerprint.Compute(elementComposite)) + ", trimFixedText, out " + element + (type.StartsWith("ac:", StringComparison.Ordinal) ? "?" : string.Empty) + " item))");
        writer.Line("return false;");
        writer.Close();
        writer.Line(local + "[index] = item;");
        writer.Close();
    }

    /// <summary>Whether the direct writer can store a member: numbers that encode without a range check, nested mapped classes, and arrays of either.</summary>
    private static bool CanWrite(FixedMappedMember member)
    {
        CompiledField field = member.Field;
        string type = member.Member.FixedType;
        bool storable = field.Codec.Kind is not (PrimitiveCodecKind.Int24 or PrimitiveCodecKind.UInt24);
        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            return type is "c" or "cs"
                       ? field.Composite is not null
                       : storable && field.Enum is null && field.Composite is null && type.StartsWith("p:", StringComparison.Ordinal) && NaturalKeyword(field.Codec) == type.Substring(2);
        }

        return !member.Member.IsNullableValue && IsLocalRead(member) && (!type.StartsWith("ap:", StringComparison.Ordinal) || storable);
    }

    /// <summary>Emits the statements storing one member; a member the writer cannot store returns <see langword="false"/> at run time.</summary>
    private static void EmitWrite(SourceWriter writer, FixedMappedMember member, string access, string local)
    {
        CompiledField field = member.Field;
        string type = member.Member.FixedType;
        string offset = Int(member.Offset);
        writer.Line("// " + member.LayoutName);
        if (field.Array.Kind == CompiledArrayKind.Scalar && type.StartsWith("p:", StringComparison.Ordinal))
        {
            if (member.Member.IsNullableValue)
            {
                // WriteTo leaves a null member out, and the writer then reports it as missing.
                writer.Open("if (" + access + " is not { } " + local + ")");
                writer.Line("return false;");
                writer.Close();
                access = local;
            }

            writer.Line(Encode(field.Codec, "target", member.Offset, access));
            return;
        }

        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            CompiledCompositeType nested = field.Composite!;
            string call = Mapped + ".TryWriteFixed<" + member.Member.TypeName + ">(" + access + ", target.Slice(" + offset + ", " + Int(nested.Symbol.FixedSize!.Value) + "), " + Hex(LayoutFingerprint.Compute(nested)) + ")";
            writer.Open("if (" + (type == "c" ? access + " is null || " : string.Empty) + "!" + call + ")");
            writer.Line("return false;");
            writer.Close();
            return;
        }

        int count = field.Array.FixedCount!.Value;
        writer.Open("if (" + access + " is null || " + access + ".Length != " + Int(count) + ")");
        writer.Line("return false;");
        writer.Close();
        if (type.StartsWith("ap:", StringComparison.Ordinal))
        {
            string keyword = type.Substring(3);
            string bytes = "target.Slice(" + offset + ", " + Int(count * field.Codec.Size) + ")";
            writer.Line(field.Codec.Kind switch
            {
                PrimitiveCodecKind.UInt8 => "new global::System.ReadOnlySpan<byte>(" + access + ").CopyTo(" + bytes + ");",
                PrimitiveCodecKind.Int8 => "global::System.Runtime.InteropServices.MemoryMarshal.AsBytes<sbyte>(new global::System.ReadOnlySpan<sbyte>(" + access + ")).CopyTo(" + bytes + ");",
                PrimitiveCodecKind.Bool => "for (int index = 0; index < " + Int(count) + "; index++) { target[" + offset + " + index] = (byte)(" + access + "[index] ? 1 : 0); }",
                _ => Codec + ".EncodeIntegers<" + keyword + ">(" + access + ", " + bytes + ", " + Bool(field.Codec.LittleEndian) + ");",
            });
            return;
        }

        string element = type.Substring(type.IndexOf(':') + 1);
        int stride = field.FixedElementSize!.Value;
        writer.Open("for (int index = 0; index < " + Int(count) + "; index++)");
        writer.Open("if (" + (type.StartsWith("ac:", StringComparison.Ordinal) ? access + "[index] is null || " : string.Empty) + "!" + Mapped + ".TryWriteFixed<" + element + ">(" + access + "[index], target.Slice(" + offset + " + index * " + Int(stride) + ", " + Int(stride) + "), " + Hex(LayoutFingerprint.Compute(field.Composite!)) + "))");
        writer.Line("return false;");
        writer.Close();
        writer.Close();
    }

    /// <summary>The C# keyword of the type a fixed-width numeric codec decodes to (the runtime's natural value type), or <see langword="null"/>.</summary>
    private static string? NaturalKeyword(PrimitiveCodec codec)
    {
        return codec.Kind switch
        {
            PrimitiveCodecKind.UInt8 => "byte",
            PrimitiveCodecKind.Int8 => "sbyte",
            PrimitiveCodecKind.Bool => "bool",
            PrimitiveCodecKind.Int16 => "short",
            PrimitiveCodecKind.UInt16 => "ushort",
            PrimitiveCodecKind.Int24 or PrimitiveCodecKind.Int32 => "int",
            PrimitiveCodecKind.UInt24 or PrimitiveCodecKind.UInt32 => "uint",
            PrimitiveCodecKind.Int64 => "long",
            PrimitiveCodecKind.UInt64 => "ulong",
            PrimitiveCodecKind.Float32 => "float",
            PrimitiveCodecKind.Float64 => "double",
            _ => null,
        };
    }

    /// <summary>The expression decoding one fixed-width number at a constant offset.</summary>
    private static string Decode(PrimitiveCodec codec, string source, int offset)
    {
        string span = source + ".Slice(" + Int(offset) + ", " + Int(codec.Size) + ")";
        string le = Bool(codec.LittleEndian);
        return codec.Kind switch
        {
            PrimitiveCodecKind.UInt8 => source + "[" + Int(offset) + "]",
            PrimitiveCodecKind.Int8 => "unchecked((sbyte)" + source + "[" + Int(offset) + "])",
            PrimitiveCodecKind.Bool => "(" + source + "[" + Int(offset) + "] != 0)",
            PrimitiveCodecKind.Int16 => Codec + ".ReadInt16(" + span + ", " + le + ")",
            PrimitiveCodecKind.UInt16 => Codec + ".ReadUInt16(" + span + ", " + le + ")",
            PrimitiveCodecKind.Int24 => Codec + ".ReadInt24(" + span + ", " + le + ")",
            PrimitiveCodecKind.UInt24 => Codec + ".ReadUInt24(" + span + ", " + le + ")",
            PrimitiveCodecKind.Int32 => Codec + ".ReadInt32(" + span + ", " + le + ")",
            PrimitiveCodecKind.UInt32 => Codec + ".ReadUInt32(" + span + ", " + le + ")",
            PrimitiveCodecKind.Int64 => Codec + ".ReadInt64(" + span + ", " + le + ")",
            PrimitiveCodecKind.UInt64 => Codec + ".ReadUInt64(" + span + ", " + le + ")",
            PrimitiveCodecKind.Float32 => Codec + ".ReadSingle(" + span + ", " + le + ")",
            PrimitiveCodecKind.Float64 => Codec + ".ReadDouble(" + span + ", " + le + ")",
            _ => throw new InvalidOperationException("Not a fixed-width number: " + codec.Kind),
        };
    }

    /// <summary>The statement encoding one fixed-width number at a constant offset.</summary>
    private static string Encode(PrimitiveCodec codec, string target, int offset, string access)
    {
        string span = target + ".Slice(" + Int(offset) + ", " + Int(codec.Size) + ")";
        string le = Bool(codec.LittleEndian);
        return codec.Kind switch
        {
            PrimitiveCodecKind.UInt8 => target + "[" + Int(offset) + "] = " + access + ";",
            PrimitiveCodecKind.Int8 => target + "[" + Int(offset) + "] = unchecked((byte)" + access + ");",
            PrimitiveCodecKind.Bool => target + "[" + Int(offset) + "] = (byte)(" + access + " ? 1 : 0);",
            PrimitiveCodecKind.Int16 => Codec + ".WriteInt16(" + span + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.UInt16 => Codec + ".WriteUInt16(" + span + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.Int32 => Codec + ".WriteInt32(" + span + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.UInt32 => Codec + ".WriteUInt32(" + span + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.Int64 => Codec + ".WriteInt64(" + span + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.UInt64 => Codec + ".WriteUInt64(" + span + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.Float32 => Codec + ".WriteSingle(" + span + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.Float64 => Codec + ".WriteDouble(" + span + ", " + access + ", " + le + ");",
            _ => throw new InvalidOperationException("Not a directly stored number: " + codec.Kind),
        };
    }

    /// <summary>An integer as a C# literal.</summary>
    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>A boolean as a C# literal.</summary>
    private static string Bool(bool value) => value ? "true" : "false";

    /// <summary>A fingerprint as a hexadecimal <c>ulong</c> literal.</summary>
    private static string Hex(ulong value) => "0x" + value.ToString("X16", CultureInfo.InvariantCulture) + "UL";
}

/// <summary>The direct members planned for one mapped class: the struct size, its fingerprint, the members, and whether it has a writer.</summary>
/// <param name="Size">The layout struct's size in bytes.</param>
/// <param name="Fingerprint">The layout struct's fingerprint.</param>
/// <param name="Members">The mapped properties with their members and offsets.</param>
/// <param name="Writable">Whether every member of the struct is written by exactly one property the writer can store.</param>
internal sealed record FixedMappedPlan(int Size, ulong Fingerprint, IReadOnlyList<FixedMappedMember> Members, bool Writable);

/// <summary>One mapped property with the layout member it maps to and that member's offset from the struct's start.</summary>
/// <param name="Member">The mapped property.</param>
/// <param name="LayoutName">The layout member's name.</param>
/// <param name="Field">The layout member.</param>
/// <param name="Offset">The member's offset from the struct's start.</param>
internal sealed record FixedMappedMember(MappedMember Member, string LayoutName, CompiledField Field, int Offset);
