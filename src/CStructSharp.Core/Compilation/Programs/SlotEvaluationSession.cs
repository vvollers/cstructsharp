namespace CStructSharp.Compilation.Programs;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using ExpressionOpcode = CStructSharp.Expressions.ExpressionEvaluator.ExpressionOpcode;

/// <summary>
///     The session path of <see cref="ProgramExpression"/>: the dictionary evaluator's
///     <see cref="ExpressionEvaluator.ExpressionEvaluationSession"/> step for step, with names replaced by slots.
/// </summary>
/// <remarks>
///     Every check happens where the dictionary session makes it, with the same message: the root program is charged
///     to the validation work counter, then the prelude is walked depth first through live expressions (a per-walk set
///     of the names on the current path reports cycles; a name already validated at the same or a greater depth is not
///     walked again), then the program runs, charging one work unit per executed instruction, validating a selected
///     conditional name before reading it, evaluating each name once per session and reporting a name that is being
///     evaluated as a cycle. A literal slot is a one-instruction program, as the dictionary evaluator compiles a literal.
///     A reached slot that is an <see cref="SlotState.Identifier"/>, or a live expression whose program is not native,
///     ends the session: the caller then runs the dictionary evaluator from the start.
/// </remarks>
internal sealed class SlotEvaluationSession
{
    private const string CircularDependency = "Circular expression dependency detected at: ";
    private const string DepthExceeded = "Maximum expression evaluation depth exceeded.";
    private const string WorkExceeded = "Maximum expression evaluation work exceeded.";

    private readonly HashSet<int> activeSlots = [];
    private readonly Dictionary<int, Int128> slotResults = [];
    private readonly ExpressionEvaluationLimits limits;
    private readonly SlotTable table;
    private readonly Dictionary<int, int> validatedDepths = [];
    private readonly SlotValue[] values;
    private int executedNodes;
    private int validatedNodes;

    /// <summary>Creates a session over one operation's slots.</summary>
    /// <param name="table">The table the programs were compiled against.</param>
    /// <param name="values">The slot array.</param>
    private SlotEvaluationSession(SlotTable table, SlotValue[] values)
    {
        this.table = table;
        this.values = values;
        this.limits = table.Evaluator.Limits;
    }

    /// <summary>Evaluates a native program in a new session.</summary>
    /// <param name="program">The program.</param>
    /// <param name="values">The slot array.</param>
    /// <param name="result">The value when the session completes.</param>
    /// <returns><see langword="false"/> when the evaluation reaches a slot only the dictionary evaluator can evaluate.</returns>
    public static bool TryEvaluate(ProgramExpression program, SlotValue[] values, out Int128 result)
    {
        var session = new SlotEvaluationSession(program.Table, values);
        try
        {
            session.Validate(program, 0);
            result = session.Execute(program, 0);
            return true;
        }
        catch (DictionaryRequiredException)
        {
            result = Int128.Zero;
            return false;
        }
    }

    /// <summary>
    ///     Returns the program a slot's value runs when selected: <see langword="null"/> for a literal or out-of-domain
    ///     literal (a one-instruction program), the live expression's program, or the end of the session.
    /// </summary>
    /// <param name="value">A slot value that is neither undefined nor unusable.</param>
    /// <returns>The program, or <see langword="null"/> for a leaf.</returns>
    /// <exception cref="DictionaryRequiredException">The slot needs the dictionary evaluator.</exception>
    private static ProgramExpression? DependencyProgram(in SlotValue value)
    {
        if (value.State is SlotState.Literal or SlotState.OutOfDomain)
        {
            return null;
        }

        if (value.State == SlotState.LiveExpression && value.Payload is ProgramExpression { IsNative: true, } program)
        {
            return program;
        }

        throw new DictionaryRequiredException();
    }

    /// <summary>Validates a program's dependency paths without recursion, as the dictionary session's validation walk does.</summary>
    /// <param name="root">The program, or <see langword="null"/> for a leaf (charged as one instruction).</param>
    /// <param name="baseDepth">The number of enclosing expression levels at the program's root.</param>
    private void Validate(ProgramExpression? root, int baseDepth)
    {
        var pathSlots = new HashSet<int>();
        var pending = new Stack<ValidationFrame>();
        this.ChargeValidated(root?.Length ?? 1);
        pending.Push(new ValidationFrame(root, baseDepth, 0, -1));

        while (pending.Count > 0)
        {
            ValidationFrame frame = pending.Pop();
            int[] prelude = frame.Program?.PreludeSlotArray ?? [];
            if (frame.NextReference >= prelude.Length)
            {
                if (frame.EnteredSlot >= 0)
                {
                    pathSlots.Remove(frame.EnteredSlot);
                }

                continue;
            }

            int slot = prelude[frame.NextReference];
            pending.Push(frame with { NextReference = frame.NextReference + 1, });
            int dependencyDepth = frame.BaseDepth + frame.Program!.PreludeDepths[frame.NextReference];
            if (!pathSlots.Add(slot))
            {
                throw new CStructLayoutException(CircularDependency + this.table.GetName(slot));
            }

            SlotValue value = this.values[slot];
            if (value.State == SlotState.Undefined)
            {
                throw ProgramExpression.Undefined(this.table.GetName(slot));
            }

            if (value.State == SlotState.Unusable)
            {
                throw ((UnusableVariable)value.Payload!).CreateFailure(this.table.GetName(slot));
            }

            ProgramExpression? dependency = DependencyProgram(value);
            if (dependencyDepth + (dependency?.MaximumDepth ?? 1) > this.limits.MaximumDepth)
            {
                throw new CStructLayoutException(DepthExceeded);
            }

            if (this.validatedDepths.TryGetValue(slot, out int validatedDepth) && validatedDepth >= dependencyDepth)
            {
                pathSlots.Remove(slot);
                continue;
            }

            this.validatedDepths[slot] = dependencyDepth;
            this.ChargeValidated(dependency?.Length ?? 1);
            pending.Push(new ValidationFrame(dependency, dependencyDepth, 0, slot));
        }
    }

    /// <summary>Adds validation work, failing past the work limit; the counter itself is checked against overflow.</summary>
    /// <param name="instructions">The number of instructions validated.</param>
    private void ChargeValidated(int instructions)
    {
        this.validatedNodes = checked(this.validatedNodes + instructions);
        if (this.validatedNodes > this.limits.MaximumNodes)
        {
            throw new CStructLayoutException(WorkExceeded);
        }
    }

    /// <summary>
    ///     Adds one executed instruction, failing past the work limit. As in the dictionary session, validation has
    ///     already charged every program that runs (each runs at most once per session), so this bound is a backstop
    ///     that keeps a change to either walk from turning into unbounded work.
    /// </summary>
    private void ChargeExecuted()
    {
        this.executedNodes++;
        if (this.executedNodes > this.limits.MaximumNodes)
        {
            throw new CStructLayoutException(WorkExceeded);
        }
    }

    /// <summary>Runs one program, evaluating the names it selects once per session.</summary>
    /// <param name="program">The program.</param>
    /// <param name="dependencyDepth">The number of enclosing expression levels at the program's root.</param>
    /// <returns>The value.</returns>
    private Int128 Execute(ProgramExpression program, int dependencyDepth)
    {
        // Validation has checked this depth already; the dictionary session checks it again here, and so does this one.
        if (dependencyDepth + program.MaximumDepth > this.limits.MaximumDepth)
        {
            throw new CStructLayoutException(DepthExceeded);
        }

        ProgramExpression.SlotInstruction[] code = program.Code;
        Int128[] stack = ArrayPool<Int128>.Shared.Rent(program.MaximumStackSize);
        int count = 0;
        try
        {
            for (int pc = 0; pc < code.Length; pc++)
            {
                ProgramExpression.SlotInstruction instruction = code[pc];
                this.ChargeExecuted();
                switch (instruction.Opcode)
                {
                case ExpressionOpcode.Literal:
                    stack[count++] = instruction.Value;
                    break;
                case ExpressionOpcode.OutOfDomainLiteral:
                    throw new InvalidOperationException(instruction.Text);
                case ExpressionOpcode.JumpIfFalse:
                case ExpressionOpcode.JumpIfTrue:
                    bool truth = stack[count - 1] != Int128.Zero;
                    if (truth == (instruction.Opcode == ExpressionOpcode.JumpIfTrue))
                    {
                        stack[count - 1] = truth ? Int128.One : Int128.Zero;
                        pc = instruction.Target - 1;
                    }

                    break;
                case ExpressionOpcode.BranchIfFalse:
                    if (stack[--count] == Int128.Zero)
                    {
                        pc = instruction.Target - 1;
                    }

                    break;
                case ExpressionOpcode.Jump:
                    pc = instruction.Target - 1;
                    break;
                case ExpressionOpcode.Join:
                    break;
                case ExpressionOpcode.Identifier:
                    int depth = dependencyDepth + instruction.Depth;
                    if (instruction.Conditional && this.values[instruction.Slot].State is not (SlotState.Undefined or SlotState.Unusable))
                    {
                        // A name in a selected arm is validated only now, even when an earlier arm already evaluated it.
                        this.Validate(DependencyProgram(this.values[instruction.Slot]), depth);
                    }

                    stack[count++] = this.EvaluateSlot(instruction.Slot, depth);
                    break;
                case ExpressionOpcode.LogicalNot:
                    stack[count - 1] = ExpressionArithmetic.LogicalNot(stack[count - 1]);
                    break;
                case ExpressionOpcode.Complement:
                    stack[count - 1] = ExpressionArithmetic.Complement(stack[count - 1]);
                    break;
                case ExpressionOpcode.Negate:
                    stack[count - 1] = ExpressionArithmetic.Negate(stack[count - 1]);
                    break;
                default:
                    Int128 right = stack[--count];
                    stack[count - 1] = ExpressionEvaluator.ExpressionEvaluationSession.EvaluateBinary(instruction.Opcode, stack[count - 1], right);
                    break;
                }
            }

            return stack[0];
        }
        finally
        {
            ArrayPool<Int128>.Shared.Return(stack);
        }
    }

    /// <summary>Evaluates one slot once per session, with the dictionary session's checks in its order.</summary>
    /// <param name="slot">The slot.</param>
    /// <param name="dependencyDepth">The expression level the slot's program starts at.</param>
    /// <returns>The value.</returns>
    private Int128 EvaluateSlot(int slot, int dependencyDepth)
    {
        if (this.slotResults.TryGetValue(slot, out Int128 known))
        {
            return known;
        }

        SlotValue value = this.values[slot];
        switch (value.State)
        {
        case SlotState.Undefined:
            throw ProgramExpression.Undefined(this.table.GetName(slot));
        case SlotState.Unusable:
            throw ((UnusableVariable)value.Payload!).CreateFailure(this.table.GetName(slot));
        case SlotState.OutOfDomain:
            // A constant beyond the domain is reported under the name the expression used.
            throw new InvalidOperationException(WideValueVariable.DescribeOutOfRange(this.table.GetName(slot), value.Payload!));
        }

        if (!this.activeSlots.Add(slot))
        {
            throw new CStructLayoutException(CircularDependency + this.table.GetName(slot));
        }

        try
        {
            ProgramExpression? program = DependencyProgram(value);
            Int128 result = program is null ? this.ExecuteLeaf(value.Value, dependencyDepth) : this.Execute(program, dependencyDepth);
            this.slotResults.Add(slot, result);
            return result;
        }
        finally
        {
            this.activeSlots.Remove(slot);
        }
    }

    /// <summary>Runs a literal slot as the one-instruction program the dictionary evaluator compiles for it.</summary>
    /// <param name="value">The literal's value.</param>
    /// <param name="dependencyDepth">The expression level the program starts at.</param>
    /// <returns>The value.</returns>
    private Int128 ExecuteLeaf(Int128 value, int dependencyDepth)
    {
        if (dependencyDepth + 1 > this.limits.MaximumDepth)
        {
            throw new CStructLayoutException(DepthExceeded);
        }

        this.ChargeExecuted();
        return value;
    }

    /// <summary>One step of the iterative validation walk: a program, its base depth, the next prelude entry, and the slot it entered.</summary>
    /// <param name="Program">The program, or <see langword="null"/> for a leaf.</param>
    /// <param name="BaseDepth">The number of enclosing expression levels at the program's root.</param>
    /// <param name="NextReference">The index of the next prelude entry to validate.</param>
    /// <param name="EnteredSlot">The slot whose program this frame walks, removed from the path when it ends; -1 for the root.</param>
    private readonly record struct ValidationFrame(ProgramExpression? Program, int BaseDepth, int NextReference, int EnteredSlot);

    /// <summary>Ends a session that reached a slot only the dictionary evaluator can evaluate; never escapes <see cref="TryEvaluate"/>.</summary>
    [SuppressMessage("Roslynator", "RCS1194:Implement exception constructors", Justification = "A private control-flow signal, created in one place and always caught by TryEvaluate.")]
    private sealed class DictionaryRequiredException : Exception
    {
        /// <summary>Creates the signal.</summary>
        public DictionaryRequiredException()
            : base("The evaluation needs the dictionary evaluator.")
        {
        }
    }
}
