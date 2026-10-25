namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using static CStructSharp.Generators.Emit;

/// <summary>
///     Where a view finds each member. A member whose offset the layout compiler knows is read at a constant; a member
///     placed after a runtime-sized array is read at an offset the view computes from the members before it, with the
///     reader's own count checks. The plan also records which members the view can expose at all.
/// </summary>
internal sealed partial class LayoutEmitter
{
    /// <summary>The caller variables a view without variables evaluates expressions with: none, so the defines keep their defaults.</summary>
    private const string NoVariables = "global::System.Collections.Generic.IReadOnlyDictionary<string, int>? variables = null;";

    // The plan of each view, made on first use.
    private readonly Dictionary<GeneratedComposite, ViewPlan> viewPlans = new(ReferenceEqualityComparer.Instance);

    /// <summary>How a view reaches the first byte of one member.</summary>
    private sealed class ViewSlot
    {
        /// <summary>Creates a slot at a build-time offset.</summary>
        /// <param name="constant">The offset from the view's first byte.</param>
        public ViewSlot(int constant)
        {
            this.Constant = constant;
        }

        /// <summary>Creates a slot at an offset computed when the member is read.</summary>
        /// <param name="method">The name of the private view method that returns the offset as a <see cref="long"/>.</param>
        public ViewSlot(string method)
        {
            this.Method = method;
        }

        /// <summary>Gets the build-time offset, or <see langword="null"/> when the view computes it.</summary>
        public int? Constant { get; }

        /// <summary>Gets the offset method's name, or <see langword="null"/> for a build-time offset.</summary>
        public string? Method { get; }

        /// <summary>Gets a value indicating whether the offset is computed when the member is read.</summary>
        public bool IsDynamic => this.Constant is null;

        /// <summary>Gets the C# expression of the offset: a literal, or a call that returns a <see cref="long"/>.</summary>
        public string At => this.Constant is { } constant ? Int(constant) : this.Method + "()";

        /// <summary>Gets the words that finish "at ..." in a member's documentation.</summary>
        public string Where => this.Constant is { } constant ? "offset " + Int(constant) : "a run-time offset";
    }

    /// <summary>The slots of one composite's view and the helper methods the dynamic ones need.</summary>
    private sealed class ViewPlan
    {
        /// <summary>Gets the slot of every member the view can place, by compiled field.</summary>
        public Dictionary<CompiledField, ViewSlot> Slots { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>Gets the count method name and the translated count expression of every runtime-sized array the view can place.</summary>
        public Dictionary<CompiledField, (string Method, string Code, bool IsLiteral)> Counts { get; } = new(ReferenceEqualityComparer.Instance);

        /// <summary>Gets the <c>@expect</c> and <c>@supports</c> checks <c>Validate()</c> runs: the field, the condition, its C# expression over the view's members and the <see cref="int"/> expression of the offset just after the field (<see langword="null"/> when its size is not known).</summary>
        public List<(CompiledField Field, CompiledConstraint Constraint, string Code, string? End)> Checks { get; } = new();

        /// <summary>Gets the offset methods to emit: the method name and the C# expression of its result.</summary>
        public List<(string Name, string Expression)> OffsetMethods { get; } = new();

        /// <summary>Gets the count methods already written with their member, so the rest are written after the members.</summary>
        public HashSet<string> WrittenCounts { get; } = new(StringComparer.Ordinal);

        /// <summary>Gets or sets a value indicating whether a count the view evaluates reads a caller variable (or a define a caller may override), so the layout's views carry the variables.</summary>
        public bool UsesVariables { get; set; }

        /// <summary>Gets a value indicating whether any member is read at a computed offset, so the view needs its slicing helper.</summary>
        public bool NeedsTake => this.OffsetMethods.Count > 0 || this.Counts.Count > 0;

        /// <summary>Gets the member names of the view that a helper or a derived member must not reuse.</summary>
        public HashSet<string> Taken { get; } = new(StringComparer.Ordinal) { "Bytes", "ToObject", "Enumerate" };

        /// <summary>Gets or sets the name of the checked-slice helper, chosen so that no member of the view has it.</summary>
        public string TakeName { get; set; } = "Take";

        /// <summary>Gets or sets the name of the helper that checks a slice and returns the rest of the source, or <see langword="null"/> when no member needs it.</summary>
        public string? TakeRestName { get; set; }

        /// <summary>Claims a name for a helper or a derived member, adding underscores until no member of the view has it.</summary>
        /// <param name="name">The wanted name.</param>
        /// <returns>The claimed name, which no later claim returns again.</returns>
        public string Unique(string name)
        {
            while (!this.Taken.Add(name))
            {
                name += "_";
            }

            return name;
        }
    }

    /// <summary>The plan of a composite's view, made once: deciding whether the layout's views carry variables plans them, and emitting each view then reuses its plan.</summary>
    /// <param name="composite">The struct or union the view reads.</param>
    /// <returns>The plan, which emitting the view goes on to use up.</returns>
    private ViewPlan PlannedView(GeneratedComposite composite)
    {
        if (!this.viewPlans.TryGetValue(composite, out ViewPlan? plan))
        {
            plan = this.PlanView(composite);
            this.viewPlans[composite] = plan;
        }

        return plan;
    }

    /// <summary>Places the members of one composite's view: constants where the compiler knows the offset, computed offsets after a runtime-sized array.</summary>
    /// <param name="composite">The struct or union the view reads.</param>
    /// <returns>The plan.</returns>
    private ViewPlan PlanView(GeneratedComposite composite)
    {
        var plan = new ViewPlan();
        var byField = new Dictionary<CompiledField, GeneratedMember>(ReferenceEqualityComparer.Instance);
        foreach (GeneratedMember member in composite.Members)
        {
            byField[member.Field] = member;
            plan.Taken.Add(member.PropertyName);
        }

        plan.TakeName = plan.Unique("Take");

        if (composite.IsUnion)
        {
            foreach (GeneratedMember member in composite.Members)
            {
                if (!member.IsConditional)
                {
                    plan.Slots[member.Field] = new ViewSlot(0);
                }
            }

            return plan;
        }

        // The members the compiler placed itself keep their constant offsets, exactly as before.
        foreach (GeneratedMember member in composite.Members)
        {
            CompiledField field = member.Field;
            if (!member.IsConditional && !field.IsZeroWidthBitfield && field.FixedOffset is { } offset)
            {
                plan.Slots[field] = new ViewSlot(offset + PromotedOffset(composite, field));
            }
        }

        // Then follow the fields in order, so a member after a runtime-sized array gets an offset built from the end of
        // the one before. The scope sees each member the view exposes, as the reader's scope sees each one it has read.
        var scope = new ReaderScope(this, composite);
        string? end = "0L";
        foreach (CompiledField field in composite.Composite.Fields)
        {
            if (end is null)
            {
                break;
            }

            if (field.IsUnnamed && field.IsInlineComposite)
            {
                // An anonymous struct or union with a build-time offset and size is stepped over, so the members after it
                // keep their checks; its own members were placed above and are published and checked in declaration order.
                if (field.ConditionalBranches.Length > 0 || field.FixedOffset is not { } innerOffset || field.FixedStorageSize is not { } innerSize ||
                    field.Type.Symbol.Definition is not CompiledCompositeType inline)
                {
                    break;
                }

                this.PublishAnonymousMembers(plan, scope, byField, inline, 0);
                end = Int(innerOffset + innerSize) + "L";
                continue;
            }

            if (!PlacesInSequence(field))
            {
                break;
            }

            byField.TryGetValue(field, out GeneratedMember? member);
            string start;
            if (field.FixedOffset is { } fixedOffset)
            {
                start = Int(fixedOffset) + "L";
            }
            else
            {
                start = this.request.Settings.Aligned && field.Alignment > 1 ? AlignUp(end, field.Alignment) : end;
                if (member is not null && !field.IsZeroWidthBitfield && member.PropertyName is not ("Bytes" or "ToObject"))
                {
                    string method = plan.Unique("OffsetOf" + member.PropertyName);
                    plan.OffsetMethods.Add((method, start));
                    plan.Slots[field] = new ViewSlot(method);
                    start = method + "()";
                }
            }

            end = this.EndOf(plan, scope, field, member, start);
            this.PublishAndCheck(plan, scope, field, member);
        }

        if (composite.Composite.HasMagic)
        {
            ScheduleChecks(plan, composite.Composite);
        }

        return plan;
    }

    /// <summary>
    ///     Puts a view's recorded checks in the order of the struct's <see cref="ConstraintSchedule"/>, so <c>Validate()</c> checks the
    ///     <c>@magic</c> conditions first like a read does: the checks a magic holds back are released after the last magic and report
    ///     the end of that member. A view that also checks the members of an anonymous struct keeps declaration order.
    /// </summary>
    /// <param name="plan">The plan, whose checks are in declaration order.</param>
    /// <param name="composite">The struct the view reads.</param>
    private static void ScheduleChecks(ViewPlan plan, CompiledCompositeType composite)
    {
        ImmutableArray<CompiledField> fields = composite.Fields;
        if (plan.Checks.Any(check => fields.IndexOf(check.Field) < 0))
        {
            return;
        }

        ConstraintSchedule schedule = ConstraintSchedule.Create(fields);
        if (!schedule.ReordersConstraints)
        {
            return;
        }

        var recorded = new Dictionary<(int Field, int Ordinal), (CompiledField Field, CompiledConstraint Constraint, string Code, string? End)>();
        var ends = new Dictionary<int, string?>();
        foreach ((CompiledField Field, CompiledConstraint Constraint, string Code, string? End) check in plan.Checks)
        {
            int owner = fields.IndexOf(check.Field);
            recorded[(owner, check.Field.Constraints.IndexOf(check.Constraint))] = check;
            ends[owner] = check.End;
        }

        var ordered = new List<(CompiledField Field, CompiledConstraint Constraint, string Code, string? End)>(plan.Checks.Count);
        for (int index = 0; index < fields.Length; index++)
        {
            foreach (ConstraintSchedule.Entry entry in schedule.After(index))
            {
                if (recorded.TryGetValue((entry.Field, entry.Ordinal), out (CompiledField Field, CompiledConstraint Constraint, string Code, string? End) check))
                {
                    ordered.Add((check.Field, check.Constraint, check.Code, entry.Field == index || !ends.TryGetValue(index, out string? flushEnd) ? check.End : flushEnd));
                }
            }
        }

        plan.Checks.Clear();
        plan.Checks.AddRange(ordered);
    }

    /// <summary>Publishes a placed member to the expression scope and records its <c>@expect</c> and <c>@supports</c> checks for <c>Validate()</c>.</summary>
    /// <param name="plan">The plan, which gains the checks.</param>
    /// <param name="scope">The expression scope of the members before the field.</param>
    /// <param name="field">The field, in declaration order.</param>
    /// <param name="member">The field's generated member, or <see langword="null"/> for padding.</param>
    private void PublishAndCheck(ViewPlan plan, ReaderScope scope, CompiledField field, GeneratedMember? member)
    {
        if (member is null || !plan.Slots.ContainsKey(field))
        {
            return;
        }

        if (this.ViewPublishes(member))
        {
            // A nested struct's own members are published only where its view has a property for them.
            scope.Publish(member, "this." + member.PropertyName, this.NestedViewExposes);
        }

        // A condition can name the field itself and the members before it, which are published by now.
        if (field.Constraints.IsDefaultOrEmpty)
        {
            return;
        }

        // A comparison with a byte string reads the array's bytes, which the view exposes for an array at a build-time offset.
        if (!field.ByteMatches.IsEmpty)
        {
            scope.PublishByteMatches(member, plan.Slots[field].Constant is not null && field.Array.Kind == CompiledArrayKind.Fixed ? "this." + member.PropertyName + "Bytes" : null, rawBytes: true);
        }

        // The reader reports the end of the failing field, so Validate() moves its cursor there before it throws.
        ViewSlot slot = plan.Slots[field];
        int? size = field.BitSize > 0 ? field.BitUnitSize ?? field.BitStorageSize ?? field.Codec.Size : field.FixedStorageSize;
        string? end = size is not { } bytes ? null : slot.Constant is { } constant ? Int(constant + bytes) : "(int)(" + slot.At + " + " + Int(bytes) + "L)";
        foreach (CompiledConstraint constraint in field.Constraints)
        {
            string code = scope.Expressions.Emit(constraint.Expression);
            if (UnresolvableInView.IsMatch(code))
            {
                continue;
            }

            if (VariableUse.IsMatch(code))
            {
                plan.UsesVariables = true;
            }

            plan.Checks.Add((field, constraint, code, end));
        }
    }

    /// <summary>Publishes and checks the members an anonymous struct or union promotes into its parent, in declaration order, descending into anonymous members of its own.</summary>
    /// <param name="plan">The plan, which gains the checks.</param>
    /// <param name="scope">The expression scope of the members before the anonymous member.</param>
    /// <param name="byField">The generated member of each field of the view's composite, promoted ones included.</param>
    /// <param name="holder">The anonymous composite.</param>
    /// <param name="depth">The number of anonymous levels entered, which stops a cyclic definition.</param>
    private void PublishAnonymousMembers(ViewPlan plan, ReaderScope scope, Dictionary<CompiledField, GeneratedMember> byField, CompiledCompositeType holder, int depth)
    {
        foreach (CompiledField inner in holder.Fields)
        {
            if (inner.IsUnnamed && inner.IsInlineComposite)
            {
                if (depth < FixedLayoutRule.MaximumNestingDepth && inner.Type.Symbol.Definition is CompiledCompositeType deeper)
                {
                    this.PublishAnonymousMembers(plan, scope, byField, deeper, depth + 1);
                }

                continue;
            }

            byField.TryGetValue(inner, out GeneratedMember? member);
            this.PublishAndCheck(plan, scope, inner, member);
        }
    }

    /// <summary>Whether the sequential placement can step over this field: a plain unconditional member that is not a bitfield, an anonymous composite, or an explicitly aligned one.</summary>
    /// <param name="field">The field.</param>
    /// <returns>Whether the plan can continue through the field.</returns>
    private static bool PlacesInSequence(CompiledField field)
    {
        if (field.ConditionalBranches.Length > 0 || (field.IsUnnamed && field.IsInlineComposite))
        {
            return false;
        }

        // A bitfield's storage unit is shared and allocated by the compiler: only its own offset is trusted.
        return field.FixedOffset is not null || (field.BitSize == 0 && !field.IsZeroWidthBitfield && field.Declaration.AlignmentOverrideExpression is null);
    }

    /// <summary>Whether a view property exists that a later count expression can name: a scalar number, enum, identifier, bitfield, or a nested fixed struct.</summary>
    /// <param name="member">The member.</param>
    /// <returns>Whether the scope may publish the member as <c>this.Property</c>.</returns>
    private bool ViewPublishes(GeneratedMember member)
    {
        CompiledField field = member.Field;
        if (member.IsConditional || field.Array.Kind != CompiledArrayKind.Scalar || field.PointerDepth > 0 || field.IsZeroWidthBitfield)
        {
            return false;
        }

        if (member.Composite is { IsUnion: false } nested)
        {
            return nested.Composite.Symbol.FixedSize is not null && this.request.Views;
        }

        return member.Composite is null && (field.BitSize > 0 || member.Enum is not null || field.Codec.IsFixedWidthNumeric || field.Codec.IsIdentifier || field.Codec.IsFixedPoint);
    }

    /// <summary>
    ///     Whether the view of a fixed-size nested struct has a property named like this member, of the member's type: it
    ///     must be a kind <see cref="ViewPublishes"/> accepts and sit at an offset the compiler fixed, which is where the
    ///     nested view places it.
    /// </summary>
    /// <param name="member">A member of the nested struct.</param>
    /// <returns>Whether <c>this.Nested.Property</c> exists in the generated view.</returns>
    private bool NestedViewExposes(GeneratedMember member) => member.Field.FixedOffset is not null && this.ViewPublishes(member);

    /// <summary>The C# expression of the offset just after a field, or <see langword="null"/> when its size depends on data the plan cannot step over.</summary>
    /// <param name="plan">The plan, which learns the count of a runtime-sized array.</param>
    /// <param name="scope">The expression scope of the members before the field.</param>
    /// <param name="field">The field.</param>
    /// <param name="member">The field's generated member, or <see langword="null"/> for padding.</param>
    /// <param name="start">The C# expression of the field's offset.</param>
    /// <returns>The end offset expression, or null.</returns>
    private string? EndOf(ViewPlan plan, ReaderScope scope, CompiledField field, GeneratedMember? member, string start)
    {
        if (field.BitSize > 0 || field.IsZeroWidthBitfield)
        {
            int unit = field.BitUnitSize ?? field.BitStorageSize ?? field.Codec.Size;
            return unit > 0 ? Add(start, Int(unit) + "L") : null;
        }

        if (field.FixedStorageSize is { } size)
        {
            return Add(start, Int(size) + "L");
        }

        if (member is null || field.Array.Kind != CompiledArrayKind.Runtime || field.Array.Dimensions.Length != 1 || field.PointerDepth > 0 || field.FixedElementSize is not { } element)
        {
            return null;
        }

        // The count is evaluated by the view from the members before it; a count that needs a caller variable or an
        // unread member would always fail, so such an array is not placed (and nothing after it).
        Syntax.Expr expression = field.Array.CountExpression!;
        string code = scope.Expressions.Emit(expression);
        if (UnresolvableInView.IsMatch(code))
        {
            return null;
        }

        // A caller variable (or a define the caller may override) is read from the variables the view was created with.
        if (VariableUse.IsMatch(code))
        {
            plan.UsesVariables = true;
        }

        string count = plan.Unique("CountOf" + member.PropertyName);
        plan.Counts[field] = (count, code, ExpressionEmitter.IsInt32Literal(expression));
        return Add(start, "(long)" + count + "() * " + Int(element) + "L");
    }

    /// <summary>An expression that names a member that is not read yet, which a view cannot supply.</summary>
    private static readonly System.Text.RegularExpressions.Regex UnresolvableInView = new(
        "Expressions\\.Undefined\\(",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>An expression that reads the caller's variables: a variable, one with a define's default, or an optional one.</summary>
    private static readonly System.Text.RegularExpressions.Regex VariableUse = new(
        "Expressions\\.(Try)?Variable\\(variables, ",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>
    ///     A count expression that is only the widened value of a view property (<c>((Int128)this.N)</c>, or through
    ///     nested views), which cannot raise a layout-expression failure. Anything else keeps the failure wrapper.
    /// </summary>
    private static readonly System.Text.RegularExpressions.Regex PlainPropertyCount = new(
        "^\\(\\(global::System\\.Int128\\)this(\\.[A-Za-z_][A-Za-z0-9_]*)+\\)$",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    /// <summary>The sum of two offset expressions, folded when both are literals.</summary>
    /// <param name="left">The first expression.</param>
    /// <param name="right">The second expression.</param>
    /// <returns>The expression.</returns>
    private static string Add(string left, string right)
    {
        if (left.EndsWith("L", StringComparison.Ordinal) && right.EndsWith("L", StringComparison.Ordinal)
            && long.TryParse(left.Substring(0, left.Length - 1), out long a) && long.TryParse(right.Substring(0, right.Length - 1), out long b))
        {
            return (a + b).ToString(System.Globalization.CultureInfo.InvariantCulture) + "L";
        }

        return left + " + " + right;
    }

    /// <summary>An offset expression rounded up to a multiple of a power-of-two alignment.</summary>
    /// <param name="offset">The offset expression.</param>
    /// <param name="alignment">The alignment in bytes.</param>
    /// <returns>The rounded expression.</returns>
    private static string AlignUp(string offset, int alignment)
        => "((" + offset + " + " + Int(alignment - 1) + "L) & ~" + Int(alignment - 1) + "L)";

    /// <summary>Emits the private helpers a view with computed offsets needs: the offset methods, the remaining count methods, and the checked slice.</summary>
    /// <param name="writer">The output.</param>
    /// <param name="composite">The composite the view reads.</param>
    /// <param name="plan">The view plan.</param>
    private void EmitViewHelpers(SourceWriter writer, GeneratedComposite composite, ViewPlan plan)
    {
        foreach (KeyValuePair<CompiledField, (string Method, string Code, bool IsLiteral)> count in plan.Counts)
        {
            if (plan.WrittenCounts.Add(count.Value.Method))
            {
                GeneratedMember member = composite.Members.First(candidate => ReferenceEquals(candidate.Field, count.Key));
                this.EmitCountMethod(writer, member, count.Value, plan);
            }
        }

        foreach ((string name, string expression) in plan.OffsetMethods)
        {
            writer.Line();
            writer.Line("/// <summary>The offset of the member after the runtime-sized array before it, from the start of the source.</summary>");
            writer.Line("/// <returns>The offset in bytes.</returns>");
            writer.Line("private long " + name + "() => " + expression + ";");
        }

        if (!plan.NeedsTake)
        {
            return;
        }

        string layout = SourceWriter.Literal(composite.LayoutName);
        writer.Line();
        writer.Line("/// <summary>Slices bytes out of the source, failing with the reader's short-read text when the source ends first.</summary>");
        writer.Line("/// <param name=\"offset\">The first byte.</param>");
        writer.Line("/// <param name=\"length\">The number of bytes.</param>");
        writer.Line("/// <param name=\"member\">The member, for the failure.</param>");
        writer.Line("/// <param name=\"memberType\">The member's type, for the failure.</param>");
        writer.Line("/// <returns>The bytes.</returns>");
        writer.Open("private global::System.ReadOnlySpan<byte> " + plan.TakeName + "(long offset, long length, string member, string memberType)");
        writer.Open("if (offset < 0 || length < 0 || offset + length > this.source.Length)");
        writer.Line("var cursor = new " + Cursor + "(this.source, this.options, " + layout + ");");
        writer.Line("global::CStructSharp.Diagnostics.CStructException failure = cursor.Fail(" + Cursor + ".ShortReadText(offset + length, this.source.Length), member, memberType);");
        writer.Line("cursor.Complete(failure);");
        writer.Line("throw failure;");
        writer.Close();
        writer.Line("return this.source.Slice((int)offset, (int)length);");
        writer.Close();

        if (plan.TakeRestName is { } rest)
        {
            writer.Line();
            writer.Line("/// <summary>Checks that the source holds a member, as the checked slice does, and returns everything from its first byte, so a nested view still sees the bytes after it (pointer targets).</summary>");
            writer.Line("/// <param name=\"offset\">The first byte.</param>");
            writer.Line("/// <param name=\"length\">The number of bytes the member needs.</param>");
            writer.Line("/// <param name=\"member\">The member, for the failure.</param>");
            writer.Line("/// <param name=\"memberType\">The member's type, for the failure.</param>");
            writer.Line("/// <returns>The source from <paramref name=\"offset\"/> to its end.</returns>");
            writer.Open("private global::System.ReadOnlySpan<byte> " + rest + "(long offset, long length, string member, string memberType)");
            writer.Line("_ = " + plan.TakeName + "(offset, length, member, memberType);");
            writer.Line("return this.source.Slice((int)offset);");
            writer.Close();
        }
    }

    /// <summary>
    ///     Emits the method that counts the elements of one runtime-sized array with the reader's own checks. The count is
    ///     evaluated and compared with the array limit directly; the cursor that builds the reader's failure is created
    ///     only by a separate method that a failing count calls, so a count that succeeds allocates and copies nothing.
    /// </summary>
    /// <param name="writer">The output.</param>
    /// <param name="member">The array member.</param>
    /// <param name="count">The method name and the translated count expression.</param>
    /// <param name="plan">The view plan, which names the failure method.</param>
    private void EmitCountMethod(SourceWriter writer, GeneratedMember member, (string Method, string Code, bool IsLiteral) count, ViewPlan plan)
    {
        CompiledField field = member.Field;
        string name = SourceWriter.Literal(field.Name);
        string typeName = SourceWriter.Literal(field.DisplayTypeSpelling);
        writer.Line();
        writer.Line("/// <summary>The element count of <c>" + DescribeDeclaration(field) + "</c>, evaluated from the members before it with the reader's limits and failures.</summary>");
        writer.Line("/// <returns>The number of elements.</returns>");
        writer.Open("public int " + count.Method + "()");
        if (count.IsLiteral)
        {
            // A constant cannot fail except against the limit, which the cursor reports.
            writer.Line("var cursor = new " + Cursor + "(this.source, this.options, " + name + ");");
            writer.Open("try");
            writer.Line("int count;");
            EmitCountCode(writer, field.Name, count.Code, true, "array length for " + field.Name, name, typeName, validatedCountIsReturned: true);
            writer.Line("return count;");
            writer.Close();
            writer.Open("catch (global::CStructSharp.Diagnostics.CStructException exception)");
            writer.Line("cursor.Complete(exception);");
            writer.Line("throw;");
            writer.Close();
            writer.Close();
            return;
        }

        string failure = plan.Unique("CountFailureOf" + member.PropertyName);
        if (PlainPropertyCount.IsMatch(count.Code))
        {
            // Reading a view property raises no expression failure (a short source fails with its own exception, which
            // the property read already completes), so no handler is needed and the method has no exception region.
            writer.Line("global::System.Int128 countValue = " + count.Code + ";");
        }
        else
        {
            writer.Line(this.ViewsCarryVariables ? "global::System.Collections.Generic.IReadOnlyDictionary<string, int>? variables = this.variables;" : NoVariables);
            writer.Line("global::System.Int128 countValue;");
            writer.Open("try");
            writer.Line("countValue = " + count.Code + ";");
            writer.Close();
            writer.Open("catch (global::System.Exception expressionFailure)");
            writer.Line("throw " + failure + "(expressionFailure, 0);");
            writer.Close();
        }

        writer.Open("if (countValue < 0 || countValue > " + Cursor + ".ArrayLimit(this.options))");
        writer.Line("throw " + failure + "(null, countValue);");
        writer.Close();
        writer.Line("return (int)countValue;");
        writer.Close();

        writer.Line();
        writer.Line("/// <summary>Builds the failure of the count of <c>" + DescribeDeclaration(field) + "</c> with the reader's text and context: the expression's own failure, a negative count, or a count over the array limit.</summary>");
        writer.Line("/// <param name=\"expressionFailure\">The exception the count expression raised, or <see langword=\"null\"/> when it produced a count.</param>");
        writer.Line("/// <param name=\"countValue\">The count the expression produced.</param>");
        writer.Line("/// <returns>The exception to throw.</returns>");
        writer.Open("private global::System.Exception " + failure + "(global::System.Exception? expressionFailure, global::System.Int128 countValue)");
        writer.Line("var cursor = new " + Cursor + "(this.source, this.options, " + name + ");");
        writer.Open("if (expressionFailure is not null)");
        writer.Line("global::System.Exception raised = cursor.FailExpression(expressionFailure, " + SourceWriter.Literal("array length for " + field.Name) + ", " + name + ", " + typeName + ");");
        writer.Open("if (raised is global::CStructSharp.Diagnostics.CStructException known)");
        writer.Line("cursor.Complete(known);");
        writer.Close();
        writer.Line("return raised;");
        writer.Close();
        writer.Open("if (countValue < 0)");
        writer.Line("global::CStructSharp.Diagnostics.CStructException negative = cursor.Fail(" + SourceWriter.Literal(LayoutFailures.NegativeArrayLength(field.Name)) + ", " + name + ", " + typeName + ");");
        writer.Line("cursor.Complete(negative);");
        writer.Line("return negative;");
        writer.Close();
        writer.Open("try");
        writer.Line("_ = cursor.RequireArrayLength(countValue, " + name + ", " + typeName + ");");
        writer.Close();
        writer.Open("catch (global::CStructSharp.Diagnostics.CStructException limit)");
        writer.Line("cursor.Complete(limit);");
        writer.Line("return limit;");
        writer.Close();
        writer.Line("return new global::System.InvalidOperationException(\"The count is within the array limit.\");");
        writer.Close();
    }
}
