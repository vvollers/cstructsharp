namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using CStructSharp.Codecs;
using CStructSharp.Compilation;

/// <summary>
///     The fixed writers: for a struct with a fixed reader whose members also encode without a possible failure
///     (numbers, enums, nested fixed structs, and fixed arrays of them - not text, whose characters are validated), an
///     <c>Is&lt;Type&gt;FixedWritable(value)</c> check and a <c>Write&lt;Type&gt;Fixed(target, value)</c> that stores
///     each member at its constant offset. The member-by-member writer tries them first: the check confirms that no
///     nested value is null and every array has its declared length, and <c>WriteCursor.TryReserveFixed</c> hands over
///     cleared bytes only where that writer would write exactly them. Anything else is written member by member.
/// </summary>
internal sealed partial class LayoutEmitter
{
    // Whether each composite has a fixed writer, decided once.
    private readonly Dictionary<GeneratedComposite, bool> fixedWritable = new(ReferenceEqualityComparer.Instance);

    /// <summary>Emits the fixed writers and their value checks for every eligible composite.</summary>
    /// <param name="writer">The generated source destination.</param>
    private void EmitFixedWriters(SourceWriter writer)
    {
        foreach (GeneratedComposite composite in this.model.Composites)
        {
            if (this.IsFixedWritable(composite, 0))
            {
                writer.Line();
                this.EmitFixedWritableCheck(writer, composite);
                writer.Line();
                this.EmitFixedWriter(writer, composite);
            }
        }
    }

    /// <summary>Emits the fast-path test at the top of a composite's member-by-member writer, when it has a fixed writer.</summary>
    /// <param name="writer">The generated source destination, after the null check and before the composite is entered.</param>
    /// <param name="composite">The composite being written.</param>
    private void EmitFixedWriterShortcut(SourceWriter writer, GeneratedComposite composite)
    {
        if (!this.IsFixedWritable(composite, 0) || this.FixedPlanOf(composite, 0) is not { } plan)
        {
            return;
        }

        writer.Open("if (Is" + composite.Name + "FixedWritable(value) && cursor.TryReserveFixed(" + Int(plan.Size) + ", " + Int(this.request.Settings.Aligned ? plan.Alignment : 1) + ", " + Int(plan.NestingLevels) + ", " + Int(plan.MaximumArrayCount) + ", out global::System.Span<byte> fixedBytes))");
        writer.Line("Write" + composite.Name + "Fixed(fixedBytes, value);");
        writer.Line("return;");
        writer.Close();
    }

    /// <summary>Emits the check that a value has every nested value and every array length the fixed writer relies on.</summary>
    private void EmitFixedWritableCheck(SourceWriter writer, GeneratedComposite composite)
    {
        string name = composite.Name;
        writer.Line("/// <summary>Whether <paramref name=\"value\"/> can be written by <see cref=\"Write" + name + "Fixed\"/>: no nested value is null and every array has its declared length.</summary>");
        writer.Line("/// <param name=\"value\">The value about to be written.</param>");
        writer.Line("/// <returns><see langword=\"true\"/> when the fixed writer encodes it exactly as the member-by-member writer would.</returns>");
        writer.Open("private static bool Is" + name + "FixedWritable(" + name + " value)");
        var scope = new ReaderScope(this, composite);
        foreach (CompiledField field in composite.Composite.Fields)
        {
            GeneratedMember member = scope.Member(field) ?? throw new InvalidOperationException("No generated member for field " + field.Name);
            string access = "value." + member.PropertyName;
            if (field.Array.Kind == CompiledArrayKind.Scalar)
            {
                if (member.Composite is { } nested)
                {
                    writer.Open("if (" + access + " is null || !Is" + nested.Name + "FixedWritable(" + access + "))");
                    writer.Line("return false;");
                    writer.Close();
                }

                continue;
            }

            int count = field.Array.FixedCount ?? throw new InvalidOperationException("Fixed array without a count: " + field.Name);
            writer.Open("if (" + access + " is null || " + access + ".Length != " + Int(count) + ")");
            writer.Line("return false;");
            writer.Close();
            if (member.Composite is { } element)
            {
                writer.Open("foreach (" + element.Name + "? item in " + access + ")");
                writer.Open("if (item is null || !Is" + element.Name + "FixedWritable(item))");
                writer.Line("return false;");
                writer.Close();
                writer.Close();
            }
        }

        writer.Line("return true;");
        writer.Close();
    }

    /// <summary>Emits one composite's fixed writer.</summary>
    private void EmitFixedWriter(SourceWriter writer, GeneratedComposite composite)
    {
        string name = composite.Name;
        writer.Line("/// <summary>Writes one <c>" + composite.LayoutName + "</c> into exactly its cleared bytes, each member at its constant offset (reached only through <c>WriteCursor.TryReserveFixed</c>).</summary>");
        writer.Line("/// <param name=\"target\">The struct's bytes, already zero so that padding is written as zeros.</param>");
        writer.Line("/// <param name=\"value\">A value that passed <see cref=\"Is" + name + "FixedWritable\"/>.</param>");
        writer.Open("private static void Write" + name + "Fixed(global::System.Span<byte> target, " + name + " value)");
        var scope = new ReaderScope(this, composite);
        foreach (CompiledField field in composite.Composite.Fields)
        {
            GeneratedMember member = scope.Member(field) ?? throw new InvalidOperationException("No generated member for field " + field.Name);
            int offset = field.FixedOffset ?? throw new InvalidOperationException("Fixed member without an offset: " + field.Name);
            string access = "value." + member.PropertyName;
            writer.Line("// " + DescribeDeclaration(field));
            if (field.Array.Kind == CompiledArrayKind.Scalar)
            {
                writer.Line(this.FixedStore(field, member, Int(offset), access));
                continue;
            }

            int count = field.Array.FixedCount ?? throw new InvalidOperationException("Fixed array without a count: " + field.Name);
            PrimitiveCodec codec = field.Codec;
            bool bulk = member.Composite is null && member.Enum is null && codec.Kind is not (PrimitiveCodecKind.Int24 or PrimitiveCodecKind.UInt24 or PrimitiveCodecKind.Int48 or PrimitiveCodecKind.UInt48 or PrimitiveCodecKind.Char);
            writer.Open(string.Empty);
            if (bulk)
            {
                // The bulk encode the member-by-member writer applies, over the array's bytes.
                writer.Line("global::System.Span<byte> bytes = target.Slice(" + Int(offset) + ", " + Int(count * codec.Size) + ");");
                if (codec.Kind == PrimitiveCodecKind.Bool)
                {
                    // The boolean encoder loops over a count local.
                    writer.Line("int count = " + Int(count) + ";");
                }

                writer.Line(BulkEncode(codec, ElementType(member.TypeName, 1), access));
            }
            else
            {
                int stride = field.FixedElementSize ?? throw new InvalidOperationException("Fixed array without an element size: " + field.Name);
                writer.Open("for (int index = 0; index < " + Int(count) + "; index++)");
                writer.Line(this.FixedStore(field, member, Int(offset) + " + index * " + Int(stride), access + "[index]"));
                writer.Close();
            }

            writer.Close();
        }

        writer.Close();
    }

    /// <summary>The statement storing one scalar or array element of a fixed member at <paramref name="offset"/>.</summary>
    private string FixedStore(CompiledField field, GeneratedMember member, string offset, string access)
    {
        if (member.Composite is { } nested)
        {
            int size = nested.Composite.Symbol.FixedSize ?? throw new InvalidOperationException("Fixed composite without a size: " + nested.Name);
            return "Write" + nested.Name + "Fixed(target.Slice(" + offset + ", " + Int(size) + "), " + access + ");";
        }

        if (member.Enum is not null)
        {
            // The member-by-member writer stores an enum through its declared storage type.
            PrimitiveCodec storage = PrimitiveCodec.Resolve(member.Enum.Compiled.Integer.StorageType, field.Codec.LittleEndian);
            return NumericStore(storage, "target.Slice(" + offset + ", " + Int(storage.Size) + ")", "(" + member.Enum.UnderlyingType + ")" + access);
        }

        return NumericStore(field.Codec, "target.Slice(" + offset + ", " + Int(field.Codec.Size) + ")", access);
    }

    /// <summary>
    ///     Whether <paramref name="composite"/> has a fixed writer: it has a fixed reader, and no member is text or a
    ///     single character (whose encoding can reject a value), in it or in any nested composite.
    /// </summary>
    private bool IsFixedWritable(GeneratedComposite composite, int depth)
    {
        if (this.fixedWritable.TryGetValue(composite, out bool known))
        {
            return known;
        }

        bool writable = depth <= FixedLayoutRule.MaximumNestingDepth && this.FixedPlanOf(composite, 0) is not null && this.MembersAreFixedWritable(composite, depth);
        this.fixedWritable[composite] = writable;
        return writable;
    }

    /// <summary>Whether every member of <paramref name="composite"/> encodes without a possible failure, nested composites included.</summary>
    /// <param name="composite">A composite that has a fixed reader.</param>
    /// <param name="depth">The nesting depth of this check.</param>
    /// <returns><see langword="true"/> when a fixed writer can store every member.</returns>
    private bool MembersAreFixedWritable(GeneratedComposite composite, int depth)
    {
        var scope = new ReaderScope(this, composite);
        foreach (CompiledField field in composite.Composite.Fields)
        {
            GeneratedMember member = scope.Member(field) ?? throw new InvalidOperationException("No generated member for field " + field.Name);
            if (member.Composite is { } nested)
            {
                if (!this.IsFixedWritable(nested, depth + 1))
                {
                    return false;
                }

                continue;
            }

            if (field.IsCharacterArray || field.Codec.Kind == PrimitiveCodecKind.Char)
            {
                return false;
            }

            if (member.Enum is not null && field.Array.Kind != CompiledArrayKind.Scalar && field.FixedElementSize != PrimitiveCodec.Resolve(member.Enum.Compiled.Integer.StorageType, field.Codec.LittleEndian).Size)
            {
                return false;
            }
        }

        return true;
    }
}
