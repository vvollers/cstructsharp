namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;

/// <summary>
///     The operations beside reading and writing: the root class's <c>ICStructGenerated</c> implementation, the
///     <c>Sizes</c>/<c>Offsets</c> constants and typed <c>Update</c> setters of statically placed members, and the
///     path-based operations (<c>ParseWithDebug</c>, <c>ResolveAddress</c>, <c>GetArrayLength</c>, <c>Update</c>)
///     that run on the runtime layout.
/// </summary>
internal sealed partial class LayoutEmitter
{
    private void EmitOperations(SourceWriter writer)
    {
        GeneratedComposite? root = this.model.Composites.FirstOrDefault(composite => composite.LayoutName == this.rootName);
        if (root is not null)
        {
            this.EmitGeneratedInterface(writer, root);
            this.EmitPathOperations(writer, root);
        }

        this.EmitSizes(writer);
        if (root is not null)
        {
            var paths = new List<StaticMember>();
            CollectStaticMembers(root, string.Empty, 0, paths, 0);
            this.EmitOffsets(writer, root, paths);
            this.EmitUpdateSetters(writer, root, paths);
        }
    }

    /// <summary>The root class implements <c>ICStructGenerated&lt;Root&gt;</c> so generic code can accept any generated root.</summary>
    private void EmitGeneratedInterface(SourceWriter writer, GeneratedComposite root)
    {
        string name = root.Name;
        string owner = this.request.ClassName;
        string contract = "global::CStructSharp.ICStructGenerated<" + name + ">";
        writer.Line();
        writer.Line("/// <summary>The root declaration as <see cref=\"" + contract.Replace('<', '{').Replace('>', '}') + "\"/>.</summary>");
        writer.Open("public sealed partial class " + name + " : " + contract);
        writer.Line("/// <inheritdoc/>");
        writer.Line("static global::CStructSharp.CStruct " + contract + ".Layout => " + owner + ".Layout;");
        writer.Line();
        writer.Line("/// <inheritdoc/>");
        writer.Line("static string " + contract + ".RootName => " + owner + ".RootName;");
        writer.Line();
        writer.Line("/// <inheritdoc/>");
        writer.Line("static " + name + " " + contract + ".Parse(global::System.ReadOnlySpan<byte> source, global::CStructSharp.ReadOptions? options) => " + owner + ".Parse(source, options);");
        writer.Line();
        writer.Line("/// <inheritdoc/>");
        writer.Line("static int " + contract + ".Serialize(" + name + " value, global::System.Span<byte> destination, global::CStructSharp.WriteOptions? options) => " + owner + ".Serialize(value, destination, options);");
        writer.Close();
    }

    /// <summary>The path-based operations run on the runtime layout: they take the runtime's path grammar and report as the runtime does.</summary>
    private void EmitPathOperations(SourceWriter writer, GeneratedComposite root)
    {
        string name = root.Name;
        string layout = SourceWriter.Literal(root.LayoutName);
        this.EmitMappedBridge(writer, root);
        writer.Line();
        writer.Line("/// <summary>Reads the root declaration with the generated reader and the runtime's debug ranges (the runtime reads the same bytes once more to produce them).</summary>");
        writer.Line("/// <param name=\"source\">The bytes; offset 0 is coordinate zero.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The read options; <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Line("/// <returns>The parsed value and one <see cref=\"global::CStructSharp.Diagnostics.DebugData\"/> per value read, in read order.</returns>");
        writer.Open("public static (" + name + " Value, global::System.Collections.Generic.IReadOnlyList<global::CStructSharp.Diagnostics.DebugData> Debug) ParseWithDebug(global::System.ReadOnlySpan<byte> source, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null)");
        writer.Line(name + " value = Parse" + name + "(source, variables, options);");
        writer.Line("return (value, Layout.ParseWithDebug(source, " + layout + ", variables, options).Debug);");
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>Resolves a path (<c>" + root.LayoutName + ".field</c>, <c>items[2].value</c>, ...) to its byte address with the runtime layout.</summary>");
        writer.Line("/// <param name=\"source\">The bytes; offset 0 is coordinate zero.</param>");
        writer.Line("/// <param name=\"path\">The path, in the runtime's path grammar.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The read options; <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Line("/// <returns>The address of the selected value.</returns>");
        writer.Line("public static long ResolveAddress(global::System.ReadOnlySpan<byte> source, string path, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null) => Layout.ResolveAddress(source, path, variables, options);");
        writer.Line();
        writer.Line("/// <summary>The element count of an array or string selected by a path, with the runtime layout.</summary>");
        writer.Line("/// <param name=\"source\">The bytes; offset 0 is coordinate zero.</param>");
        writer.Line("/// <param name=\"path\">The path, in the runtime's path grammar.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The read options; <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Line("/// <returns>The element count.</returns>");
        writer.Line("public static int GetArrayLength(global::System.ReadOnlySpan<byte> source, string path, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null) => Layout.GetArrayLength(source, path, variables, options);");
        writer.Line();
        writer.Line("/// <summary>Replaces one value in place by path with the runtime layout; statically placed members also have typed setters in <see cref=\"Update\"/>.</summary>");
        writer.Line("/// <param name=\"target\">The bytes holding the value.</param>");
        writer.Line("/// <param name=\"path\">The path, in the runtime's path grammar.</param>");
        writer.Line("/// <param name=\"value\">The new value.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The update options; <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Line("public static void UpdatePath(global::System.Span<byte> target, string path, object value, " + VariablesType + " variables = null, global::CStructSharp.UpdateOptions? options = null) => Layout.Update(target, path, value, variables, options);");
    }

    /// <summary>
    ///     The bridge to mapped classes: a generated value becomes the runtime's <c>StructValue</c> (through its bytes)
    ///     and from there any <c>ICStructMapped</c> class; a mapped instance serializes through the runtime layout.
    /// </summary>
    private void EmitMappedBridge(SourceWriter writer, GeneratedComposite root)
    {
        string name = root.Name;
        string layout = SourceWriter.Literal(root.LayoutName);
        writer.Line();
        writer.Line("/// <summary>The runtime's <see cref=\"global::CStructSharp.Values.StructValue\"/> for a generated value: its bytes, parsed by the runtime layout (every member, in the layout's shape). Pointers keep their addresses and are not followed: the value's bytes hold no targets.</summary>");
        writer.Line("/// <param name=\"value\">The value to convert.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The read options for the runtime parse (limits, text trimming); <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Line("/// <returns>The struct value.</returns>");
        writer.Open("public static global::CStructSharp.Values.StructValue ToStructValue(" + name + " value, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null)");
        writer.Line("byte[] bytes = Serialize" + name + "(value, variables);");
        writer.Line("var effective = new global::CStructSharp.ReadOptions");
        writer.Line("{");
        writer.Indent();
        writer.Line("DereferencePointers = false,");
        writer.Line("AddressingMode = options?.AddressingMode ?? global::CStructSharp.PointerAddressingMode.Absolute,");
        writer.Line("Origin = options?.Origin ?? 0,");
        writer.Line("MaxPointerDepth = options?.MaxPointerDepth ?? 64,");
        writer.Line("MaxPointerTargetBytes = options?.MaxPointerTargetBytes,");
        writer.Line("MaxArrayElements = options?.MaxArrayElements ?? 1_000_000,");
        writer.Line("MaxStringBytes = options?.MaxStringBytes ?? (16L * 1024 * 1024),");
        writer.Line("MaxTotalBytesRead = options?.MaxTotalBytesRead ?? (64L * 1024 * 1024),");
        writer.Line("MaxNestingDepth = options?.MaxNestingDepth ?? 256,");
        writer.Line("TrimFixedText = options?.TrimFixedText ?? false,");
        writer.Outdent();
        writer.Line("};");
        writer.Line("return Layout.Parse(bytes, " + layout + ", variables, effective);");
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>Maps a generated value to a mapped class through the runtime's <see cref=\"global::CStructSharp.Values.StructValue\"/>: <c>ToMapped&lt;MyRoot&gt;(value)</c>.</summary>");
        writer.Line("/// <typeparam name=\"T\">A class implementing <see cref=\"global::CStructSharp.ICStructMapped{TSelf}\"/>.</typeparam>");
        writer.Line("/// <param name=\"value\">The value to map.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <returns>The mapped instance.</returns>");
        writer.Line("public static T ToMapped<T>(" + name + " value, " + VariablesType + " variables = null)");
        writer.Line("    where T : global::CStructSharp.ICStructMapped<T>");
        writer.Line("    => T.ReadFrom(ToStructValue(value, variables));");
        writer.Line();
        writer.Line("/// <summary>Serializes a mapped instance as the root declaration through the runtime layout (its <c>WriteTo</c> supplies the members).</summary>");
        writer.Line("/// <typeparam name=\"T\">A class implementing <see cref=\"global::CStructSharp.ICStructMapped{TSelf}\"/>, registered with <see cref=\"global::CStructSharp.MappedTypes\"/>.</typeparam>");
        writer.Line("/// <param name=\"value\">The instance to write.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The write options; <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Line("/// <returns>The serialized bytes.</returns>");
        writer.Line("public static byte[] SerializeMapped<T>(T value, " + VariablesType + " variables = null, global::CStructSharp.WriteOptions? options = null)");
        writer.Line("    where T : global::CStructSharp.ICStructMapped<T>");
        writer.Line("    => Layout.Serialize(" + layout + ", value!, variables, options);");
        writer.Line();
        writer.Line("/// <summary>Reads the root declaration straight into a mapped class through the runtime layout.</summary>");
        writer.Line("/// <typeparam name=\"T\">A class implementing <see cref=\"global::CStructSharp.ICStructMapped{TSelf}\"/>.</typeparam>");
        writer.Line("/// <param name=\"source\">The bytes; offset 0 is coordinate zero.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The read options; <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Line("/// <returns>The mapped instance.</returns>");
        writer.Line("public static T ParseMapped<T>(global::System.ReadOnlySpan<byte> source, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null)");
        writer.Line("    where T : global::CStructSharp.ICStructMapped<T>");
        writer.Line("    => T.ReadFrom(Layout.Parse(source, " + layout + ", variables, options));");
    }

    /// <summary>The fixed size of every composite that has one, as constants.</summary>
    private void EmitSizes(SourceWriter writer)
    {
        var sized = this.model.Composites.Where(composite => composite.Composite.Symbol.FixedSize is not null).ToList();
        if (sized.Count == 0)
        {
            return;
        }

        writer.Line();
        writer.Line("/// <summary>The size in bytes of every composite with a static size (<c>sizeof</c>); a runtime-sized composite has none.</summary>");
        writer.Open("public static class Sizes");
        bool first = true;
        foreach (GeneratedComposite composite in sized)
        {
            if (!first)
            {
                writer.Line();
            }

            first = false;
            writer.Line("/// <summary><c>sizeof(" + composite.LayoutName + ")</c>.</summary>");
            writer.Line("public const int " + composite.Name + " = " + Int(composite.Composite.Symbol.FixedSize!.Value) + ";");
        }

        writer.Close();
    }

    /// <summary>A statically placed member reachable from the root through statically placed structs: its offset from the root's first byte and its access path.</summary>
    private sealed record StaticMember(GeneratedMember Member, string PathName, string LayoutPath, int Offset, IReadOnlyList<string> Containers);

    private static void CollectStaticMembers(GeneratedComposite composite, string layoutPrefix, int baseOffset, List<StaticMember> members, int depth)
    {
        if (composite.IsUnion || depth > 8)
        {
            return;
        }

        var containers = new List<string>();
        CollectStaticMembers(composite, layoutPrefix, baseOffset, members, depth, containers);
    }

    private static void CollectStaticMembers(GeneratedComposite composite, string layoutPrefix, int baseOffset, List<StaticMember> members, int depth, List<string> containers)
    {
        foreach (GeneratedMember member in composite.Members)
        {
            CompiledField field = member.Field;
            if (member.IsConditional || field.FixedOffset is not { } offset || (field.PointerDepth > 0 && field.Array.Kind != CompiledArrayKind.Scalar))
            {
                continue;
            }

            string layoutPath = layoutPrefix + field.Name;
            int absolute = baseOffset + PromotedOffset(composite, field) + offset;
            if (member.Composite is { IsUnion: false } nested && field.Array.Kind == CompiledArrayKind.Scalar && field.PointerDepth == 0)
            {
                if (depth < 8)
                {
                    containers.Add(member.PropertyName);
                    CollectStaticMembers(nested, layoutPath + ".", absolute, members, depth + 1, containers);
                    containers.RemoveAt(containers.Count - 1);
                }

                continue;
            }

            if (member.Composite is not null || field.Array.Kind is not (CompiledArrayKind.Scalar or CompiledArrayKind.Fixed) || field.IsCharacterArray || field.Array.Dimensions.Length > 1 || BoundedTextCodec.IsType(field.TypeSpelling))
            {
                continue;
            }

            if (field.PointerDepth == 0 && !IsStaticScalarKind(field.Codec.Kind) && member.Enum is null)
            {
                continue;
            }

            if (field.Array.Kind == CompiledArrayKind.Fixed && (field.Array.FixedCount is null || field.BitSize > 0))
            {
                continue;
            }

            // C# forbids a member named like its enclosing type: `child value` inside `Value` becomes `ValueMember`.
            string pathName = containers.Count > 0 && containers[containers.Count - 1] == member.PropertyName ? member.PropertyName + "Member" : member.PropertyName;
            members.Add(new StaticMember(member, pathName, layoutPath, absolute, containers.ToArray()));
        }
    }

    /// <summary>A promoted anonymous composite's members carry offsets relative to that composite; its own offset in the parent is added.</summary>
    private static int PromotedOffset(GeneratedComposite owner, CompiledField field)
    {
        int extra = 0;
        CompiledCompositeType? holder = FindHolder(owner.Composite, field, ref extra);
        return holder is null ? 0 : extra;
    }

    private static CompiledCompositeType? FindHolder(CompiledCompositeType composite, CompiledField target, ref int offset)
    {
        foreach (CompiledField field in composite.Fields)
        {
            if (ReferenceEquals(field, target))
            {
                return composite;
            }

            if (field.IsUnnamed && field.IsInlineComposite && field.Type.Symbol.Definition is CompiledCompositeType inline && field.FixedOffset is { } inlineOffset)
            {
                int inner = offset + inlineOffset;
                CompiledCompositeType? holder = FindHolder(inline, target, ref inner);
                if (holder is not null)
                {
                    offset = inner;
                    return holder;
                }
            }
        }

        return null;
    }

    private static bool IsStaticScalarKind(PrimitiveCodecKind kind)
        => kind is PrimitiveCodecKind.UInt8 or PrimitiveCodecKind.Int8 or PrimitiveCodecKind.Bool or PrimitiveCodecKind.Char or PrimitiveCodecKind.WChar
               or PrimitiveCodecKind.Int16 or PrimitiveCodecKind.UInt16 or PrimitiveCodecKind.Int24 or PrimitiveCodecKind.UInt24 or PrimitiveCodecKind.Int32 or PrimitiveCodecKind.UInt32
               or PrimitiveCodecKind.Int48 or PrimitiveCodecKind.UInt48 or PrimitiveCodecKind.Int64 or PrimitiveCodecKind.UInt64 or PrimitiveCodecKind.Int128 or PrimitiveCodecKind.UInt128
               or PrimitiveCodecKind.Float16 or PrimitiveCodecKind.Float32 or PrimitiveCodecKind.Float64 or PrimitiveCodecKind.Uuid or PrimitiveCodecKind.Guid
               or PrimitiveCodecKind.Fixed16_16 or PrimitiveCodecKind.UFixed16_16 or PrimitiveCodecKind.Fixed2_30 or PrimitiveCodecKind.UFixed8_8
               or PrimitiveCodecKind.Latin1 or PrimitiveCodecKind.Cp437 or PrimitiveCodecKind.Utf8Unit or PrimitiveCodecKind.Utf16LeUnit or PrimitiveCodecKind.Utf16BeUnit;

    private void EmitOffsets(SourceWriter writer, GeneratedComposite root, List<StaticMember> members)
    {
        if (members.Count == 0)
        {
            return;
        }

        writer.Line();
        writer.Line("/// <summary>The offset from the root's first byte of every statically placed scalar (<c>offsetof</c>), nested by struct member.</summary>");
        writer.Open("public static class Offsets");
        EmitNested(writer, members, Array.Empty<string>(), member =>
        {
            writer.Line("/// <summary><c>offsetof(" + root.LayoutName + ", " + member.LayoutPath + ")</c>" + (member.Member.Field.BitSize > 0 ? " (the storage unit's first byte)" : string.Empty) + ".</summary>");
            writer.Line("public const int " + member.PathName + " = " + Int(member.Offset) + ";");
        });
        writer.Close();
    }

    private void EmitUpdateSetters(SourceWriter writer, GeneratedComposite root, List<StaticMember> members)
    {
        if (members.Count == 0)
        {
            return;
        }

        writer.Line();
        writer.Line("/// <summary>Typed in-place setters for the statically placed scalars, nested by struct member; a fixed array takes the element index.</summary>");
        writer.Open("public static class Update");
        EmitNested(writer, members, Array.Empty<string>(), member => this.EmitSetter(writer, root, member));
        writer.Close();
    }

    /// <summary>Groups members by their container path into nested static classes, emitting the leaves at each level.</summary>
    private static void EmitNested(SourceWriter writer, List<StaticMember> members, IReadOnlyList<string> containers, Action<StaticMember> emitLeaf)
    {
        bool first = true;
        foreach (StaticMember member in members.Where(item => item.Containers.SequenceEqual(containers)))
        {
            if (!first)
            {
                writer.Line();
            }

            first = false;
            emitLeaf(member);
        }

        foreach (string child in members.Where(item => item.Containers.Count > containers.Count && item.Containers.Take(containers.Count).SequenceEqual(containers)).Select(item => item.Containers[containers.Count]).Distinct(StringComparer.Ordinal))
        {
            if (!first)
            {
                writer.Line();
            }

            first = false;
            writer.Line("/// <summary>The members of <c>" + child + "</c>.</summary>");
            writer.Open("public static class " + child);
            EmitNested(writer, members, containers.Concat(new[] { child }).ToList(), emitLeaf);
            writer.Close();
        }
    }

    private void EmitSetter(SourceWriter writer, GeneratedComposite root, StaticMember member)
    {
        CompiledField field = member.Member.Field;
        string type = ElementType(member.Member.TypeName, 1);
        string path = SourceWriter.Literal(root.LayoutName + "." + member.LayoutPath);
        string memberName = SourceWriter.Literal(field.Name);
        string memberType = SourceWriter.Literal(field.DisplayTypeSpelling);
        bool indexed = field.Array.Kind == CompiledArrayKind.Fixed;
        writer.Line("/// <summary>Stores <c>" + member.LayoutPath + "</c>" + (indexed ? "[<paramref name=\"index\"/>]" : string.Empty) + " into <paramref name=\"target\"/> at offset " + Int(member.Offset) + (indexed ? " plus the element's" : string.Empty) + ", leaving every other byte alone.</summary>");
        writer.Line("/// <param name=\"target\">The bytes holding the value.</param>");
        if (indexed)
        {
            writer.Line("/// <param name=\"index\">The element index.</param>");
        }

        writer.Line("/// <param name=\"value\">The new value.</param>");
        writer.Open("public static void " + member.PathName + "(global::System.Span<byte> target, " + (indexed ? "int index, " : string.Empty) + type + " value)");
        if (indexed)
        {
            writer.Open("if ((uint)index >= " + Int(field.Array.FixedCount!.Value) + "u)");
            writer.Line("throw new global::System.ArgumentOutOfRangeException(nameof(index));");
            writer.Close();
        }

        writer.Line("var cursor = " + WriteCursorType + ".ForUpdate(target, null, " + path + ");");
        writer.Open("try");
        if (field.BitSize > 0)
        {
            int unitSize = field.BitUnitSize ?? field.BitStorageSize ?? field.Codec.Size;
            string bits = member.Member.Enum is not null
                              ? "(ulong)(" + UnsignedCounterpart(member.Member.Enum.UnderlyingType) + ")(" + member.Member.Enum.UnderlyingType + ")value"
                              : "value";
            writer.Line("cursor.WriteBits(new global::CStructSharp.Generated.BitfieldSlot(" + Int(member.Offset) + ", " + Int(unitSize) + ", " + Int(field.BitOffset) + "), " + Int(field.BitSize) + ", " + bits + ", " + Bool(field.BitStorageIsLittleEndian ?? true) + ", HighBitFirst, " + memberName + ", " + memberType + ");");
        }
        else
        {
            writer.Line("cursor.Position = " + Int(member.Offset) + (indexed ? " + index * " + Int(field.FixedElementSize ?? field.Codec.Size) : string.Empty) + ";");
            if (field.PointerDepth > 0)
            {
                writer.Line("cursor.WritePointerAddress(value.Address, PointerSize, LittleEndian, " + memberName + ", " + memberType + ");");
            }
            else if (member.Member.Enum is not null)
            {
                PrimitiveCodec storage = PrimitiveCodec.Resolve(member.Member.Enum.Compiled.Integer.StorageType, field.Codec.LittleEndian);
                writer.Line(this.NumericWrite(storage, "(" + member.Member.Enum.UnderlyingType + ")value", memberName, memberType));
            }
            else
            {
                this.EmitPrimitiveWrite(writer, field.Codec, field.Type.Symbol.Name, field.TypeSpelling, "value", memberName, memberType);
            }
        }

        writer.Close();
        writer.Open("catch (global::CStructSharp.Diagnostics.CStructException exception)");
        writer.Line("cursor.Complete(exception);");
        writer.Line("throw;");
        writer.Close();
        writer.Close();
    }
}
