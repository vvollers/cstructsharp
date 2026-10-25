namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Linq;
using CStructSharp.Compilation;
using static CStructSharp.Generators.Emit;

/// <summary>
///     The constraint pass of a fixed class-shaped struct that runs on the struct's bytes, before the object is built. A
///     non-throwing <c>TryParse</c> of input that breaks a condition then returns the failure without having allocated the value it
///     would throw away. The pass decodes only the members the conditions name, as <c>Matches</c> does, and evaluates the same
///     condition code in the same order, so it fails with the failure (and collects the violations) of the pass over the object.
/// </summary>
internal sealed partial class LayoutEmitter
{
    /// <summary>The members a bytes-first pass decodes, each with its place in the struct.</summary>
    /// <param name="Decoded">The members the conditions name, in declaration order.</param>
    private sealed record BytesCheck(IReadOnlyList<(GeneratedMember Member, ViewSlot Slot)> Decoded);

    // The decision for each composite, made once; null for a composite whose conditions need more than its scalar members.
    private readonly Dictionary<GeneratedComposite, BytesCheck?> bytesChecks = new(ReferenceEqualityComparer.Instance);

    /// <summary>
    ///     Decides whether a fixed composite's conditions can be evaluated from its bytes: the shape is a class (a value type is
    ///     not allocated), and every condition names only scalar members at build-time offsets and caller variables.
    /// </summary>
    /// <param name="composite">A composite with a fixed reader and conditions.</param>
    /// <returns>The members to decode, or <see langword="null"/> when the object has to be built first.</returns>
    private BytesCheck? BytesCheckOf(GeneratedComposite composite)
    {
        if (!this.bytesChecks.TryGetValue(composite, out BytesCheck? check))
        {
            check = this.PlanBytesCheck(composite);
            this.bytesChecks[composite] = check;
        }

        return check;
    }

    /// <summary>Plans the bytes-first pass of one composite; see <see cref="BytesCheckOf"/>.</summary>
    /// <param name="composite">The composite.</param>
    /// <returns>The plan, or <see langword="null"/>.</returns>
    private BytesCheck? PlanBytesCheck(GeneratedComposite composite)
    {
        if (this.UsesValueTypes || composite.IsUnion || !composite.Composite.HasConstraints)
        {
            return null;
        }

        ViewPlan plan = this.PlannedView(composite);
        var scope = new ReaderScope(this, composite);
        string? savedOrder = this.runtimeOrder;
        this.UseRuntimeOrder("littleEndian");
        try
        {
            var decoded = new List<(GeneratedMember Member, ViewSlot Slot)>();
            var codes = new List<string>();
            foreach (CompiledField field in composite.Composite.Fields)
            {
                if (scope.Member(field) is not { } member)
                {
                    return null;
                }

                if (member.Composite is null && this.ViewPublishes(member) && plan.Slots.TryGetValue(field, out ViewSlot? slot) && slot.Constant is not null)
                {
                    scope.Publish(member, "m_" + member.PropertyName);
                    decoded.Add((member, slot));
                }
                else
                {
                    // A member this pass does not decode is undefined: a condition that names it sends the struct to the pass over the value.
                    scope.PublishUndefined(member);
                }

                if (!field.ByteMatches.IsEmpty)
                {
                    // The array's bytes are at a build-time offset in the struct's bytes, or the pass over the value does the check.
                    scope.PublishByteMatches(member, plan.Slots.TryGetValue(field, out ViewSlot? arraySlot) && arraySlot.Constant is not null && field.FixedStorageSize is not null ? "bytes" : null, rawBytes: true);
                }

                foreach (CompiledConstraint constraint in field.Constraints)
                {
                    string code = scope.Expressions.Emit(constraint.Expression);
                    if (UnresolvableInView.IsMatch(code))
                    {
                        return null;
                    }

                    codes.Add(code);
                }
            }

            return new BytesCheck(decoded.Where(entry => codes.Any(code => NamesLocal(code, "m_" + entry.Member.PropertyName))).ToList());
        }
        finally
        {
            this.runtimeOrder = savedOrder;
        }
    }

    /// <summary>Emits the bytes-first constraint pass of a composite, when it has one.</summary>
    /// <param name="writer">The generated source destination.</param>
    /// <param name="composite">A constrained composite with a fixed reader.</param>
    private void EmitFixedBytesConstraintCheck(SourceWriter writer, GeneratedComposite composite)
    {
        if (this.BytesCheckOf(composite) is not { } check)
        {
            return;
        }

        string name = composite.Name;
        writer.Line();
        writer.Line("/// <summary>Checks the <c>@expect</c> and <c>@supports</c> conditions of a <c>" + composite.LayoutName + "</c> on its bytes, before the value is built, in field order and with the failures of <c>TryCheck" + name + "Constraints</c>.</summary>");
        writer.Line("/// <param name=\"cursor\">The cursor, which sits after the struct.</param>");
        writer.Line(VariablesDoc);
        writer.Line("/// <param name=\"bytes\">Exactly the struct's bytes.</param>");
        this.EmitOrderDoc(writer);
        writer.Line("/// <param name=\"start\">The position of the struct's first byte.</param>");
        writer.Line("/// <param name=\"failure\">The first condition that is not satisfied (its position is attached when it leaves the operation), or <see langword=\"null\"/>; null as well when violations are being collected.</param>");
        writer.Line("/// <returns><see langword=\"false\"/> when a condition stopped the check.</returns>");
        writer.Open("private static bool TryCheck" + name + "ConstraintsOfBytes(ref " + Cursor + " cursor, " + VariablesType + " variables, global::System.ReadOnlySpan<byte> bytes, " + this.OrderParameter + "int start, out global::CStructSharp.Diagnostics.CStructConstraintException? failure)");
        writer.Line("failure = null;");
        string? savedOrder = this.runtimeOrder;
        this.UseRuntimeOrder("littleEndian");
        foreach ((GeneratedMember member, ViewSlot slot) in check.Decoded)
        {
            writer.Line("var m_" + member.PropertyName + " = " + this.MatchDecode(member, "bytes.Slice(" + Int(slot.Constant!.Value) + ", " + Int(DecodedSize(member.Field, member)) + ")") + ";");
        }

        ViewPlan plan = this.PlannedView(composite);
        var scope = new ReaderScope(this, composite);
        var decodedFields = new HashSet<CompiledField>(check.Decoded.Select(entry => entry.Member.Field), ReferenceEqualityComparer.Instance);
        ConstraintSchedule schedule = ConstraintSchedule.Create(composite.Composite.Fields);
        var held = new Dictionary<(int Field, int Ordinal), string>();
        for (int index = 0; index < composite.Composite.Fields.Length; index++)
        {
            CompiledField field = composite.Composite.Fields[index];
            GeneratedMember member = scope.Member(field) ?? throw new InvalidOperationException("No generated member for field " + field.Name);
            if (decodedFields.Contains(field))
            {
                scope.Publish(member, "m_" + member.PropertyName);
            }
            else
            {
                scope.PublishUndefined(member);
            }

            if (!field.ByteMatches.IsEmpty)
            {
                scope.PublishByteMatches(member, "bytes.Slice(" + Int(plan.Slots[field].Constant!.Value) + ", " + Int(field.FixedStorageSize ?? 0) + ")", rawBytes: true);
            }

            EmitPassConstraints(writer, composite.Composite.Fields, schedule, held, index, scope);
        }

        this.runtimeOrder = savedOrder;
        writer.Line("return true;");
        writer.Close();
    }
}
