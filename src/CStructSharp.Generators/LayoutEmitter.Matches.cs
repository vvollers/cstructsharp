namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using static CStructSharp.Generators.Emit;

/// <summary>
///     <c>Matches</c>: a static, non-throwing recognition check for a struct with <c>@expect</c> conditions. It decodes only
///     the members the conditions name, each at its build-time offset straight from the bytes, so a probe that asks "is this
///     the format?" builds no value, reads nothing else and throws nothing.
/// </summary>
internal sealed partial class LayoutEmitter
{
    /// <summary>One <c>@expect</c> condition <c>Matches</c> evaluates.</summary>
    /// <param name="Constraint">The condition.</param>
    /// <param name="Code">Its C# expression over the decoded members.</param>
    private sealed record MatchCondition(CompiledConstraint Constraint, string Code);

    /// <summary>
    ///     Emits <c>Matches&lt;Name&gt;</c> (and <c>Matches</c> for the root) for a struct that has an <c>@expect</c> condition it can
    ///     evaluate from members at build-time offsets: a condition that names a member after a runtime-sized array, inside an
    ///     array, a conditional arm or a nested struct is not evaluated, so <c>Matches</c> is a necessary check, not a complete one.
    /// </summary>
    /// <param name="writer">The generated source destination.</param>
    /// <param name="composite">The composite whose parse overloads were just emitted.</param>
    /// <param name="isRoot">Whether the composite is the root declaration.</param>
    private void EmitMatches(SourceWriter writer, GeneratedComposite composite, bool isRoot)
    {
        if (composite.IsUnion || !composite.Composite.HasConstraints)
        {
            return;
        }

        ViewPlan plan = this.PlannedView(composite);
        var scope = new ReaderScope(this, composite);
        string? savedOrder = this.runtimeOrder;
        this.UseRuntimeOrder("littleEndian");
        var decoded = new List<(GeneratedMember Member, ViewSlot Slot)>();
        var conditions = new List<MatchCondition>();
        foreach (CompiledField field in composite.Composite.Fields)
        {
            if (scope.Member(field) is not { } member || !plan.Slots.TryGetValue(field, out ViewSlot? slot) || slot.Constant is null)
            {
                continue;
            }

            // The member is visible to its own conditions and to the later ones, as in the reader; only a scalar number, enum,
            // identifier or bitfield is decoded here.
            if (member.Composite is null && this.ViewPublishes(member))
            {
                scope.Publish(member, "m_" + member.PropertyName);
                decoded.Add((member, slot));
            }

            foreach (CompiledConstraint constraint in field.Constraints)
            {
                if (constraint.Kind != ConstraintKind.Malformed)
                {
                    continue;
                }

                string code = scope.Expressions.Emit(constraint.Expression);
                if (!UnresolvableInView.IsMatch(code))
                {
                    conditions.Add(new MatchCondition(constraint, code));
                }
            }
        }

        if (conditions.Count == 0)
        {
            this.runtimeOrder = savedOrder;
            return;
        }

        // Only the members a condition names are decoded, and the source must hold the furthest of them.
        List<(GeneratedMember Member, ViewSlot Slot)> used = decoded.Where(entry => conditions.Any(condition => NamesLocal(condition.Code, "m_" + entry.Member.PropertyName))).ToList();
        int needed = used.Count == 0 ? 0 : used.Max(entry => entry.Slot.Constant!.Value + DecodedSize(entry.Member.Field, entry.Member));
        string name = composite.Name;
        writer.Line();
        writer.Line("/// <summary>Whether <paramref name=\"source\"/> satisfies the <c>@expect</c> conditions of a <c>" + composite.LayoutName + "</c> that it can evaluate from members at build-time offsets: it decodes only the members the conditions name, builds no value, and does not throw for a source that is too short or a condition that cannot be evaluated (they do not match). A condition on a member after a runtime-sized array, in a conditional arm or in a nested struct is not evaluated, and <c>@supports</c> conditions are not checked (an unsupported value is still recognized), so a match is not a promise that a read succeeds.</summary>");
        writer.Line("/// <param name=\"source\">The bytes; offset 0 is the struct's first byte.</param>");
        this.EmitOrderDoc(writer);
        writer.Line(VariablesDoc);
        writer.Line("/// <returns><see langword=\"true\"/> when every condition evaluated holds.</returns>");
        writer.Open("public static bool Matches" + name + "(global::System.ReadOnlySpan<byte> source, " + this.OrderParameter + VariablesType + " variables = null)");
        if (needed > 0)
        {
            writer.Open("if (source.Length < " + Int(needed) + ")");
            writer.Line("return false;");
            writer.Close();
        }

        foreach ((GeneratedMember member, ViewSlot slot) in used)
        {
            writer.Line("var m_" + member.PropertyName + " = " + this.MatchDecode(member, "source.Slice(" + Int(slot.Constant!.Value) + ", " + Int(DecodedSize(member.Field, member)) + ")") + ";");
        }

        foreach (MatchCondition condition in conditions)
        {
            writer.Line("// @expect(" + SingleLine(condition.Constraint.Text) + ")");
            if (CannotFail(condition.Constraint.Expression, condition.Code))
            {
                writer.Line("if (" + condition.Code + " == 0)");
                writer.Line("{");
                writer.Line("    return false;");
                writer.Line("}");
            }
            else
            {
                writer.Open("try");
                writer.Open("if (" + condition.Code + " == 0)");
                writer.Line("return false;");
                writer.Close();
                writer.Close();
                writer.Open("catch (global::System.Exception exception) when (" + Cursor + ".IsExpressionFailure(exception))");
                writer.Line("return false;");
                writer.Close();
            }
        }

        writer.Line("return true;");
        writer.Close();
        if (isRoot)
        {
            writer.Line();
            writer.Line("/// <summary>Whether <paramref name=\"source\"/> matches the root declaration (<c>" + composite.LayoutName + "</c>); see " + Cref("Matches" + name + "(global::System.ReadOnlySpan{byte}, " + this.OrderCref + VariablesCref + ")") + ".</summary>");
            writer.Line("/// <param name=\"source\">The bytes; offset 0 is the struct's first byte.</param>");
            this.EmitOrderDoc(writer);
            writer.Line(VariablesDoc);
            writer.Line("/// <returns><see langword=\"true\"/> when every condition evaluated holds.</returns>");
            writer.Line("public static bool Matches(global::System.ReadOnlySpan<byte> source, " + this.OrderParameter + VariablesType + " variables = null) => Matches" + name + "(source, " + this.OrderArgument + "variables);");
        }

        this.runtimeOrder = savedOrder;
    }

    /// <summary>Whether generated code uses a local by its whole name, so that <c>m_Flag</c> is not found in <c>m_FlagMask</c>.</summary>
    /// <param name="code">The C# expression.</param>
    /// <param name="local">The local's name.</param>
    /// <returns><see langword="true"/> when the name occurs delimited by non-identifier characters.</returns>
    private static bool NamesLocal(string code, string local)
    {
        for (int index = code.IndexOf(local, StringComparison.Ordinal); index >= 0; index = code.IndexOf(local, index + 1, StringComparison.Ordinal))
        {
            int end = index + local.Length;
            bool startsName = index == 0 || !IsIdentifierPart(code[index - 1]);
            if (startsName && (end == code.Length || !IsIdentifierPart(code[end])))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>Whether a character can continue a C# identifier.</summary>
    /// <param name="character">The character.</param>
    /// <returns>Whether it is a letter, digit or underscore.</returns>
    private static bool IsIdentifierPart(char character) => char.IsLetterOrDigit(character) || character == '_';

    /// <summary>The bytes one decoded member occupies: the bitfield's storage unit, the enum's storage or the number's width.</summary>
    /// <param name="field">The field.</param>
    /// <param name="member">The field's generated member.</param>
    /// <returns>The size in bytes.</returns>
    private static int DecodedSize(CompiledField field, GeneratedMember member)
    {
        if (field.BitSize > 0)
        {
            return field.BitUnitSize ?? field.BitStorageSize ?? field.Codec.Size;
        }

        return member.Enum is not null ? PrimitiveCodec.Resolve(member.Enum.Compiled.Integer.StorageType, field.Codec.LittleEndian).Size : field.Codec.Size;
    }

    /// <summary>The expression that decodes a scalar member from exactly its bytes, as its view property does.</summary>
    /// <param name="member">The member: a bitfield, an enum or a fixed-width number, identifier or fixed-point value.</param>
    /// <param name="slice">An expression for exactly the member's bytes.</param>
    /// <returns>The decode expression.</returns>
    private string MatchDecode(GeneratedMember member, string slice)
    {
        CompiledField field = member.Field;
        if (field.BitSize > 0)
        {
            int unitSize = field.BitUnitSize ?? field.BitStorageSize ?? field.Codec.Size;
            bool littleEndian = field.BitStorageIsLittleEndian ?? true;
            string cast = "(" + member.TypeName + ")" + (member.Enum is not null ? "(" + member.Enum.UnderlyingType + ")" : string.Empty);
            return cast + CodecClass + ".ExtractBits(" + CodecClass + ".ReadUnsigned(" + slice + ", " + this.EndianExpression(field, littleEndian) + "), " + CodecClass + ".BitfieldShift(" + Int(field.BitOffset) + ", " + Int(field.BitSize) + ", " + Int(unitSize * 8) + ", HighBitFirst), " + Int(field.BitSize) + ")";
        }

        if (member.Enum is not null)
        {
            PrimitiveCodec storage = PrimitiveCodec.Resolve(member.Enum.Compiled.Integer.StorageType, field.Codec.LittleEndian);
            return "(" + member.TypeName + ")" + NumericDecode(storage, slice, this.EndianOf(field));
        }

        return ScalarDecode(field.Codec, slice, this.EndianOf(field));
    }
}
