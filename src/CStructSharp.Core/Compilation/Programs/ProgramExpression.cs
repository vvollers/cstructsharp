namespace CStructSharp.Compilation.Programs;

using System;
using System.Collections.Generic;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Syntax;
using ExpressionOpcode = CStructSharp.Expressions.ExpressionEvaluator.ExpressionOpcode;

/// <summary>
///     A layout expression compiled against a <see cref="SlotTable"/>: the evaluator's own postfix program (same
///     instructions, same checked 128-bit arithmetic, same short-circuit jumps) with every identifier replaced by its
///     slot, plus the <em>validation prelude</em> - the identifiers outside short-circuit and <c>?:</c> arms, in the
///     order the dictionary evaluator validates them before it evaluates anything, so <c>1/0 + missing</c> reports the
///     undefined name as it does there.
/// </summary>
/// <remarks>
///     <para>Evaluation takes the first of three paths that applies; each gives the dictionary evaluator's result or failure:</para>
///     <list type="number">
///         <item>
///             <b>Leaf path</b> (no allocation): the program is <see cref="IsLeafSafe"/> - the depth and work limits
///             cannot be reached when every name it reads is a literal - and every slot it reaches holds a literal, an
///             out-of-domain literal, an unusable value or nothing. The prelude is checked in order (undefined and
///             unusable names fail as validation fails), then the instructions run.
///         </item>
///         <item>
///             <b>Session path</b>: the program reaches a live expression, or is not leaf-safe (near the limits). A
///             session over the slots runs the session core the dictionary evaluator runs - the transitive
///             validation walk with its per-walk cycle set, depth checks and work counter, the lazy validation of
///             selected conditional names, one evaluation per name per session, the evaluation cycle check - so cycles
///             through live expressions and limits are reported identically.
///         </item>
///         <item>
///             <b>Dictionary path</b>: the dictionary evaluator itself, run over <see cref="SlotTable.CreateDictionary"/>.
///             Used when the program is not native - its expression does not compile (a call, a tree over the limits:
///             the compile failure is the result) or names an identifier without a slot - and when an evaluation reaches
///             a live expression whose program is not native. Only the count of a root spelled at run time
///             (<c>uint8[M]</c>), which may name a caller variable no layout expression reads, names an identifier without
///             a slot; the dictionary holds the caller's value of it. Because evaluation has no side effects, restarting on
///             the dictionary path when such a slot is reached gives the dictionary result.
///         </item>
///     </list>
///     <para>The program is immutable and may be evaluated by any number of threads at once.</para>
/// </remarks>
internal sealed class ProgramExpression
{
    private const string UndefinedIdentifier = "Undefined expression identifier: ";

    // One value stack per thread for the leaf path, which never re-enters itself; taken and put back so a nested use
    // (an evaluation from a callback) would only allocate, never share.
    [ThreadStatic]
    private static Int128[]? scratch;

    private readonly SessionInstruction[] code;
    private readonly SessionReference[] prelude;
    private readonly SlotTable table;

    // The program's shape when it is one operand, or two operands and an operator that cannot fail (a comparison or a
    // bitwise and, or, xor): such a program evaluates in a few instructions when its slots hold literals.
    private readonly FastForm fastForm;

    /// <summary>Translates the evaluator's program for <paramref name="source"/>, or records why it cannot.</summary>
    /// <param name="table">The table the identifiers are resolved against.</param>
    /// <param name="source">The expression.</param>
    internal ProgramExpression(SlotTable table, Expr source)
    {
        this.table = table;
        this.Source = source;
        this.code = [];
        this.prelude = [];

        ExpressionEvaluator.CompiledExpression compiled;
        try
        {
            compiled = table.Evaluator.GetProgram(source);
        }
        catch (Exception exception) when (exception is CStructLayoutException or NotSupportedException or InvalidOperationException)
        {
            // The dictionary evaluator compiles again on every evaluation and throws this failure each time.
            this.NotNativeReason = "the expression does not compile: " + exception.Message;
            return;
        }

        // The evaluator's code names each identifier by its index in the program's dependencies; a slot program names
        // the slot instead.
        string[] dependencies = compiled.Dependencies;
        int[] slots = new int[dependencies.Length];
        for (int dependency = 0; dependency < dependencies.Length; dependency++)
        {
            if (!table.TryGetSlot(dependencies[dependency], out slots[dependency]))
            {
                this.NotNativeReason = "the expression names '" + dependencies[dependency] + "', which has no slot";
                return;
            }
        }

        SessionInstruction[] instructions = compiled.Code;
        var code = new SessionInstruction[instructions.Length];
        int identifiers = 0;
        int deepestIdentifier = 0;
        for (int index = 0; index < instructions.Length; index++)
        {
            SessionInstruction instruction = instructions[index];
            if (instruction.Opcode == ExpressionOpcode.Identifier)
            {
                identifiers++;
                deepestIdentifier = Math.Max(deepestIdentifier, instruction.Depth);
                instruction = instruction with { Operand = slots[instruction.Key], };
            }

            code[index] = instruction;
        }

        SessionReference[] references = compiled.Prelude;
        this.prelude = new SessionReference[references.Length];
        for (int index = 0; index < references.Length; index++)
        {
            this.prelude[index] = references[index] with { Key = slots[references[index].Key], };
        }

        this.code = code;
        this.MaximumDepth = compiled.MaximumDepth;
        this.MaximumStackSize = Math.Max(1, compiled.MaximumStackSize);

        // With literal dependencies, validation charges the program and at most one node per identifier occurrence
        // (a literal's own one-instruction program), and execution charges at most every instruction plus one node per
        // distinct name; a literal dependency is one level deeper than the identifier that names it. Within these
        // bounds no limit can fail, so the leaf path need not count.
        ExpressionEvaluationLimits limits = table.Evaluator.Limits;
        this.IsLeafSafe = (long)instructions.Length + identifiers <= limits.MaximumNodes &&
                          deepestIdentifier + 1 <= limits.MaximumDepth;
        this.fastForm = this.IsLeafSafe ? Classify(code) : FastForm.None;
    }

    /// <summary>The shapes a program can take that evaluate without the leaf path's loop and value stack.</summary>
    private enum FastForm : byte
    {
        /// <summary>Any other program.</summary>
        None,

        /// <summary>One literal or identifier.</summary>
        Operand,

        /// <summary>Two literals or identifiers and an operator that cannot fail.</summary>
        Binary,
    }

    /// <summary>Gets the expression the program was compiled from.</summary>
    public Expr Source { get; }

    /// <summary>Gets a value indicating whether the program runs on slots; otherwise it always takes the dictionary path.</summary>
    public bool IsNative => this.NotNativeReason is null;

    /// <summary>Gets why the program is not native, or <see langword="null"/> when it is.</summary>
    public string? NotNativeReason { get; }

    /// <summary>
    ///     Gets a value indicating whether no depth or work limit can fail while every name the program reads holds a
    ///     literal, so such an evaluation may take the leaf path.
    /// </summary>
    public bool IsLeafSafe { get; }

    /// <summary>Gets the number of instructions, which is also the work a session charges for validating the program.</summary>
    internal int Length => this.code.Length;

    /// <summary>Gets the deepest syntax level of any instruction.</summary>
    internal int MaximumDepth { get; }

    /// <summary>Gets the number of values the program's stack must hold.</summary>
    internal int MaximumStackSize { get; }

    /// <summary>Gets the instructions, for the session path.</summary>
    internal SessionInstruction[] Code => this.code;

    /// <summary>Gets the validation prelude: each identifier outside short-circuit and <c>?:</c> arms, as its slot and level.</summary>
    internal SessionReference[] Prelude => this.prelude;

    /// <summary>Gets the table the program was compiled against.</summary>
    internal SlotTable Table => this.table;

    /// <summary>
    ///     Evaluates the expression and reports a failure as <see cref="LayoutExpressionEvaluator.Evaluate"/> does: a
    ///     depth, work or cycle failure passes through as a <see cref="CStructLayoutException"/>, and any other
    ///     expression failure becomes the exception of <paramref name="domain"/> with the message
    ///     <c>Cannot evaluate {context}: {reason}</c> and the failure as its inner exception.
    /// </summary>
    /// <param name="values">The operation's slot array (the table's slots first; the array may be longer).</param>
    /// <param name="unslotted">The operation's caller variables without a slot, or <see langword="null"/>.</param>
    /// <param name="context">What is being evaluated, used in the failure message.</param>
    /// <param name="domain">Whether a failure is reported as a layout, read, or write exception.</param>
    /// <returns>The signed 128-bit value.</returns>
    /// <exception cref="CStructException">The expression cannot be evaluated.</exception>
    public Int128 Evaluate(SlotValue[] values, IReadOnlyDictionary<string, Expr>? unslotted, string context, ExpressionFailureDomain domain)
    {
        if (this.fastForm != FastForm.None && this.TryEvaluateFast(values, out Int128 fast))
        {
            return fast;
        }

        try
        {
            return this.EvaluateUnmapped(values, unslotted);
        }
        catch (CStructLayoutException)
        {
            throw;
        }
        catch (Exception exception) when (LayoutExpressionEvaluator.IsExpressionFailure(exception))
        {
            throw LayoutExpressionEvaluator.CreateFailure(domain, LayoutExpressionEvaluator.DescribeFailure(context, exception), exception);
        }
    }

    /// <summary>
    ///     Evaluates the expression for a consumer that stores an <see cref="int"/>, as
    ///     <see cref="LayoutExpressionEvaluator.EvaluateInt32"/> does: the expression uses the full 128-bit domain and only
    ///     the result must fit.
    /// </summary>
    /// <param name="values">The operation's slot array.</param>
    /// <param name="unslotted">The operation's entries without a slot, or <see langword="null"/>.</param>
    /// <param name="context">What is being evaluated, used in the failure messages.</param>
    /// <param name="domain">Whether a failure is reported as a layout, read, or write exception.</param>
    /// <returns>The value as an <see cref="int"/>.</returns>
    /// <exception cref="CStructException">The expression cannot be evaluated or its value is outside the 32-bit range.</exception>
    public int EvaluateInt32(SlotValue[] values, IReadOnlyDictionary<string, Expr>? unslotted, string context, ExpressionFailureDomain domain)
        => LayoutExpressionEvaluator.RequireInt32(this.Evaluate(values, unslotted, context, domain), context, domain);

    /// <summary>Creates the failure of a name that is not defined, worded as the dictionary evaluator words it.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The exception.</returns>
    internal static KeyNotFoundException Undefined(string name) => new(UndefinedIdentifier + name);

    /// <summary>
    ///     Evaluates the expression with the dictionary evaluator's own failures (<see cref="ExpressionEvaluator.Evaluate"/>):
    ///     <see cref="KeyNotFoundException"/>, <see cref="InvalidOperationException"/>, <see cref="ArithmeticException"/>,
    ///     <see cref="NotSupportedException"/> or <see cref="CStructLayoutException"/>.
    /// </summary>
    /// <param name="values">The operation's slot array.</param>
    /// <param name="unslotted">The operation's entries without a slot, or <see langword="null"/>.</param>
    /// <returns>The signed 128-bit value.</returns>
    internal Int128 EvaluateUnmapped(SlotValue[] values, IReadOnlyDictionary<string, Expr>? unslotted)
    {
        if (this.IsNative)
        {
            if (this.IsLeafSafe && this.TryEvaluateLeaves(values, out Int128 result))
            {
                return result;
            }

            if (SlotEvaluationSession.TryEvaluate(this, values, out result))
            {
                return result;
            }
        }

        return this.table.EvaluateWithDictionary(this.Source, values, unslotted);
    }

    /// <summary>Classifies a program's shape for <see cref="TryEvaluateFast"/>.</summary>
    /// <param name="code">The program's instructions.</param>
    /// <returns>The shape.</returns>
    private static FastForm Classify(SessionInstruction[] code)
    {
        // An operand pushes one value: a literal, or an identifier whose slot is read.
        static bool IsOperand(in SessionInstruction instruction) => instruction.Opcode is ExpressionOpcode.Literal or ExpressionOpcode.Identifier;

        if (code.Length == 1 && IsOperand(code[0]))
        {
            return FastForm.Operand;
        }

        return code.Length == 3 && IsOperand(code[0]) && IsOperand(code[1]) &&
               code[2].Opcode is ExpressionOpcode.Equal or ExpressionOpcode.NotEqual or ExpressionOpcode.Less or ExpressionOpcode.LessOrEqual or
                   ExpressionOpcode.Greater or ExpressionOpcode.GreaterOrEqual or ExpressionOpcode.And or ExpressionOpcode.Or or ExpressionOpcode.Xor
                   ? FastForm.Binary
                   : FastForm.None;
    }

    /// <summary>Reads a literal operand, or an identifier whose slot holds a literal.</summary>
    /// <param name="instruction">A literal or identifier instruction.</param>
    /// <param name="values">The slot array.</param>
    /// <param name="value">The operand.</param>
    /// <returns><see langword="false"/> when the identifier's slot holds anything but a literal.</returns>
    private static bool TryReadLiteral(in SessionInstruction instruction, SlotValue[] values, out Int128 value)
    {
        if (instruction.Opcode == ExpressionOpcode.Literal)
        {
            value = instruction.Value;
            return true;
        }

        ref readonly SlotValue slot = ref values[instruction.Key];
        value = slot.Value;
        return slot.State == SlotState.Literal;
    }

    /// <summary>
    ///     The leaf path: checks the prelude, then runs the instructions while every slot reached holds a literal,
    ///     an out-of-domain literal, an unusable value or nothing.
    /// </summary>
    /// <param name="values">The slot array.</param>
    /// <param name="result">The value when the path completes.</param>
    /// <returns><see langword="false"/> when a live expression or identifier slot is reached; the caller starts over on the session path.</returns>
    private bool TryEvaluateLeaves(SlotValue[] values, out Int128 result)
    {
        result = Int128.Zero;

        // The dictionary session validates these names in this order before running anything; with leaf values only
        // the undefined and unusable checks can fail (no cycles, and the limits are proven out of reach).
        SessionReference[] prelude = this.prelude;
        for (int index = 0; index < prelude.Length; index++)
        {
            int slot = prelude[index].Key;
            switch (values[slot].State)
            {
            case SlotState.Literal:
            case SlotState.OutOfDomain:
                break;
            case SlotState.Undefined:
                throw Undefined(this.table.GetName(slot));
            case SlotState.Unusable:
                throw ((UnusableVariable)values[slot].Payload!).CreateFailure(this.table.GetName(slot));
            default:
                return false;
            }
        }

        SessionInstruction[] instructions = this.code;
        if (instructions.Length == 1)
        {
            return this.TryRead(instructions[0], values, out result);
        }

        Int128[] stack = scratch is { } cached && cached.Length >= this.MaximumStackSize ? cached : new Int128[Math.Max(8, this.MaximumStackSize)];
        scratch = null;
        try
        {
            int count = 0;
            for (int pc = 0; pc < instructions.Length; pc++)
            {
                SessionInstruction instruction = instructions[pc];
                switch (instruction.Opcode)
                {
                case ExpressionOpcode.Literal:
                case ExpressionOpcode.OutOfDomainLiteral:
                case ExpressionOpcode.Identifier:
                    if (!this.TryRead(instruction, values, out stack[count]))
                    {
                        return false;
                    }

                    count++;
                    break;
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
                    stack[count - 1] = ExpressionArithmetic.Binary(instruction.Opcode, stack[count - 1], right);
                    break;
                }
            }

            result = stack[0];
            return true;
        }
        finally
        {
            scratch = stack;
        }
    }

    /// <summary>
    ///     Evaluates a program of one operand, or two and an operator that cannot fail, when every slot it reads holds a
    ///     literal: then the validation prelude cannot fail and no limit can be reached (the program is leaf-safe), so the
    ///     result is the leaf path's. Any other slot state returns <see langword="false"/> and the full paths run.
    /// </summary>
    /// <param name="values">The slot array.</param>
    /// <param name="result">The value when the method returns <see langword="true"/>.</param>
    /// <returns>Whether the program was evaluated.</returns>
    private bool TryEvaluateFast(SlotValue[] values, out Int128 result)
    {
        SessionInstruction[] code = this.code;
        if (!TryReadLiteral(code[0], values, out result))
        {
            return false;
        }

        if (this.fastForm == FastForm.Operand)
        {
            return true;
        }

        if (!TryReadLiteral(code[1], values, out Int128 right))
        {
            return false;
        }

        result = ExpressionArithmetic.Binary(code[2].Opcode, result, right);
        return true;
    }

    /// <summary>
    ///     Pushes one operand on the leaf path: a literal, or a slot's literal; the other leaf states fail as the
    ///     dictionary evaluator's identifier step fails, in its order (undefined, unusable, out of domain).
    /// </summary>
    /// <param name="instruction">A literal, out-of-domain literal or identifier instruction.</param>
    /// <param name="values">The slot array.</param>
    /// <param name="value">The operand.</param>
    /// <returns><see langword="false"/> when the slot holds a live expression or an identifier.</returns>
    private bool TryRead(in SessionInstruction instruction, SlotValue[] values, out Int128 value)
    {
        if (instruction.Opcode == ExpressionOpcode.Literal)
        {
            value = instruction.Value;
            return true;
        }

        if (instruction.Opcode == ExpressionOpcode.OutOfDomainLiteral)
        {
            throw new InvalidOperationException(instruction.Text);
        }

        SlotValue slot = values[instruction.Key];
        switch (slot.State)
        {
        case SlotState.Literal:
            value = slot.Value;
            return true;
        case SlotState.Undefined:
            throw Undefined(this.table.GetName(instruction.Key));
        case SlotState.Unusable:
            throw ((UnusableVariable)slot.Payload!).CreateFailure(this.table.GetName(instruction.Key));
        case SlotState.OutOfDomain:
            throw new InvalidOperationException(WideValueVariable.DescribeOutOfRange(this.table.GetName(instruction.Key), slot.Payload!));
        default:
            value = Int128.Zero;
            return false;
        }
    }
}
