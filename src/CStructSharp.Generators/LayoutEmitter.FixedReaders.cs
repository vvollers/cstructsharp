namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using static CStructSharp.Generators.Emit;

/// <summary>
///     The fixed readers: for a struct whose every member sits at an offset known at build time, a
///     <c>Read&lt;Type&gt;Fixed(source, trimFixedText)</c> that decodes each member at its constant offset, the
///     build-time counterpart of the runtime's static read plan. The composite's generated <c>Read&lt;Type&gt;</c> tries it first
///     through <c>ReadCursor.TryTakeFixed</c>, which hands over the struct's bytes only when the member-by-member reader would
///     read exactly those bytes without a failure (enough input, within the read budget and the nesting and array
///     limits, and on the struct's alignment); any other input takes the member-by-member reader, which reports any
///     failure.
/// </summary>
internal sealed partial class LayoutEmitter
{
    // The eligibility of each composite, decided once; null for a composite that needs the member-by-member reader.
    private readonly Dictionary<GeneratedComposite, FixedPlan?> fixedPlans = new(ReferenceEqualityComparer.Instance);

    /// <summary>Emits the fixed readers of every eligible composite.</summary>
    /// <param name="writer">The generated source destination.</param>
    private void EmitFixedReaders(SourceWriter writer)
    {
        foreach (GeneratedComposite composite in this.model.Composites)
        {
            if (this.FixedPlanOf(composite, 0) is { } plan)
            {
                writer.Line();
                this.EmitFixedReader(writer, composite, plan);
            }
        }
    }

    /// <summary>Emits the fast-path test at the top of a composite's <c>Read&lt;Type&gt;</c>, when it has a fixed reader.</summary>
    /// <param name="writer">The generated source destination, inside the reader before the composite is entered.</param>
    /// <param name="composite">The composite being read.</param>
    /// <returns>Whether the composite has a fixed reader, so that the test was emitted.</returns>
    private bool EmitFixedReaderShortcut(SourceWriter writer, GeneratedComposite composite)
    {
        if (this.FixedPlanOf(composite, 0) is not { } plan)
        {
            return false;
        }

        // Alignment only constrains the start in an aligned layout; a packed layout places members by size alone.
        writer.Open("if (cursor.TryTakeFixed(" + Int(plan.Size) + ", " + Int(this.request.Settings.Aligned ? plan.Alignment : 1) + ", " + plan.ChargedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Int(plan.NestingLevels) + ", " + Int(plan.MaximumArrayCount) + ", out global::System.ReadOnlySpan<byte> fixedBytes))");
        writer.Line("return Read" + composite.Name + "Fixed(fixedBytes, cursor.TrimFixedText);");
        writer.Close();
        return true;
    }

    /// <summary>Emits one composite's fixed reader.</summary>
    /// <param name="writer">The generated source destination.</param>
    /// <param name="composite">An eligible composite.</param>
    /// <param name="plan">Its plan.</param>
    private void EmitFixedReader(SourceWriter writer, GeneratedComposite composite, FixedPlan plan)
    {
        string name = composite.Name;
        writer.Line("/// <summary>Reads one <c>" + composite.LayoutName + "</c> from exactly its " + Int(plan.Size) + " bytes, each member at its constant offset (reached only through <c>ReadCursor.TryTakeFixed</c>).</summary>");
        writer.Line("/// <param name=\"source\">The struct's bytes.</param>");
        writer.Line("/// <param name=\"trimFixedText\">Whether fixed-capacity text drops its trailing NUL padding.</param>");
        writer.Line("/// <returns>The value.</returns>");
        writer.Open("private static " + name + " Read" + name + "Fixed(global::System.ReadOnlySpan<byte> source, bool trimFixedText)");
        writer.Line("var value = new " + name + "();");
        var scope = new ReaderScope(this, composite);
        foreach (CompiledField field in composite.Composite.Fields)
        {
            GeneratedMember member = scope.Member(field) ?? throw new InvalidOperationException("No generated member for field " + field.Name);
            this.EmitFixedMember(writer, field, member, "value." + member.PropertyName);
        }

        writer.Line("return value;");
        writer.Close();
    }

    /// <summary>Emits the statement that decodes one member of a fixed reader.</summary>
    private void EmitFixedMember(SourceWriter writer, CompiledField field, GeneratedMember member, string property)
    {
        int offset = field.FixedOffset ?? throw new InvalidOperationException("Fixed member without an offset: " + field.Name);
        writer.Line("// " + DescribeDeclaration(field));
        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            writer.Line(property + " = " + this.FixedElement(field, member, "source", Int(offset)) + ";");
            return;
        }

        int count = field.Array.FixedCount ?? throw new InvalidOperationException("Fixed array without a count: " + field.Name);
        if (field.IsCharacterArray)
        {
            // The member-by-member reader's TakeFixedText: one Latin-1 character per byte, TrimFixedText applied.
            writer.Line(property + " = " + CodecClass + ".DecodeFixedText(source.Slice(" + Int(offset) + ", " + Int(count) + "), trimFixedText);");
            return;
        }

        string elementType = ElementType(member.TypeName, 1);
        writer.Open(string.Empty);
        writer.Line("var elements = new " + elementType + "[" + Int(count) + "];");
        if (member.Composite is null && member.Enum is null)
        {
            // The bulk decode the member-by-member reader applies to the same bytes.
            writer.Line("global::System.ReadOnlySpan<byte> bytes = source.Slice(" + Int(offset) + ", " + Int(count * field.Codec.Size) + ");");
            writer.Line(BulkDecode(field.Codec, elementType, "bytes", "elements"));
        }
        else
        {
            int stride = field.FixedElementSize ?? throw new InvalidOperationException("Fixed array without an element size: " + field.Name);
            writer.Open("for (int index = 0; index < " + Int(count) + "; index++)");
            writer.Line("elements[index] = " + this.FixedElement(field, member, "source", Int(offset) + " + index * " + Int(stride)) + ";");
            writer.Close();
        }

        writer.Line(property + " = elements;");
        writer.Close();
    }

    /// <summary>The expression decoding one scalar or array element of a fixed member at <paramref name="offset"/>.</summary>
    private string FixedElement(CompiledField field, GeneratedMember member, string source, string offset)
    {
        if (member.Composite is { } nested)
        {
            int size = nested.Composite.Symbol.FixedSize ?? throw new InvalidOperationException("Fixed composite without a size: " + nested.Name);
            return "Read" + nested.Name + "Fixed(" + source + ".Slice(" + offset + ", " + Int(size) + "), trimFixedText)";
        }

        string slice = source + ".Slice(" + offset + ", " + Int(field.Codec.Size) + ")";
        return member.Enum is not null ? "(" + ElementType(member.TypeName, 1) + ")" + NumericDecode(field.Codec, slice) : NumericDecode(field.Codec, slice);
    }

    /// <summary>
    ///     Decides whether <paramref name="composite"/> has a fixed reader: the conditions of the runtime's static read
    ///     plan, applied to what the generated member-by-member reader does, and the same for every nested composite.
    /// </summary>
    /// <param name="composite">The composite.</param>
    /// <param name="depth">The nesting depth of this check, bounded like the runtime's plan builder.</param>
    /// <returns>The plan, or <see langword="null"/> when a member needs the member-by-member reader.</returns>
    private FixedPlan? FixedPlanOf(GeneratedComposite composite, int depth)
    {
        if (this.fixedPlans.TryGetValue(composite, out FixedPlan? known))
        {
            return known;
        }

        FixedPlan? plan = depth > FixedLayoutRule.MaximumNestingDepth ? null : this.BuildFixedPlan(composite, depth);
        this.fixedPlans[composite] = plan;
        return plan;
    }

    /// <summary>Checks every member of <paramref name="composite"/> and sums what its fixed reader needs; see <see cref="FixedPlanOf"/>.</summary>
    /// <param name="composite">The composite.</param>
    /// <param name="depth">The nesting depth of this check.</param>
    /// <returns>The plan, or <see langword="null"/> when a member needs the member-by-member reader.</returns>
    private FixedPlan? BuildFixedPlan(GeneratedComposite composite, int depth)
    {
        CompiledCompositeType compiled = composite.Composite;
        if (!FixedLayoutRule.IsFixedComposite(compiled) || compiled.Symbol.FixedSize is not int size)
        {
            return null;
        }

        var scope = new ReaderScope(this, composite);
        long charged = 0;
        int nesting = 1;
        int arrays = 0;
        foreach (CompiledField field in compiled.Fields)
        {
            // A fixed member (FixedLayoutRule) that is named: the member-by-member reader then only seeks, takes and
            // decodes, which the fixed reader reproduces. Unnamed padding has no member, so it takes that reader.
            if (!FixedLayoutRule.IsFixedMember(field) || field.IsUnnamed || scope.Member(field) is not { IsConditional: false } member)
            {
                return null;
            }

            bool scalar = field.Array.Kind == CompiledArrayKind.Scalar;
            int count = field.Array.FixedCount ?? 1;
            if (!scalar)
            {
                arrays = Math.Max(arrays, count);
            }

            if (member.Composite is { } nested)
            {
                if (nested.IsUnion || this.FixedPlanOf(nested, depth + 1) is not { } nestedPlan || (!scalar && field.FixedElementSize != nestedPlan.Size))
                {
                    return null;
                }

                charged += nestedPlan.ChargedBytes * count;
                nesting = Math.Max(nesting, 1 + nestedPlan.NestingLevels);
                arrays = Math.Max(arrays, nestedPlan.MaximumArrayCount);
                continue;
            }

            bool fixedText = !scalar && field.IsCharacterArray && !field.IsWideCharElement && field.Codec.Size == 1 && !BoundedTextCodec.IsType(field.TypeSpelling);
            bool numeric = member.Enum is null
                               ? (scalar ? IsFixedDecodable(field.Codec) : field.Codec.IsFixedWidthNumeric && !field.IsCharacterArray && !BoundedTextCodec.IsType(field.TypeSpelling))
                               : field.Codec.IsFixedWidthNumeric;
            if (!fixedText && !numeric)
            {
                return null;
            }

            charged += (long)field.Codec.Size * count;
        }

        return new FixedPlan(size, compiled.Symbol.Alignment, charged, nesting, arrays);
    }

    /// <summary>Whether a scalar codec is read by the member-by-member reader's fixed-width numeric decode.</summary>
    private static bool IsFixedDecodable(PrimitiveCodec codec)
    {
        switch (codec.Kind)
        {
        case PrimitiveCodecKind.UInt8:
        case PrimitiveCodecKind.Int8:
        case PrimitiveCodecKind.Bool:
        case PrimitiveCodecKind.Char:
        case PrimitiveCodecKind.WChar:
        case PrimitiveCodecKind.Int16:
        case PrimitiveCodecKind.UInt16:
        case PrimitiveCodecKind.Int24:
        case PrimitiveCodecKind.UInt24:
        case PrimitiveCodecKind.Int32:
        case PrimitiveCodecKind.UInt32:
        case PrimitiveCodecKind.Int48:
        case PrimitiveCodecKind.UInt48:
        case PrimitiveCodecKind.Int64:
        case PrimitiveCodecKind.UInt64:
        case PrimitiveCodecKind.Int128:
        case PrimitiveCodecKind.UInt128:
        case PrimitiveCodecKind.Float16:
        case PrimitiveCodecKind.Float32:
        case PrimitiveCodecKind.Float64:
            return true;
        default:
            return false;
        }
    }

    /// <summary>What <c>ReadCursor.TryTakeFixed</c> checks before handing over a fixed composite's bytes.</summary>
    /// <param name="Size">The composite's storage size, tail padding included.</param>
    /// <param name="Alignment">The composite's alignment, which its start must meet in an aligned layout.</param>
    /// <param name="ChargedBytes">The bytes the member-by-member reader charges to the read budget (members only, not padding).</param>
    /// <param name="NestingLevels">The composite levels it enters, itself included.</param>
    /// <param name="MaximumArrayCount">The largest fixed array count it reads, for the array element limit.</param>
    private sealed record FixedPlan(int Size, int Alignment, long ChargedBytes, int NestingLevels, int MaximumArrayCount);
}
