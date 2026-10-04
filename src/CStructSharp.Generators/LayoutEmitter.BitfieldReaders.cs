namespace CStructSharp.Generators;

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CStructSharp.Compilation;
using static CStructSharp.Generators.Emit;

/// <summary>Reader-only shortcuts for fixed bitfield leaves, with the original member reader retained for every failure.</summary>
internal sealed partial class LayoutEmitter
{
    private readonly Dictionary<GeneratedComposite, BitfieldLeafPlan?> bitfieldLeafPlans = new(ReferenceEqualityComparer.Instance);

    /// <summary>Checks a bitfield-only leaf without changing the shared fixed plan that also drives writers and parent readers.</summary>
    /// <param name="composite">The generated composite to inspect.</param>
    /// <returns>Its byte extent and repeated read charges, or null when member-by-member execution is required.</returns>
    private BitfieldLeafPlan? BitfieldLeafPlanOf(GeneratedComposite composite)
    {
        if (this.bitfieldLeafPlans.TryGetValue(composite, out BitfieldLeafPlan? known))
        {
            return known;
        }

        BitfieldLeafPlan? plan = this.BuildBitfieldLeafPlan(composite);
        this.bitfieldLeafPlans[composite] = plan;
        return plan;
    }

    /// <summary>Requires constant storage windows and an implicit, side-effect-free leaf constructor.</summary>
    /// <param name="composite">The generated composite to inspect.</param>
    /// <returns>The eligible plan, or null without changing any existing execution path.</returns>
    private BitfieldLeafPlan? BuildBitfieldLeafPlan(GeneratedComposite composite)
    {
        CompiledCompositeType compiled = composite.Composite;
        if (!FixedLayoutRule.IsFixedComposite(compiled) || compiled.Symbol.FixedSize is not int size ||
            this.request.ConsumerDeclaredTypes.Contains(composite.Name))
        {
            return null;
        }

        long charged = 0;
        int named = 0;
        foreach (CompiledField field in compiled.Fields)
        {
            if (field.FixedOffset is not int offset || offset < 0 || offset > size || field.IsConditional)
            {
                return null;
            }

            if (field.IsZeroWidthBitfield)
            {
                continue;
            }

            if (field.BitSize <= 0 || field.PointerDepth != 0 || field.Array.Kind != CompiledArrayKind.Scalar ||
                field.BitUnitSize is not int unitSize || unitSize is < 1 or > 8 || unitSize > size - offset ||
                field.BitOffset < 0 || field.BitOffset + field.BitSize > unitSize * 8)
            {
                return null;
            }

            // Each declaration rereads and charges its whole window, including anonymous padding fields.
            charged += unitSize;
            if (!field.IsUnnamed)
            {
                named++;
            }
        }

        return named < 2 ? null : new BitfieldLeafPlan(size, compiled.Symbol.Alignment, charged);
    }

    /// <summary>Emits a guard that leaves all state untouched when the original reader might fail.</summary>
    /// <param name="writer">The source destination inside the composite reader.</param>
    /// <param name="composite">The eligible bitfield leaf.</param>
    /// <param name="plan">Its extent, alignment and repeated charges.</param>
    private void EmitBitfieldLeafShortcut(SourceWriter writer, GeneratedComposite composite, BitfieldLeafPlan plan)
    {
        writer.Open("if (cursor.TryTakeFixed(" + Int(plan.Size) + ", " + Int(this.request.Settings.Aligned ? plan.Alignment : 1) + ", " + plan.ChargedBytes.ToString(CultureInfo.InvariantCulture) + ", 1, 0, out global::System.ReadOnlySpan<byte> bitfieldBytes))");
        writer.Line("return Read" + composite.Name + "Bitfields(bitfieldBytes);");
        writer.Close();
    }

    /// <summary>Emits constant-offset leaf readers without making their parents eligible for a fixed shortcut.</summary>
    /// <param name="writer">The generated source destination.</param>
    private void EmitBitfieldLeafReaders(SourceWriter writer)
    {
        foreach (GeneratedComposite composite in this.model.Composites)
        {
            if (this.BitfieldLeafPlanOf(composite) is null)
            {
                continue;
            }

            writer.Line();
            writer.Line("/// <summary>Decodes a complete bitfield leaf after the cursor validated its extent and every repeated read charge.</summary>");
            writer.Line("/// <param name=\"source\">The complete leaf's borrowed bytes.</param>");
            writer.Line("/// <returns>A new owned leaf with every declared value.</returns>");
            writer.Open("private static " + composite.Name + " Read" + composite.Name + "Bitfields(global::System.ReadOnlySpan<byte> source)");
            writer.Line("var value = new " + composite.Name + "();");
            var scope = new ReaderScope(this, composite);
            var units = new Dictionary<(int Offset, int Size, bool LittleEndian), string>();
            foreach (CompiledField field in composite.Composite.Fields)
            {
                if (field.IsZeroWidthBitfield || field.IsUnnamed)
                {
                    continue;
                }

                int offset = field.FixedOffset!.Value;
                int size = field.BitUnitSize!.Value;
                bool littleEndian = field.BitStorageIsLittleEndian ?? true;
                var key = (offset, size, littleEndian);
                if (!units.TryGetValue(key, out string? unit))
                {
                    unit = "unit" + Int(units.Count);
                    units.Add(key, unit);
                    writer.Line("ulong " + unit + " = " + CodecClass + ".ReadUnsigned(source.Slice(" + Int(offset) + ", " + Int(size) + "), " + Bool(littleEndian) + ");");
                }

                GeneratedMember member = scope.Member(field)!;
                int shift = this.compilation.HighBitFirst ? (size * 8) - field.BitOffset - field.BitSize : field.BitOffset;
                string cast = "(" + member.TypeName + ")" + (member.Enum is not null ? "(" + member.Enum.UnderlyingType + ")" : string.Empty);
                writer.Line("value." + member.PropertyName + " = " + cast + CodecClass + ".ExtractBits(" + unit + ", " + Int(shift) + ", " + Int(field.BitSize) + ");");
            }

            writer.Line("return value;");
            writer.Close();
        }
    }

    /// <summary>The facts checked without consuming input before entering an eligible leaf.</summary>
    /// <param name="Size">The full byte extent, including tail padding and separators.</param>
    /// <param name="Alignment">The alignment required to relocate compiled offsets.</param>
    /// <param name="ChargedBytes">The sum of each declaration's storage window, including repeated windows.</param>
    private sealed record BitfieldLeafPlan(int Size, int Alignment, long ChargedBytes);
}
