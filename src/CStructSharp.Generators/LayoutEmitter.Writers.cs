namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;

/// <summary>
///     The writers: one <c>Encode&lt;Type&gt;(ref WriteCursor, value, variables)</c> per composite that mirrors the
///     runtime writer step by step - the same placement, the same validation order and texts (array length, string
///     capacity, value range, bitfield width), the same union and padding rules - plus the public <c>Serialize</c>
///     and <c>Write</c> overloads that create the cursor.
/// </summary>
internal sealed partial class LayoutEmitter
{
    private const string WriteCursorType = "global::CStructSharp.Generated.WriteCursor";

    private void EmitWriters(SourceWriter writer)
    {
        foreach (GeneratedComposite composite in this.model.Composites)
        {
            this.EmitSerializeOverloads(writer, composite, composite.LayoutName == this.rootName);
        }

        foreach (GeneratedComposite composite in this.model.Composites)
        {
            writer.Line();
            this.EmitCompositeWriter(writer, composite);
        }
    }

    private void EmitSerializeOverloads(SourceWriter writer, GeneratedComposite composite, bool isRoot)
    {
        string name = composite.Name;
        string method = "Serialize" + name;
        string layout = SourceWriter.Literal(composite.LayoutName);
        writer.Line();
        writer.Line("/// <summary>Writes one <c>" + composite.LayoutName + "</c> into a new array with the generated writer; the same bytes, and the same failures, as the runtime's <c>Serialize</c>.</summary>");
        writer.Line("/// <param name=\"value\">The value to write.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The write options; <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Line("/// <returns>The serialized bytes.</returns>");
        writer.Open("public static byte[] " + method + "(" + name + " value, " + VariablesType + " variables = null, global::CStructSharp.WriteOptions? options = null)");
        writer.Line("var cursor = new " + WriteCursorType + "(options, " + layout + ");");
        writer.Open("try");
        writer.Line("Encode" + name + "(ref cursor, value, variables, null, null);");
        writer.Line("return cursor.ToArray();");
        writer.Close();
        writer.Open("catch (global::CStructSharp.Diagnostics.CStructException exception)");
        writer.Line("cursor.Complete(exception);");
        writer.Line("throw;");
        writer.Close();
        writer.Open("finally");
        writer.Line("cursor.Dispose();");
        writer.Close();
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>Writes one <c>" + composite.LayoutName + "</c> into <paramref name=\"destination\"/>; a value that does not fit fails with the runtime's capacity message.</summary>");
        writer.Line("/// <param name=\"value\">The value to write.</param>");
        writer.Line("/// <param name=\"destination\">The bytes to write into.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The write options; <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Line("/// <returns>The number of bytes written.</returns>");
        writer.Open("public static int " + method + "(" + name + " value, global::System.Span<byte> destination, " + VariablesType + " variables = null, global::CStructSharp.WriteOptions? options = null)");
        writer.Line("var cursor = new " + WriteCursorType + "(destination, options, " + layout + ");");
        writer.Open("try");
        writer.Line("Encode" + name + "(ref cursor, value, variables, null, null);");
        writer.Line("return cursor.Length;");
        writer.Close();
        writer.Open("catch (global::CStructSharp.Diagnostics.CStructException exception)");
        writer.Line("cursor.Complete(exception);");
        writer.Line("throw;");
        writer.Close();
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>Writes one <c>" + composite.LayoutName + "</c> to <paramref name=\"stream\"/> at its current position.</summary>");
        writer.Line("/// <param name=\"stream\">The destination stream.</param>");
        writer.Line("/// <param name=\"value\">The value to write.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The write options; <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Open("public static void Write" + name + "(global::System.IO.Stream stream, " + name + " value, " + VariablesType + " variables = null, global::CStructSharp.WriteOptions? options = null)");
        writer.Line("global::System.ArgumentNullException.ThrowIfNull(stream);");
        writer.Open("if (!stream.CanWrite)");
        writer.Line("throw new global::System.ArgumentException(\"Writing requires a writable stream.\", nameof(stream));");
        writer.Close();
        writer.Line("byte[] bytes = " + method + "(value, variables, options);");
        writer.Line("stream.Write(bytes, 0, bytes.Length);");
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>Writes one <c>" + composite.LayoutName + "</c> to <paramref name=\"stream\"/> with <see cref=\"global::System.IO.Stream.WriteAsync(global::System.ReadOnlyMemory{byte}, global::System.Threading.CancellationToken)\"/>: the value is serialized first (a validation failure writes nothing), then sent in one write.</summary>");
        writer.Line("/// <param name=\"stream\">The writable stream; the current position is the output origin.</param>");
        writer.Line("/// <param name=\"value\">The value to write.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The write options; <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Line("/// <param name=\"cancellationToken\">Ends the write before the bytes are sent or at the next boundary the writer checks; linked with the options' token.</param>");
        writer.Line("/// <returns>A task that completes when the bytes have been written.</returns>");
        writer.Open("public static async global::System.Threading.Tasks.ValueTask Write" + name + "Async(global::System.IO.Stream stream, " + name + " value, " + VariablesType + " variables = null, global::CStructSharp.WriteOptions? options = null, global::System.Threading.CancellationToken cancellationToken = default)");
        writer.Line("global::System.ArgumentNullException.ThrowIfNull(stream);");
        writer.Open("if (!stream.CanWrite)");
        writer.Line("throw new global::System.ArgumentException(\"Writing requires a writable stream.\", nameof(stream));");
        writer.Close();
        writer.Line("global::CStructSharp.WriteOptions? effective = global::CStructSharp.Generated.WriteCursor.WithCancellation(options, cancellationToken, out global::System.Threading.CancellationTokenSource? linked);");
        writer.Open("using (linked)");
        writer.Line("byte[] bytes = " + method + "(value, variables, effective);");
        writer.Line("await stream.WriteAsync(bytes, effective?.CancellationToken ?? default).ConfigureAwait(false);");
        writer.Close();
        writer.Close();
        if (isRoot)
        {
            writer.Line();
            writer.Line("/// <summary>Writes the root declaration (<c>" + composite.LayoutName + "</c>) into a new array; see <see cref=\"" + method + "(" + name + ", " + VariablesType.TrimEnd('?').Replace("<string, int>", "{string, int}") + ", global::CStructSharp.WriteOptions)\"/>.</summary>");
            writer.Line("/// <param name=\"value\">The value to write.</param>");
            writer.Line("/// <param name=\"options\">The write options; <see langword=\"null\"/> uses the documented defaults.</param>");
            writer.Line("/// <returns>The serialized bytes.</returns>");
            writer.Line("public static byte[] Serialize(" + name + " value, global::CStructSharp.WriteOptions? options = null) => " + method + "(value, null, options);");
            writer.Line();
            writer.Line("/// <summary>Writes the root declaration (<c>" + composite.LayoutName + "</c>) into <paramref name=\"destination\"/>.</summary>");
            writer.Line("/// <param name=\"value\">The value to write.</param>");
            writer.Line("/// <param name=\"destination\">The bytes to write into.</param>");
            writer.Line("/// <param name=\"options\">The write options; <see langword=\"null\"/> uses the documented defaults.</param>");
            writer.Line("/// <returns>The number of bytes written.</returns>");
            writer.Line("public static int Serialize(" + name + " value, global::System.Span<byte> destination, global::CStructSharp.WriteOptions? options = null) => " + method + "(value, destination, null, options);");
            writer.Line();
            writer.Line("/// <summary>Writes the root declaration (<c>" + composite.LayoutName + "</c>) to <paramref name=\"stream\"/>.</summary>");
            writer.Line("/// <param name=\"stream\">The destination stream.</param>");
            writer.Line("/// <param name=\"value\">The value to write.</param>");
            writer.Line("/// <param name=\"options\">The write options; <see langword=\"null\"/> uses the documented defaults.</param>");
            writer.Line("public static void Write(global::System.IO.Stream stream, " + name + " value, global::CStructSharp.WriteOptions? options = null) => Write" + name + "(stream, value, null, options);");
            writer.Line();
            writer.Line("/// <inheritdoc cref=\"Write" + name + "Async(global::System.IO.Stream, " + name + ", " + VariablesType.TrimEnd('?').Replace("<string, int>", "{string, int}") + ", global::CStructSharp.WriteOptions, global::System.Threading.CancellationToken)\"/>");
            writer.Line("public static global::System.Threading.Tasks.ValueTask WriteAsync(global::System.IO.Stream stream, " + name + " value, global::CStructSharp.WriteOptions? options = null, global::System.Threading.CancellationToken cancellationToken = default) => Write" + name + "Async(stream, value, null, options, cancellationToken);");
        }
    }

    /// <summary>One composite's writer, entered with the cursor at the composite's first byte.</summary>
    private void EmitCompositeWriter(SourceWriter writer, GeneratedComposite composite)
    {
        string name = composite.Name;
        writer.Line("/// <summary>Writes one <c>" + composite.LayoutName + "</c> at the cursor's position.</summary>");
        writer.Open("private static void Encode" + name + "(ref " + WriteCursorType + " cursor, " + name + "? value, " + VariablesType + " variables, string? member, string? memberType)");
        writer.Open("if (value is null)");
        writer.Line("throw cursor.Fail(" + SourceWriter.Literal("Null is not valid for struct or union value: " + composite.LayoutName) + ", member, memberType);");
        writer.Close();
        writer.Line("cursor.EnterComposite(member ?? " + SourceWriter.Literal(composite.LayoutName) + ", memberType);");
        var scope = new ReaderScope(this, composite);
        this.decidedGroups.Clear();
        if (composite.IsUnion)
        {
            this.EmitUnionWriter(writer, composite, scope);
        }
        else
        {
            writer.Line("var placement = global::CStructSharp.Generated.CompositeCursor.Start(cursor.Position, Aligned, Packing, Allocation);");
            this.EmitWriteFields(writer, composite.Composite, scope, "value", "placement");
            EmitTailPadding(writer, "placement", composite.Composite.Symbol.Alignment, "member", "memberType");
        }

        writer.Line("cursor.ExitComposite();");
        writer.Close();
    }

    /// <summary>The struct's tail: the position moves to the last field's end, then the aligned tail is written as zeros (the runtime's explicit tail bytes).</summary>
    private static void EmitTailPadding(SourceWriter writer, string placement, int alignment, string member, string memberType)
    {
        writer.Line("cursor.Seek(" + placement + ".Current, " + member + ", " + memberType + ");");
        writer.Line("cursor.Pad((int)(" + placement + ".Finish(" + Int(alignment) + ") - " + placement + ".Current), " + member + ", " + memberType + ");");
    }

    /// <summary>
    ///     A named union as the runtime writes a <c>UnionValue</c>: with <c>SelectedMember</c> set, that member into a
    ///     zeroed extent of the union's size; otherwise the <c>RawStorage</c> bytes, which must be the union's size.
    /// </summary>
    private void EmitUnionWriter(SourceWriter writer, GeneratedComposite composite, ReaderScope scope)
    {
        CompiledCompositeType union = composite.Composite;
        string layout = SourceWriter.Literal(composite.LayoutName);
        if (union.Symbol.FixedSize is not { } size)
        {
            writer.Line("throw new global::System.NotSupportedException(" + SourceWriter.Literal("The generated writer does not write the runtime-sized union '" + composite.LayoutName + "'; write it through Layout.Serialize.") + ");");
            return;
        }

        writer.Line("int unionStart = cursor.Position;");
        writer.Open("if (value.SelectedMember is null)");
        writer.Open("if (value.RawStorage is null)");
        writer.Line("throw cursor.Fail(" + SourceWriter.Literal("A whole union write requires SelectedMember or RawStorage: " + composite.LayoutName) + ", member, memberType);");
        writer.Close();
        writer.Open("if (value.RawStorage.Length != " + Int(size) + ")");
        writer.Line("throw cursor.Fail(" + SourceWriter.Literal("Raw storage length mismatch for " + composite.LayoutName + ": expected " + size + ", got ") + " + value.RawStorage.Length.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \".\", member, memberType);");
        writer.Close();
        writer.Line("new global::System.ReadOnlySpan<byte>(value.RawStorage).CopyTo(cursor.Reserve(" + Int(size) + ", member, memberType));");
        writer.Line("cursor.ExitComposite();");
        writer.Line("return;");
        writer.Close();

        // The extent is zero first, so the bytes the selected member leaves alone are zero as in the runtime's staging buffer.
        writer.Line("cursor.Pad(" + Int(size) + ", member, memberType);");
        writer.Line("cursor.Position = unionStart;");
        writer.Line("switch (value.SelectedMember)");
        writer.Line("{");
        foreach (CompiledField field in union.Fields)
        {
            if (field.Name.Length == 0 || field.IsZeroWidthBitfield)
            {
                continue;
            }

            writer.Line("case " + SourceWriter.Literal(field.Name) + ":");
            writer.Indent();
            this.EmitWriteField(writer, field, union, scope, "value", inUnion: true, "union", selected: true);
            writer.Line("break;");
            writer.Outdent();
        }

        writer.Line("default:");
        writer.Indent();
        writer.Line("throw cursor.Fail(" + SourceWriter.Literal("Union '" + composite.LayoutName + "' has no member named '") + " + value.SelectedMember + \"'.\", member, memberType);");
        writer.Outdent();
        writer.Line("}");
        writer.Line("cursor.Position = unionStart + " + Int(size) + ";");
    }

    private void EmitWriteFields(SourceWriter writer, CompiledCompositeType composite, ReaderScope scope, string target, string placement)
    {
        EmitConditionalSlots(writer, composite, placement);
        foreach (CompiledField field in composite.Fields)
        {
            this.EmitWriteField(writer, field, composite, scope, target, inUnion: false, placement, selected: false);
        }
    }

    private void EmitWriteField(SourceWriter writer, CompiledField field, CompiledCompositeType composite, ReaderScope scope, string target, bool inUnion, string placement, bool selected)
    {
        bool promoted = field.IsUnnamed && field.IsInlineComposite;
        string member = promoted ? "member" : SourceWriter.Literal(field.Name.Length == 0 && field.BitSize == 0 ? "_" : field.Name);
        string memberType = promoted ? "memberType" : SourceWriter.Literal(field.DisplayTypeSpelling);
        writer.Line("// " + DescribeDeclaration(field));
        GeneratedMember? generated = promoted || field.IsUnnamed ? null : scope.Member(field);

        // Conditional arms: the decision comes from the values written so far; a member supplied for an inactive arm
        // is an error, as is a missing member of an active one.
        int opened = 0;
        foreach (CompiledConditionalBranch branch in field.ConditionalBranches)
        {
            string slot = ArmSlot(placement, branch.Slot);
            if (this.decidedGroups.Add(branch.Group))
            {
                this.EmitSelector(writer, branch.Group, scope, slot);
            }

            if (generated is not null && branch.Arm >= -1)
            {
                // The runtime rejects the value before entering the field, so the enclosing member is what it notes.
                writer.Open("if (" + slot + " != " + Int(branch.Arm) + " && " + target + "." + generated.HasFlagName + ")");
                writer.Line("throw cursor.Fail(" + SourceWriter.Literal("Inactive conditional field supplied: " + field.Name) + ", member, memberType);");
                writer.Close();
            }

            writer.Open("if (" + slot + " == " + Int(branch.Arm) + ")");
            opened++;
        }

        if (generated is { IsConditional: true })
        {
            writer.Open("if (!" + target + "." + generated.HasFlagName + ")");
            writer.Line("throw cursor.Fail(" + SourceWriter.Literal("No value was supplied for '" + field.Name + "'.") + ", " + member + ", " + memberType + ");");
            writer.Close();
        }

        this.EmitWriteFieldBody(writer, field, scope, target, inUnion, placement, promoted, member, memberType, generated, openBlock: opened == 0);
        for (int index = 0; index < opened; index++)
        {
            writer.Close();
        }
    }

    private void EmitWriteFieldBody(SourceWriter writer, CompiledField field, ReaderScope scope, string target, bool inUnion, string placement, bool promoted, string member, string memberType, GeneratedMember? generated, bool openBlock)
    {
        if (openBlock)
        {
            writer.Open(string.Empty);
        }

        if (field.IsZeroWidthBitfield)
        {
            if (!inUnion)
            {
                writer.Line("cursor.Seek(" + placement + ".AdvanceToSeparator(" + Int(field.BitStorageSize ?? 1) + ", " + Int(field.Alignment) + ", " + Int(field.BitRunBits) + "), null, null);");
            }

            CloseBlock(writer, openBlock);
            return;
        }

        if (field.BitSize > 0)
        {
            this.EmitWriteBitfield(writer, field, scope, target, inUnion, member, memberType, placement, generated);
            CloseBlock(writer, openBlock);
            return;
        }

        if (!inUnion)
        {
            writer.Line("cursor.Seek(" + placement + ".AdvanceToField(" + Int(field.Alignment) + "), " + member + ", " + memberType + ");");
            this.EmitOffsetAssertion(writer, field, scope, member, memberType);
        }

        CompiledCompositeType? inline = this.InlineComposite(field);
        if (field.IsUnnamed && inline is not null)
        {
            if (inline.IsUnion)
            {
                this.EmitPromotedUnionWriter(writer, inline, scope, target, member, memberType, placement);
            }
            else
            {
                string inner = placement + "N";
                writer.Line("cursor.EnterComposite(" + member + ", " + memberType + ");");
                writer.Line("var " + inner + " = global::CStructSharp.Generated.CompositeCursor.Start(cursor.Position, Aligned, Packing, Allocation);");
                this.EmitWriteFields(writer, inline, scope, target, inner);
                EmitTailPadding(writer, inner, inline.Symbol.Alignment, member, memberType);
                writer.Line("cursor.ExitComposite();");
            }
        }
        else if (field.IsUnnamed)
        {
            // Padding (`uint16 _;`): the runtime writes the type's zero value; the same bytes are zeros of its extent.
            int extent = field.FixedStorageSize ?? throw new InvalidOperationException("Padding without a fixed size: " + field.TypeSpelling);
            writer.Line("cursor.Pad(" + Int(extent) + ", " + member + ", " + memberType + ");");
        }
        else
        {
            generated ??= scope.Member(field) ?? throw new InvalidOperationException("No generated member for field " + field.Name);
            string access = target + "." + generated.PropertyName;
            if (generated.IsReferenceType || (generated.IsConditional && generated.TypeName.EndsWith("?", StringComparison.Ordinal)))
            {
                writer.Open("if (" + access + " is null)");
                writer.Line("throw cursor.Fail(" + SourceWriter.Literal("Null is valid only for a scalar pointer field: " + field.Name) + ", " + member + ", " + memberType + ");");
                writer.Close();
            }

            this.EmitWriteValue(writer, field, generated, scope, access, inUnion, member, memberType);
            if (!inUnion)
            {
                scope.Publish(generated, access);
            }
        }

        if (!inUnion)
        {
            writer.Line(placement + ".CompleteField(cursor.Position);");
        }

        CloseBlock(writer, openBlock);
    }

    /// <summary>
    ///     An anonymous union inside a struct, as the runtime writes it from the parent's members: the widest member
    ///     (declaration order among equals) is written at the union's start and the rest of the extent is zero.
    /// </summary>
    private void EmitPromotedUnionWriter(SourceWriter writer, CompiledCompositeType union, ReaderScope scope, string target, string member, string memberType, string placement)
    {
        if (union.Symbol.FixedSize is not { } size)
        {
            writer.Line("throw new global::System.NotSupportedException(\"The generated writer does not write a runtime-sized anonymous union; write the value through Layout.Serialize.\");");
            return;
        }

        CompiledField widest = union.Fields.Where(item => !item.IsZeroWidthBitfield).OrderByDescending(item => item.FixedStorageSize ?? int.MaxValue).First();
        writer.Line("int unionStart = cursor.Position;");
        writer.Line("cursor.EnterComposite(" + member + ", " + memberType + ");");
        writer.Line("cursor.Pad(" + Int(size) + ", " + member + ", " + memberType + ");");
        writer.Line("cursor.Position = unionStart;");
        this.EmitWriteField(writer, widest, union, scope, target, inUnion: true, placement + "U", selected: true);
        writer.Line("cursor.ExitComposite();");
        writer.Line("cursor.Position = unionStart + " + Int(size) + ";");
    }

    private void EmitWriteBitfield(SourceWriter writer, CompiledField field, ReaderScope scope, string target, bool inUnion, string member, string memberType, string placement, GeneratedMember? generated)
    {
        int declaredSize = field.BitStorageSize ?? field.Codec.Size;
        bool littleEndian = field.BitStorageIsLittleEndian ?? true;
        if (inUnion)
        {
            writer.Line("var slot = new global::CStructSharp.Generated.BitfieldSlot(cursor.Position, " + Int(declaredSize) + ", 0);");
        }
        else
        {
            writer.Line("var slot = " + placement + ".AdvanceToBitfield(" + Int(declaredSize) + ", " + Int(field.Alignment) + ", " + Int(field.BitSize) + ", " + Int(field.BitRunBits) + ", " + Bool(littleEndian) + ", " + member + ");");
        }

        string bits = "0UL";
        if (field.Name.Length > 0)
        {
            generated ??= scope.Member(field) ?? throw new InvalidOperationException("No generated member for bitfield " + field.Name);
            string access = target + "." + generated.PropertyName;
            if (generated.Enum is not null)
            {
                // An enum's raw bits in its storage width: a negative member of a signed storage type wraps to the width.
                string storage = generated.Enum.UnderlyingType;
                bits = "(ulong)(" + UnsignedCounterpart(storage) + ")(" + storage + ")" + access;
            }
            else
            {
                bits = access;
            }

            if (!inUnion)
            {
                scope.Publish(generated, access);
            }
        }

        writer.Line("cursor.WriteBits(slot, " + Int(field.BitSize) + ", " + bits + ", " + Bool(littleEndian) + ", HighBitFirst, " + member + ", " + memberType + ");");
    }

    private static string UnsignedCounterpart(string storage)
    {
        return storage switch
        {
            "sbyte" => "byte",
            "short" => "ushort",
            "int" => "uint",
            "long" => "ulong",
            _ => storage,
        };
    }

    private void EmitWriteValue(SourceWriter writer, CompiledField field, GeneratedMember generated, ReaderScope scope, string access, bool inUnion, string member, string memberType)
    {
        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            this.EmitScalarWrite(writer, field, generated, access, member, memberType);
            return;
        }

        this.EmitArrayWrite(writer, field, generated, scope, access, member, memberType);
    }

    private void EmitArrayWrite(SourceWriter writer, CompiledField field, GeneratedMember generated, ReaderScope scope, string access, string member, string memberType)
    {
        switch (field.Array.Kind)
        {
        case CompiledArrayKind.Flexible:
            // char name[] writes as a terminated string.
            writer.Line(this.TerminatedWrite(field, access, member, memberType));
            return;
        case CompiledArrayKind.ToEnd:
        case CompiledArrayKind.Terminated:
            // A data-sized array writes exactly the supplied elements (plus its terminator); the runtime materializes
            // at most MaxArrayElements of them.
            writer.Line("int count = " + access + ".Length;");
            writer.Open("if (count > cursor.MaxArrayElements)");
            writer.Line("throw cursor.Fail(" + SourceWriter.Literal("Array value for " + field.Name + " exceeds its permitted element count of ") + " + cursor.MaxArrayElements.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \".\", " + member + ", " + memberType + ");");
            writer.Close();
            break;
        case CompiledArrayKind.Fixed when field.Array.Dimensions.Length > 1 || field.Array.CountExpression is null:
            {
                int total = field.Array.TotalFixedElementCount ?? field.FixedArrayCount ?? throw new InvalidOperationException("Fixed array without a count: " + field.Name);
                writer.Line("int count = " + Int(total) + ";");
                if (field.Array.Dimensions.Length > 1)
                {
                    writer.Line("cursor.RequireArrayLength(count, " + member + ", " + memberType + ");");
                }

                break;
            }

        case CompiledArrayKind.Fixed:
        case CompiledArrayKind.Runtime:
            writer.Line("int count;");
            this.EmitExpression(writer, field.Array.CountExpression!, scope, "count", "array length for " + field.Name, member, memberType, "int");
            if (!ExpressionEmitter.IsInt32Literal(field.Array.CountExpression!))
            {
                writer.Open("if (count < 0)");
                writer.Line("throw cursor.Fail(" + SourceWriter.Literal("Array length cannot be negative: " + field.Name) + ", " + member + ", " + memberType + ");");
                writer.Close();
            }

            writer.Line("cursor.RequireArrayLength(count, " + member + ", " + memberType + ");");
            break;
        default:
            throw new InvalidOperationException("Unsupported array kind: " + field.Array.Kind);
        }

        if (field.PointerDepth == 0 && field.IsCharacterArray)
        {
            this.EmitCharacterArrayWrite(writer, field, access, member, memberType);
            return;
        }

        if (field.PointerDepth == 0 && BoundedTextCodec.IsType(field.TypeSpelling))
        {
            writer.Line("cursor.WriteBoundedText(count, " + SourceWriter.Literal(field.TypeSpelling) + ", " + access + ", " + member + ", " + memberType + ");");
            return;
        }

        bool dataSized = field.Array.Kind is CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated;
        string elementType = ElementType(generated.TypeName, field.Array.Dimensions.Length == 0 ? 1 : field.Array.Dimensions.Length);
        if (field.Array.Dimensions.Length > 1)
        {
            this.EmitNestedArrayWrite(writer, field, generated, access, elementType, member, memberType);
            return;
        }

        if (!dataSized)
        {
            EmitLengthChecks(writer, field.Name, access, "count", member, memberType);
        }

        PrimitiveCodec codec = field.Codec;
        bool bulk = field.PointerDepth == 0 && generated.Composite is null && generated.Enum is null && codec.IsFixedWidthNumeric && codec.Kind is not (PrimitiveCodecKind.Int24 or PrimitiveCodecKind.UInt24 or PrimitiveCodecKind.Int48 or PrimitiveCodecKind.UInt48 or PrimitiveCodecKind.Char);
        if (bulk)
        {
            writer.Open("if (count > 0)");
            writer.Line("global::System.Span<byte> bytes = cursor.Reserve(count * " + Int(codec.Size) + ", " + member + ", " + memberType + ");");
            writer.Line(BulkEncode(codec, elementType, access));
            writer.Close();
        }
        else
        {
            writer.Open("for (int index = 0; index < count; index++)");
            this.EmitScalarWrite(writer, field, generated, access + "[index]", member, memberType);
            writer.Close();
        }

        if (field.Array.Kind == CompiledArrayKind.Terminated)
        {
            writer.Line("cursor.Pad(" + Int(field.FixedElementSize ?? 0) + ", " + member + ", " + memberType + ");");
        }
    }

    /// <summary>The runtime's two array-length checks, in its order: too many elements (the materialization cap), then a count that differs.</summary>
    private static void EmitLengthChecks(SourceWriter writer, string fieldName, string access, string expected, string member, string memberType)
    {
        const string Invariant = ".ToString(global::System.Globalization.CultureInfo.InvariantCulture)";
        writer.Open("if (" + access + ".Length > " + expected + ")");
        writer.Line("throw cursor.Fail(" + SourceWriter.Literal("Array value for " + fieldName + " exceeds its permitted element count of ") + " + " + expected + Invariant + " + \".\", " + member + ", " + memberType + ");");
        writer.Close();
        writer.Open("if (" + access + ".Length != " + expected + ")");
        writer.Line("throw cursor.Fail(" + SourceWriter.Literal("Array length mismatch for " + fieldName + ": expected ") + " + " + expected + Invariant + " + \", got \" + " + access + ".Length" + Invariant + " + \".\", " + member + ", " + memberType + ");");
        writer.Close();
    }

    /// <summary>A multidimensional array: every level must have its declared length; the leaves are written in row-major order.</summary>
    private void EmitNestedArrayWrite(SourceWriter writer, CompiledField field, GeneratedMember generated, string access, string elementType, string member, string memberType)
    {
        var dimensions = field.Array.Dimensions;
        string current = access;
        var indices = new List<string>();
        for (int level = 0; level < dimensions.Length; level++)
        {
            int expected = dimensions[level].FixedCount ?? throw new InvalidOperationException("Multidimensional array without a fixed dimension: " + field.Name);
            EmitLengthChecks(writer, field.Name, current, Int(expected), member, memberType);
            if (level == dimensions.Length - 1)
            {
                break;
            }

            string index = "i" + Int(level);
            writer.Open("for (int " + index + " = 0; " + index + " < " + Int(expected) + "; " + index + "++)");
            indices.Add(index);
            current += "[" + index + "]";
            writer.Open("if (" + current + " is null)");
            writer.Line("throw cursor.Fail(" + SourceWriter.Literal("Null is valid only for a scalar pointer field: " + field.Name) + ", " + member + ", " + memberType + ");");
            writer.Close();
        }

        string leaf = "i" + Int(dimensions.Length - 1);
        writer.Open("for (int " + leaf + " = 0; " + leaf + " < " + current + ".Length; " + leaf + "++)");
        this.EmitScalarWrite(writer, field, generated, current + "[" + leaf + "]", member, memberType);
        writer.Close();
        for (int level = 0; level < indices.Count; level++)
        {
            writer.Close();
        }
    }

    private void EmitCharacterArrayWrite(SourceWriter writer, CompiledField field, string access, string member, string memberType)
    {
        bool wide = field.IsWideCharElement;
        string littleEndian = Bool(field.ExplicitWideCharacterEncoding is null ? this.request.LittleEndian : field.Codec.LittleEndian);
        if (field.Array.Dimensions.Length <= 1)
        {
            writer.Line("cursor.WriteFixedText(count, " + access + ", " + Bool(wide) + ", " + littleEndian + ", " + member + ", " + memberType + ");");
            return;
        }

        // A table of fixed text: the outer dimensions nest around the rows, each row one string of the innermost length.
        var dimensions = field.Array.Dimensions;
        int rowLength = dimensions[dimensions.Length - 1].FixedCount ?? throw new InvalidOperationException("Character table without a fixed row length: " + field.Name);
        string current = access;
        int opened = 0;
        for (int level = 0; level < dimensions.Length - 1; level++)
        {
            int expected = dimensions[level].FixedCount ?? throw new InvalidOperationException("Character table without a fixed dimension: " + field.Name);
            EmitLengthChecks(writer, field.Name, current, Int(expected), member, memberType);
            string index = "i" + Int(level);
            writer.Open("for (int " + index + " = 0; " + index + " < " + Int(expected) + "; " + index + "++)");
            opened++;
            current += "[" + index + "]";
            writer.Open("if (" + current + " is null)");
            writer.Line("throw cursor.Fail(" + SourceWriter.Literal("Null is valid only for a scalar pointer field: " + field.Name) + ", " + member + ", " + memberType + ");");
            writer.Close();
        }

        writer.Line("cursor.WriteFixedText(" + Int(rowLength) + ", " + current + ", " + Bool(wide) + ", " + littleEndian + ", " + member + ", " + memberType + ");");
        for (int level = 0; level < opened; level++)
        {
            writer.Close();
        }
    }

    private void EmitScalarWrite(SourceWriter writer, CompiledField field, GeneratedMember generated, string access, string member, string memberType)
    {
        if (field.PointerDepth > 0)
        {
            writer.Line("cursor.WritePointerAddress(" + access + ".Address, PointerSize, LittleEndian, " + member + ", " + memberType + ");");
            return;
        }

        if (generated.Composite is not null)
        {
            writer.Line("Encode" + generated.Composite.Name + "(ref cursor, " + access + ", variables, " + member + ", " + memberType + ");");
            return;
        }

        if (generated.Enum is not null)
        {
            PrimitiveCodec storage = PrimitiveCodec.Resolve(generated.Enum.Compiled.Integer.StorageType, field.Codec.LittleEndian);
            writer.Line(this.NumericWrite(storage, "(" + generated.Enum.UnderlyingType + ")" + access, member, memberType));
            return;
        }

        this.EmitPrimitiveWrite(writer, field.Codec, field.Type.Symbol.Name, field.TypeSpelling, access, member, memberType);
    }

    private void EmitPrimitiveWrite(SourceWriter writer, PrimitiveCodec codec, string typeName, string typeSpelling, string access, string member, string memberType)
    {
        string le = Bool(codec.LittleEndian);
        switch (codec.Kind)
        {
        case PrimitiveCodecKind.TerminatedAscii:
        case PrimitiveCodecKind.TerminatedUtf8:
        case PrimitiveCodecKind.TerminatedUtf16:
            writer.Line("cursor.WriteTerminatedString(" + TerminatedEncoding(codec) + ", " + CharLiteral(codec.Terminator) + ", " + access + ", " + member + ", " + memberType + ");");
            return;
        case PrimitiveCodecKind.Uuid:
            writer.Line(CodecClass + ".WriteGuid(cursor.Reserve(16, " + member + ", " + memberType + "), " + access + ", true);");
            return;
        case PrimitiveCodecKind.Guid:
            writer.Line(CodecClass + ".WriteGuid(cursor.Reserve(16, " + member + ", " + memberType + "), " + access + ", false);");
            return;
        case PrimitiveCodecKind.ULeb128_32:
        case PrimitiveCodecKind.ULeb128_64:
            writer.Open(string.Empty);
            writer.Line("global::System.Span<byte> encoded = stackalloc byte[10];");
            writer.Line("int written = " + CodecClass + ".WriteULeb128(encoded, " + access + ");");
            writer.Line("encoded.Slice(0, written).CopyTo(cursor.Reserve(written, " + member + ", " + memberType + "));");
            writer.Close();
            return;
        case PrimitiveCodecKind.SLeb128_32:
        case PrimitiveCodecKind.SLeb128_64:
            writer.Open(string.Empty);
            writer.Line("global::System.Span<byte> encoded = stackalloc byte[10];");
            writer.Line("int written = " + CodecClass + ".WriteSLeb128(encoded, " + access + ");");
            writer.Line("encoded.Slice(0, written).CopyTo(cursor.Reserve(written, " + member + ", " + memberType + "));");
            writer.Close();
            return;
        case PrimitiveCodecKind.Fixed16_16:
        case PrimitiveCodecKind.UFixed16_16:
        case PrimitiveCodecKind.Fixed2_30:
        case PrimitiveCodecKind.UFixed8_8:
            {
                (int width, int fraction, bool signed) = codec.Kind switch
                {
                    PrimitiveCodecKind.Fixed16_16 => (32, 16, true),
                    PrimitiveCodecKind.UFixed16_16 => (32, 16, false),
                    PrimitiveCodecKind.Fixed2_30 => (32, 30, true),
                    _ => (16, 8, false),
                };
                writer.Open(string.Empty);
                writer.Line("long raw;");
                writer.Open("try");
                writer.Line("raw = " + CodecClass + ".EncodeFixedPoint(" + access + ", " + Int(width) + ", " + Int(fraction) + ", " + Bool(signed) + ");");
                writer.Close();
                writer.Open("catch (global::CStructSharp.Diagnostics.CStructWriteException exception)");
                writer.Line("throw cursor.WithMember(exception, " + member + ", " + memberType + ");");
                writer.Close();
                string store = width == 16
                                   ? CodecClass + ".WriteUInt16(cursor.Reserve(2, " + member + ", " + memberType + "), unchecked((ushort)raw), " + le + ");"
                                   : CodecClass + ".WriteUInt32(cursor.Reserve(4, " + member + ", " + memberType + "), unchecked((uint)raw), " + le + ");";
                writer.Line(store);
                writer.Close();
                return;
            }

        case PrimitiveCodecKind.Custom:
            writer.Line("cursor.WriteCustom(CodecInstances.Value[" + Int(this.CodecIndex(typeName)) + "], " + access + ", " + member + ", " + memberType + ");");
            return;
        case PrimitiveCodecKind.Char:
            writer.Open("try");
            writer.Line("cursor.Reserve(1, " + member + ", " + memberType + ")[0] = " + CodecClass + ".ToNarrowCharacter(" + access + ");");
            writer.Close();
            writer.Open("catch (global::CStructSharp.Diagnostics.CStructWriteException exception)");
            writer.Line("throw cursor.WithMember(exception, " + member + ", " + memberType + ");");
            writer.Close();
            return;
        case PrimitiveCodecKind.Int24:
        case PrimitiveCodecKind.UInt24:
        case PrimitiveCodecKind.Int48:
        case PrimitiveCodecKind.UInt48:
            {
                // The range is checked before any byte is reserved, so a failure reports the field's start like the runtime
                // (the codec's own text: the runtime's writer delegate lets a CStructWriteException through unchanged).
                string method = codec.Kind switch
                {
                    PrimitiveCodecKind.Int24 => "WriteInt24",
                    PrimitiveCodecKind.UInt24 => "WriteUInt24",
                    PrimitiveCodecKind.Int48 => "WriteInt48",
                    _ => "WriteUInt48",
                };
                writer.Open(string.Empty);
                writer.Line("global::System.Span<byte> encoded = stackalloc byte[" + Int(codec.Size) + "];");
                writer.Open("try");
                writer.Line(CodecClass + "." + method + "(encoded, " + access + ", " + le + ");");
                writer.Close();
                writer.Open("catch (global::CStructSharp.Diagnostics.CStructWriteException exception)");
                writer.Line("throw cursor.WithMember(exception, " + member + ", " + memberType + ");");
                writer.Close();
                writer.Line("encoded.CopyTo(cursor.Reserve(" + Int(codec.Size) + ", " + member + ", " + memberType + "));");
                writer.Close();
                return;
            }

        default:
            writer.Line(this.NumericWrite(codec, access, member, memberType));
            return;
        }
    }

    /// <summary>The statement that stores one fixed-width numeric value (whose C# type already fits the codec) at the cursor.</summary>
    private string NumericWrite(PrimitiveCodec codec, string access, string member, string memberType)
    {
        string le = Bool(codec.LittleEndian);
        string reserve = "cursor.Reserve(" + Int(codec.Size) + ", " + member + ", " + memberType + ")";
        return codec.Kind switch
        {
            PrimitiveCodecKind.UInt8 or PrimitiveCodecKind.Latin1 or PrimitiveCodecKind.Cp437 or PrimitiveCodecKind.Utf8Unit or PrimitiveCodecKind.Utf16LeUnit or PrimitiveCodecKind.Utf16BeUnit => reserve + "[0] = " + access + ";",
            PrimitiveCodecKind.Int8 => reserve + "[0] = unchecked((byte)" + access + ");",
            PrimitiveCodecKind.Bool => reserve + "[0] = (byte)(" + access + " ? 1 : 0);",
            PrimitiveCodecKind.Char => reserve + "[0] = " + CodecClass + ".ToNarrowCharacter(" + access + ");",
            PrimitiveCodecKind.WChar => CodecClass + ".WriteChar(" + reserve + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.Int16 => CodecClass + ".WriteInt16(" + reserve + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.UInt16 => CodecClass + ".WriteUInt16(" + reserve + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.Int24 => CodecClass + ".WriteInt24(" + reserve + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.UInt24 => CodecClass + ".WriteUInt24(" + reserve + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.Int32 => CodecClass + ".WriteInt32(" + reserve + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.UInt32 => CodecClass + ".WriteUInt32(" + reserve + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.Int48 => CodecClass + ".WriteInt48(" + reserve + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.UInt48 => CodecClass + ".WriteUInt48(" + reserve + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.Int64 => CodecClass + ".WriteInt64(" + reserve + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.UInt64 => CodecClass + ".WriteUInt64(" + reserve + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.Int128 => CodecClass + ".WriteInt128(" + reserve + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.UInt128 => CodecClass + ".WriteUInt128(" + reserve + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.Float16 => CodecClass + ".WriteHalf(" + reserve + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.Float32 => CodecClass + ".WriteSingle(" + reserve + ", " + access + ", " + le + ");",
            PrimitiveCodecKind.Float64 => CodecClass + ".WriteDouble(" + reserve + ", " + access + ", " + le + ");",
            _ => throw new InvalidOperationException("Codec is not a fixed-width numeric: " + codec.Kind),
        };
    }

    private static string BulkEncode(PrimitiveCodec codec, string elementType, string access)
    {
        string le = Bool(codec.LittleEndian);
        return codec.Kind switch
        {
            PrimitiveCodecKind.UInt8 or PrimitiveCodecKind.Latin1 or PrimitiveCodecKind.Cp437 or PrimitiveCodecKind.Utf8Unit or PrimitiveCodecKind.Utf16LeUnit or PrimitiveCodecKind.Utf16BeUnit => "new global::System.ReadOnlySpan<byte>(" + access + ").CopyTo(bytes);",
            PrimitiveCodecKind.Int8 => "global::System.Runtime.InteropServices.MemoryMarshal.AsBytes<sbyte>(" + access + ").CopyTo(bytes);",
            PrimitiveCodecKind.Bool => "for (int index = 0; index < count; index++) { bytes[index] = (byte)(" + access + "[index] ? 1 : 0); }",
            _ => CodecClass + ".EncodeIntegers<" + elementType + ">(" + access + ", bytes, " + le + ");",
        };
    }

    private string TerminatedWrite(CompiledField field, string access, string member, string memberType)
    {
        string name = PrimitiveCatalog.CanonicalNames[field.TerminatedCodecId];
        PrimitiveCodec codec = PrimitiveCodec.Resolve(name, this.request.LittleEndian);
        return "cursor.WriteTerminatedString(" + TerminatedEncoding(codec) + ", " + CharLiteral(codec.Terminator) + ", " + access + ", " + member + ", " + memberType + ");";
    }
}
