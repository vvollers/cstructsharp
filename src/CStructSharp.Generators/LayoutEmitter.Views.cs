namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;

/// <summary>
///     The views: one <c>readonly ref struct &lt;Type&gt;View</c> per composite over the composite's bytes, with a
///     direct decode at a constant offset for every statically placed scalar, a nested view for a statically placed
///     struct, the raw bytes of a fixed numeric array, and <c>ToObject()</c> for everything else. Nothing is allocated
///     until a string or <c>ToObject()</c> is asked for.
/// </summary>
internal sealed partial class LayoutEmitter
{
    private void EmitViews(SourceWriter writer)
    {
        if (!this.request.Views)
        {
            return;
        }

        foreach (GeneratedComposite composite in this.model.Composites)
        {
            writer.Line();
            this.EmitView(writer, composite);
        }
    }

    private static string ViewName(GeneratedComposite composite) => composite.Name + "View";

    private void EmitView(SourceWriter writer, GeneratedComposite composite)
    {
        string name = ViewName(composite);
        string layout = SourceWriter.Literal(composite.LayoutName);
        int? size = composite.Composite.Symbol.FixedSize;
        writer.Line("/// <summary>");
        writer.Line("///     A zero-allocation view of one <c>" + composite.LayoutName + "</c> over its bytes: each statically placed member decodes");
        writer.Line("///     directly from the span when read; a member the view does not expose (runtime-sized, conditional, or placed after one)");
        writer.Line("///     is reached through <see cref=\"ToObject\"/>. A view cannot leave the method that created it.");
        writer.Line("/// </summary>");
        writer.Open("public readonly ref struct " + name);
        writer.Line("private readonly global::System.ReadOnlySpan<byte> source;");
        writer.Line("private readonly global::CStructSharp.ReadOptions? options;");
        writer.Line();
        writer.Line("/// <summary>Creates a view over <paramref name=\"source\"/>, whose first byte is the value's first byte" + (size is not null ? "; the source must hold the value's " + Int(size.Value) + " bytes" : string.Empty) + ".</summary>");
        writer.Line("/// <param name=\"source\">The bytes, from the value's start; more may follow (pointer targets, the rest of the input).</param>");
        writer.Line("/// <param name=\"options\">The read options <see cref=\"ToObject\"/> uses; <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Open("public " + name + "(global::System.ReadOnlySpan<byte> source, global::CStructSharp.ReadOptions? options = null)");
        if (size is { } fixedSize)
        {
            // The runtime's short-read text for a source that cannot hold the value.
            writer.Open("if (source.Length < " + Int(fixedSize) + ")");
            writer.Line("var cursor = new global::CStructSharp.Generated.ReadCursor(source, options, " + layout + ");");
            writer.Line("global::CStructSharp.Diagnostics.CStructException failure = cursor.Fail(global::CStructSharp.Generated.ReadCursor.ShortReadText(" + Int(fixedSize) + ", source.Length), null, null);");
            writer.Line("cursor.Complete(failure);");
            writer.Line("throw failure;");
            writer.Close();
        }

        writer.Line("this.source = source;");
        writer.Line("this.options = options;");
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>Gets the value's bytes" + (size is not null ? " (its " + Int(size.Value) + " bytes)" : ": the whole source, as the value's extent depends on its contents") + ".</summary>");
        writer.Line("public global::System.ReadOnlySpan<byte> Bytes => this.source" + (size is { } bytes ? ".Slice(0, " + Int(bytes) + ")" : string.Empty) + ";");
        foreach (GeneratedMember member in composite.Members)
        {
            this.EmitViewMember(writer, composite, member);
        }

        writer.Line();
        writer.Line("/// <summary>Reads the whole value with the generated reader (every member, runtime-sized ones included).</summary>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <returns>The value.</returns>");
        writer.Open("public " + composite.Name + " ToObject(" + VariablesType + " variables = null)");
        writer.Line("var cursor = new " + Cursor + "(this.source, this.options, " + layout + ");");
        writer.Open("try");
        writer.Line("return Read" + composite.Name + "(ref cursor, variables, null, null);");
        writer.Close();
        writer.Open("catch (global::CStructSharp.Diagnostics.CStructException exception)");
        writer.Line("cursor.Complete(exception);");
        writer.Line("throw;");
        writer.Close();
        writer.Close();
        writer.Close();
    }

    private void EmitViewMember(SourceWriter writer, GeneratedComposite composite, GeneratedMember member)
    {
        CompiledField field = member.Field;
        int? offset = composite.IsUnion ? 0 : field.FixedOffset;
        if (member.IsConditional || offset is null || field.IsZeroWidthBitfield)
        {
            return;
        }

        int start = offset.Value + (composite.IsUnion ? 0 : PromotedOffset(composite, field));
        string bytes = "this.source";
        string summary = "/// <summary><c>" + DescribeDeclaration(field) + "</c> at offset " + Int(start) + ".</summary>";
        if (field.BitSize > 0)
        {
            int unitSize = field.BitUnitSize ?? field.BitStorageSize ?? field.Codec.Size;
            bool littleEndian = field.BitStorageIsLittleEndian ?? true;
            string cast = "(" + member.TypeName + ")" + (member.Enum is not null ? "(" + member.Enum.UnderlyingType + ")" : string.Empty);
            writer.Line();
            writer.Line(summary);
            writer.Line("public " + member.TypeName + " " + member.PropertyName + " => " + cast + CodecClass + ".ExtractBits(" + CodecClass + ".ReadUnsigned(" + bytes + ".Slice(" + Int(start) + ", " + Int(unitSize) + "), " + Bool(littleEndian) + "), " + CodecClass + ".BitfieldShift(" + Int(field.BitOffset) + ", " + Int(field.BitSize) + ", " + Int(unitSize * 8) + ", HighBitFirst), " + Int(field.BitSize) + ");");
            return;
        }

        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            if (field.PointerDepth > 0)
            {
                writer.Line();
                writer.Line("/// <summary>The stored address of <c>" + DescribeDeclaration(field) + "</c> at offset " + Int(start) + " (the target is read by <see cref=\"ToObject\"/>).</summary>");
                writer.Line("public long " + member.PropertyName + "Address => (long)" + CodecClass + ".ReadUnsigned(" + bytes + ".Slice(" + Int(start) + ", PointerSize), LittleEndian);");
                return;
            }

            if (member.Composite is { } nested)
            {
                if (nested.Composite.Symbol.FixedSize is null || !this.request.Views)
                {
                    return;
                }

                writer.Line();
                writer.Line("/// <summary>A view of <c>" + DescribeDeclaration(field) + "</c> at offset " + Int(start) + ".</summary>");
                writer.Line("public " + ViewName(nested) + " " + member.PropertyName + " => new(" + bytes + ".Slice(" + Int(start) + "), this.options);");
                return;
            }

            if (member.Enum is not null)
            {
                PrimitiveCodec storage = PrimitiveCodec.Resolve(member.Enum.Compiled.Integer.StorageType, field.Codec.LittleEndian);
                writer.Line();
                writer.Line(summary);
                writer.Line("public " + member.TypeName + " " + member.PropertyName + " => (" + member.TypeName + ")" + this.NumericDecode(storage, bytes + ".Slice(" + Int(start) + ", " + Int(storage.Size) + ")") + ";");
                return;
            }

            if (field.Codec.IsFixedWidthNumeric || field.Codec.IsIdentifier || field.Codec.IsFixedPoint)
            {
                writer.Line();
                writer.Line(summary);
                writer.Line("public " + member.TypeName + " " + member.PropertyName + " => " + this.ScalarDecode(field.Codec, bytes + ".Slice(" + Int(start) + ", " + Int(field.Codec.Size) + ")") + ";");
            }

            return;
        }

        if (field.Array.Kind != CompiledArrayKind.Fixed || field.Array.Dimensions.Length != 1 || field.Array.FixedCount is not { } count || field.PointerDepth > 0 || member.Composite is not null)
        {
            return;
        }

        if (field.IsCharacterArray)
        {
            int width = field.IsWideCharElement ? 2 : 1;
            writer.Line();
            writer.Line("/// <summary>The bytes of <c>" + DescribeDeclaration(field) + "</c> at offset " + Int(start) + ".</summary>");
            writer.Line("public global::System.ReadOnlySpan<byte> " + member.PropertyName + "Bytes => " + bytes + ".Slice(" + Int(start) + ", " + Int(count * width) + ");");
            writer.Line();
            writer.Line("/// <summary><c>" + DescribeDeclaration(field) + "</c> as text (trailing NULs kept unless the options set <c>TrimFixedText</c>, as the reader does); allocates the string.</summary>");
            string littleEndian = Bool(field.ExplicitWideCharacterEncoding is null ? this.request.LittleEndian : field.Codec.LittleEndian);
            writer.Line("public string " + member.PropertyName + " => " + (field.IsWideCharElement
                                                                              ? CodecClass + ".DecodeWideText(" + member.PropertyName + "Bytes, " + littleEndian + ", this.options)"
                                                                              : CodecClass + ".DecodeFixedText(" + member.PropertyName + "Bytes, " + CodecClass + ".TrimsFixedText(this.options))") + ";");
            return;
        }

        if (BoundedTextCodec.IsType(field.TypeSpelling) || !(field.Codec.IsFixedWidthNumeric || member.Enum is not null))
        {
            return;
        }

        PrimitiveCodec element = member.Enum is not null ? PrimitiveCodec.Resolve(member.Enum.Compiled.Integer.StorageType, field.Codec.LittleEndian) : field.Codec;
        string elementType = ElementType(member.TypeName, 1);
        writer.Line();
        writer.Line("/// <summary>The bytes of <c>" + DescribeDeclaration(field) + "</c> at offset " + Int(start) + ".</summary>");
        writer.Line("public global::System.ReadOnlySpan<byte> " + member.PropertyName + "Bytes => " + bytes + ".Slice(" + Int(start) + ", " + Int(count * element.Size) + ");");
        writer.Line();
        writer.Line("/// <summary>One element of <c>" + DescribeDeclaration(field) + "</c>.</summary>");
        writer.Line("/// <param name=\"index\">The element index.</param>");
        writer.Line("/// <returns>The element.</returns>");
        writer.Open("public " + elementType + " " + member.PropertyName + "(int index)");
        writer.Open("if ((uint)index >= " + Int(count) + "u)");
        writer.Line("throw new global::System.ArgumentOutOfRangeException(nameof(index));");
        writer.Close();
        string slice = bytes + ".Slice(" + Int(start) + " + index * " + Int(element.Size) + ", " + Int(element.Size) + ")";
        writer.Line("return " + (member.Enum is not null ? "(" + elementType + ")" + this.NumericDecode(element, slice) : this.ScalarDecode(element, slice)) + ";");
        writer.Close();
    }

    /// <summary>The expression decoding one fixed-width scalar (numeric, identifier, or fixed point) from a span of its size.</summary>
    private string ScalarDecode(PrimitiveCodec codec, string span)
    {
        string le = Bool(codec.LittleEndian);
        return codec.Kind switch
        {
            PrimitiveCodecKind.Uuid => CodecClass + ".ReadGuid(" + span + ", true)",
            PrimitiveCodecKind.Guid => CodecClass + ".ReadGuid(" + span + ", false)",
            PrimitiveCodecKind.Fixed16_16 => CodecClass + ".DecodeFixedPoint(" + CodecClass + ".ReadInt32(" + span + ", " + le + "), 16)",
            PrimitiveCodecKind.UFixed16_16 => CodecClass + ".DecodeFixedPoint(" + CodecClass + ".ReadUInt32(" + span + ", " + le + "), 16)",
            PrimitiveCodecKind.Fixed2_30 => CodecClass + ".DecodeFixedPoint(" + CodecClass + ".ReadInt32(" + span + ", " + le + "), 30)",
            PrimitiveCodecKind.UFixed8_8 => CodecClass + ".DecodeFixedPoint(" + CodecClass + ".ReadUInt16(" + span + ", " + le + "), 8)",
            _ => this.NumericDecode(codec, span),
        };
    }

    /// <summary>The expression decoding one fixed-width numeric from a span of its size (the reader's <c>NumericRead</c> over a span instead of the cursor).</summary>
    private string NumericDecode(PrimitiveCodec codec, string span)
    {
        string le = Bool(codec.LittleEndian);
        return codec.Kind switch
        {
            PrimitiveCodecKind.UInt8 or PrimitiveCodecKind.Latin1 or PrimitiveCodecKind.Cp437 or PrimitiveCodecKind.Utf8Unit or PrimitiveCodecKind.Utf16LeUnit or PrimitiveCodecKind.Utf16BeUnit => span + "[0]",
            PrimitiveCodecKind.Int8 => "unchecked((sbyte)" + span + "[0])",
            PrimitiveCodecKind.Bool => span + "[0] != 0",
            PrimitiveCodecKind.Char => "(char)" + span + "[0]",
            PrimitiveCodecKind.WChar => CodecClass + ".ReadChar(" + span + ", " + le + ")",
            PrimitiveCodecKind.Int16 => CodecClass + ".ReadInt16(" + span + ", " + le + ")",
            PrimitiveCodecKind.UInt16 => CodecClass + ".ReadUInt16(" + span + ", " + le + ")",
            PrimitiveCodecKind.Int24 => CodecClass + ".ReadInt24(" + span + ", " + le + ")",
            PrimitiveCodecKind.UInt24 => CodecClass + ".ReadUInt24(" + span + ", " + le + ")",
            PrimitiveCodecKind.Int32 => CodecClass + ".ReadInt32(" + span + ", " + le + ")",
            PrimitiveCodecKind.UInt32 => CodecClass + ".ReadUInt32(" + span + ", " + le + ")",
            PrimitiveCodecKind.Int48 => CodecClass + ".ReadInt48(" + span + ", " + le + ")",
            PrimitiveCodecKind.UInt48 => CodecClass + ".ReadUInt48(" + span + ", " + le + ")",
            PrimitiveCodecKind.Int64 => CodecClass + ".ReadInt64(" + span + ", " + le + ")",
            PrimitiveCodecKind.UInt64 => CodecClass + ".ReadUInt64(" + span + ", " + le + ")",
            PrimitiveCodecKind.Int128 => CodecClass + ".ReadInt128(" + span + ", " + le + ")",
            PrimitiveCodecKind.UInt128 => CodecClass + ".ReadUInt128(" + span + ", " + le + ")",
            PrimitiveCodecKind.Float16 => CodecClass + ".ReadHalf(" + span + ", " + le + ")",
            PrimitiveCodecKind.Float32 => CodecClass + ".ReadSingle(" + span + ", " + le + ")",
            PrimitiveCodecKind.Float64 => CodecClass + ".ReadDouble(" + span + ", " + le + ")",
            _ => throw new InvalidOperationException("Codec is not a fixed-width numeric: " + codec.Kind),
        };
    }
}
