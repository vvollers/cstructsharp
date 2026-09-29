namespace CStructSharp.Tests;

using System.Globalization;
using System.Text;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Syntax;

/// <summary>
///     Renders <see cref="ReadProgram"/>s as readable text for golden tests: one line per step with its index, operation,
///     member and decoded operands, followed by each nested program once. Operands are shown by meaning (a codec's kind
///     and byte order, a slot's name, an expression's source, a jump's target) rather than by table index, so a golden
///     dump changes only when what the program does changes.
/// </summary>
internal static class ReadProgramDump
{
    /// <summary>Renders a program and every program it reaches, each once, in first-reached order.</summary>
    /// <param name="program">The program.</param>
    /// <param name="table">The slot table the program indexes, which names the slots.</param>
    /// <returns>The text, with LF line endings and a final newline.</returns>
    public static string Render(ReadProgram program, SlotTable table)
    {
        var text = new StringBuilder();
        var pending = new Queue<ReadProgram>();
        var seen = new HashSet<ReadProgram>(ReferenceEqualityComparer.Instance);
        pending.Enqueue(program);
        seen.Add(program);
        while (pending.Count > 0)
        {
            ReadProgram current = pending.Dequeue();
            if (text.Length > 0)
            {
                text.Append('\n');
            }

            RenderOne(text, current, table);
            foreach (ReadProgram nested in current.Nested)
            {
                if (seen.Add(nested))
                {
                    pending.Enqueue(nested);
                }
            }
        }

        return text.ToString();
    }

    /// <summary>Renders the program of a root of a layout.</summary>
    /// <param name="layout">The layout.</param>
    /// <param name="root">The root name.</param>
    /// <returns>The text; fails the test when the root has no program.</returns>
    public static string RenderRoot(CStruct layout, string root)
    {
        ReadProgramOutcome outcome = layout.Compilation.GetRootReadProgram(root);
        Assert.IsNotNull(outcome.Program, outcome.Reason);
        return Render(outcome.Program, layout.Compilation.SlotTable);
    }

    /// <summary>
    ///     Renders one program's steps compactly, without its header or nested programs: <c>Op member operands</c> per
    ///     step, for assertions about a few steps.
    /// </summary>
    /// <param name="program">The program.</param>
    /// <param name="table">The slot table the program indexes.</param>
    /// <returns>One line per step.</returns>
    public static string[] Lines(ReadProgram program, SlotTable table)
        => program.Steps.Select(step => (step.Op + " " + Member(program, step) + " " + Operands(program, step, table)).TrimEnd()).ToArray();

    /// <summary>Renders one program's header and steps.</summary>
    /// <param name="text">The output.</param>
    /// <param name="program">The program.</param>
    /// <param name="table">The slot table.</param>
    private static void RenderOne(StringBuilder text, ReadProgram program, SlotTable table)
    {
        string kind = program.Kind switch
        {
            ReadProgramKind.Composite => "struct",
            ReadProgramKind.Promoted => "promoted struct",
            ReadProgramKind.Union => "union",
            _ => "root",
        };
        text.Append(kind).Append(' ').Append(program.Name.Length > 0 ? program.Name : "(anonymous)").Append('\n');
        for (int index = 0; index < program.Steps.Length; index++)
        {
            ReadStep step = program.Steps[index];
            string line = string.Format(CultureInfo.InvariantCulture, "  {0,3}  {1,-28} {2,-12} {3}", index, step.Op, Member(program, step), Operands(program, step, table));
            text.Append(line.TrimEnd()).Append('\n');
        }
    }

    /// <summary>Names the member a step belongs to: its name, <c>(anonymous)</c> for a promoted struct, <c>(unnamed)</c> for padding, or <c>-</c> for a step outside any member.</summary>
    /// <param name="program">The program.</param>
    /// <param name="step">The step.</param>
    /// <returns>The name.</returns>
    private static string Member(ReadProgram program, ReadStep step)
        => step.Field < 0 ? "-"
           : program.Fields[step.Field].Name is { Length: > 0 } name ? name
           : program.Fields[step.Field].IsPromotedComposite ? "(anonymous)"
           : "(unnamed)";

    /// <summary>Describes a step's operands by their meaning.</summary>
    /// <param name="program">The program.</param>
    /// <param name="step">The step.</param>
    /// <param name="table">The slot table.</param>
    /// <returns>The description.</returns>
    private static string Operands(ReadProgram program, ReadStep step, SlotTable table)
    {
        switch (step.Op)
        {
        case ReadOpCode.Seek:
            return "+" + step.A;
        case ReadOpCode.Align:
            return "to " + step.A;
        case ReadOpCode.CheckOffset:
            return "@" + step.A;
        case ReadOpCode.CheckFixedCount:
            return "count " + step.A;
        case ReadOpCode.EvaluateCount:
            return "count = " + Expression(program.Expressions[step.A].Source);
        case ReadOpCode.CountToEnd:
        case ReadOpCode.CountTerminated:
            return "element size " + step.A;
        case ReadOpCode.SkipTerminator:
            return "+" + step.A;
        case ReadOpCode.ReshapeTable:
            return "dimensions " + string.Join("x", program.Fields[step.Field].Array.Dimensions.Select(dimension => dimension.FixedCount));
        case ReadOpCode.ReadEnum:
        case ReadOpCode.ReadEnumArray:
            return Codec(program.Codecs[step.A]) + " as " + program.Enums[step.B].Name;
        case ReadOpCode.ReadStruct:
        case ReadOpCode.ReadUnion:
            return (program.Nested[step.A].Name is { Length: > 0 } named ? named : "(anonymous)") + (step.B >= 0 ? ", prefix " + program.Prefixes[step.B] : string.Empty);
        case ReadOpCode.PlaceMember:
        case ReadOpCode.PlaceBitfield:
        case ReadOpCode.PlaceSeparator:
        case ReadOpCode.CompletePlacement:
        case ReadOpCode.OpenBitfieldUnit:
        case ReadOpCode.RewindToUnionStart:
        case ReadOpCode.RestoreUnionSlots:
            return string.Empty;
        case ReadOpCode.ReadPointer:
        case ReadOpCode.ReadPointerArray:
            return (step.B == 1 ? "deferred " : "in place ") + program.PointerTargets[step.A].Kind;
        case ReadOpCode.FollowPendingPointers:
            return string.Empty;
        case ReadOpCode.FinishPlaced:
            return "tail to " + step.B;
        case ReadOpCode.ReadPromotedStruct:
        case ReadOpCode.ReadStructArray:
        case ReadOpCode.ReadStructElements:
        case ReadOpCode.ReadPromotedUnion:
        case ReadOpCode.ReadUnionArray:
        case ReadOpCode.ReadRootUnion:
        case ReadOpCode.ReadRootStruct:
            return program.Nested[step.A].Name is { Length: > 0 } nested ? nested : "(anonymous)";
        case ReadOpCode.CaptureInteger:
        case ReadOpCode.CaptureUInt128:
        case ReadOpCode.CaptureEnum:
            return "-> " + table.GetName(step.A);
        case ReadOpCode.CaptureNotANumber:
        case ReadOpCode.CaptureNotANumberIfElements:
            return "-> " + table.GetName(step.A) + " (" + program.Unusables[step.B] + ")";
        case ReadOpCode.PublishQualified:
            return table.GetName(step.A) + " -> " + string.Join(", ", program.QualifiedTargets[step.B].Select(target => target.Prefix + "*=" + table.GetName(target.Slot)));
        case ReadOpCode.EnterConditionalScope:
            return "clear " + string.Join(", ", program.Scope!.ClearedSlots.Select(table.GetName));
        case ReadOpCode.SelectArm:
            {
                ReadProgram.ConditionalBranch branch = program.Branches[step.A];
                ReadProgram.ConditionalGroup group = program.Groups[branch.Group];
                return $"group {branch.Group} ({Expression(program.Expressions[group.Selector].Source)}) arm {branch.Arm}, else -> {step.B}";
            }

        case ReadOpCode.CompleteMember:
            {
                ReadConditionalScope scope = program.Scope!;

                // Names the slots that scope locals mirror.
                string Names(IReadOnlyList<int> locals) => string.Join(", ", locals.Select(local => table.GetName(scope.LocalSlots[local])));
                return "save [" + Names(scope.GetCaptured(step.Field)) + "] restore [" + Names(scope.GetRestored(step.Field)) + "]";
            }

        case ReadOpCode.FinishComposite:
            return step.A >= 0 ? "tail +" + step.A : "tail to " + step.B;
        case ReadOpCode.EvaluateDefinition:
            return Expression(program.Expressions[step.A].Source) + (step.B >= 0 ? " -> " + table.GetName(step.B) : string.Empty);
        default:
            return Codec(program.Codecs[step.A]);
        }
    }

    /// <summary>Spells an expression in C-like syntax, fully parenthesized.</summary>
    /// <param name="expression">The expression.</param>
    /// <returns>The text, such as <c>(kind == 1)</c>.</returns>
    private static string Expression(Expr expression) => expression switch
    {
        Identifier identifier => identifier.Name,
        Literal literal => literal.ExactValue.ToString(CultureInfo.InvariantCulture),
        UnaryOp unary => (unary.Type == UnaryOperatorType.LogicalNot ? "!" : unary.Type == UnaryOperatorType.Neg ? "-" : "~") + Expression(unary.Expr),
        BinaryOp binary => "(" + Expression(binary.Left) + " " + Operator(binary.Type) + " " + Expression(binary.Right) + ")",
        ConditionalExpr conditional => "(" + Expression(conditional.Condition) + " ? " + Expression(conditional.WhenTrue) + " : " + Expression(conditional.WhenFalse) + ")",
        _ => expression.ToString() ?? string.Empty,
    };

    /// <summary>Spells a binary operator.</summary>
    /// <param name="type">The operator.</param>
    /// <returns>Its C spelling.</returns>
    private static string Operator(BinaryOperatorType type) => type switch
    {
        BinaryOperatorType.LogicalAnd => "&&",
        BinaryOperatorType.LogicalOr => "||",
        BinaryOperatorType.Equal => "==",
        BinaryOperatorType.NotEqual => "!=",
        BinaryOperatorType.Less => "<",
        BinaryOperatorType.LessOrEqual => "<=",
        BinaryOperatorType.Greater => ">",
        BinaryOperatorType.GreaterOrEqual => ">=",
        BinaryOperatorType.Add => "+",
        BinaryOperatorType.Minus => "-",
        BinaryOperatorType.Mul => "*",
        BinaryOperatorType.Div => "/",
        BinaryOperatorType.And => "&",
        BinaryOperatorType.Or => "|",
        BinaryOperatorType.ShiftRight => ">>",
        BinaryOperatorType.ShiftLeft => "<<",
        BinaryOperatorType.Mod => "%",
        _ => "^",
    };

    /// <summary>Describes a codec by kind and, for multi-byte built-in kinds, byte order (a caller's codec decides its own).</summary>
    /// <param name="codec">The codec.</param>
    /// <returns>The description, such as <c>UInt32 le</c>.</returns>
    private static string Codec(ReadProgram.Codec codec)
    {
        PrimitiveCodec primitive = codec.Primitive;
        return primitive.Size > 1 && !primitive.IsCustom ? primitive.Kind + (primitive.LittleEndian ? " le" : " be") : primitive.Kind.ToString();
    }
}
