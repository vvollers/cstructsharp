namespace CStructSharp.Expressions;

using System;
using System.Buffers;
using System.Collections.Generic;
using CStructSharp.Diagnostics;
using ExpressionOpcode = CStructSharp.Expressions.ExpressionEvaluator.ExpressionOpcode;

/// <summary>
///     One bounded evaluation session: the rules every layout expression is evaluated by, over names the resolver
///     supplies. The dictionary evaluator's session resolves names through a variable dictionary
///     (<see cref="ExpressionEvaluator.ExpressionEvaluationSession"/>); a slot program's session resolves slots through an
///     operation's slot array (<c>SlotEvaluationSession</c>).
/// </summary>
/// <remarks>
///     <para>
///         A root is first validated, then run. Validation charges the root to the validation work counter and walks the
///         prelude depth first through the programs the names hold: a per-walk set of the names on the current path
///         reports a cycle, a name validated before at the same or a greater depth is not walked again, and the depth of
///         every dependency path is checked against the limit.
///     </para>
///     <para>
///         Running charges one work unit per executed instruction, validates a selected conditional name before reading it
///         (even when an earlier arm evaluated it), evaluates each name once per session and reports a name that is being
///         evaluated as a cycle. The two counters and the evaluated names persist across the roots of one session.
///     </para>
///     <para>Owned by one evaluation on one thread.</para>
/// </remarks>
/// <typeparam name="TKey">What identifies a name: its text, or its slot.</typeparam>
/// <typeparam name="TProgram">The program type.</typeparam>
/// <typeparam name="TResolver">The resolver, a struct so its calls bind statically.</typeparam>
internal class ExpressionSession<TKey, TProgram, TResolver>
    where TKey : notnull
    where TProgram : class
    where TResolver : struct, IExpressionSessionResolver<TKey, TProgram>
{
    private const string CircularDependency = "Circular expression dependency detected at: ";
    private const string DepthExceeded = "Maximum expression evaluation depth exceeded.";
    private const string WorkExceeded = "Maximum expression evaluation work exceeded.";

    private readonly HashSet<TKey> activeKeys = [];
    private readonly Dictionary<TKey, Int128> results = [];
    private readonly Dictionary<TKey, int> validatedDepths = [];
    private readonly ExpressionEvaluationLimits limits;
    private TResolver resolver;
    private int executedNodes;
    private int validatedNodes;

    /// <summary>Creates a session over one resolver.</summary>
    /// <param name="resolver">What the names hold.</param>
    protected ExpressionSession(TResolver resolver)
    {
        this.resolver = resolver;
        this.limits = resolver.Limits;
    }

    /// <summary>Validates and runs one root, keeping the session's evaluated names and work counters.</summary>
    /// <param name="program">The root's program.</param>
    /// <returns>The signed 128-bit value.</returns>
    /// <exception cref="CStructLayoutException">A cycle, or the depth or work limit, is reached.</exception>
    /// <exception cref="KeyNotFoundException">A name the root reaches is undefined.</exception>
    /// <exception cref="InvalidOperationException">A selected value or operation is outside the expression domain.</exception>
    /// <exception cref="OverflowException">An arithmetic result does not fit the signed 128-bit domain.</exception>
    /// <exception cref="DivideByZeroException">A selected division or remainder has a zero divisor.</exception>
    protected Int128 EvaluateRoot(TProgram program)
    {
        this.Validate(program, 0);
        return this.Execute(program, 0);
    }

    /// <summary>Validates a program's dependency paths without recursion.</summary>
    /// <param name="root">The program, or <see langword="null"/> for a leaf (charged as one instruction).</param>
    /// <param name="baseDepth">The number of enclosing expression levels at the program's root.</param>
    private void Validate(TProgram? root, int baseDepth)
    {
        var pathKeys = new HashSet<TKey>();
        var pending = new Stack<ValidationFrame>();
        this.ChargeValidated(root is null ? 1 : this.resolver.Code(root).Length);
        pending.Push(new ValidationFrame(root, baseDepth, 0, default, false));

        while (pending.Count > 0)
        {
            ValidationFrame frame = pending.Pop();
            SessionReference[] prelude = frame.Program is null ? [] : this.resolver.Prelude(frame.Program);
            if (frame.NextReference >= prelude.Length)
            {
                if (frame.Entered)
                {
                    pathKeys.Remove(frame.EnteredKey!);
                }

                continue;
            }

            SessionReference reference = prelude[frame.NextReference];
            TKey key = this.resolver.Key(frame.Program!, reference.Key);
            pending.Push(frame with { NextReference = frame.NextReference + 1, });
            int dependencyDepth = frame.BaseDepth + reference.Depth;
            if (!pathKeys.Add(key))
            {
                throw new CStructLayoutException(CircularDependency + this.resolver.Name(key));
            }

            TProgram? dependency = this.resolver.Dependency(key);
            if (dependencyDepth + (dependency is null ? 1 : this.resolver.MaximumDepth(dependency)) > this.limits.MaximumDepth)
            {
                throw new CStructLayoutException(DepthExceeded);
            }

            if (this.validatedDepths.TryGetValue(key, out int validatedDepth) && validatedDepth >= dependencyDepth)
            {
                pathKeys.Remove(key);
                continue;
            }

            this.validatedDepths[key] = dependencyDepth;
            this.ChargeValidated(dependency is null ? 1 : this.resolver.Code(dependency).Length);
            pending.Push(new ValidationFrame(dependency, dependencyDepth, 0, key, true));
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
    ///     Adds one executed instruction, failing past the work limit. Validation has already charged every program that
    ///     runs (each runs at most once per session), so this bound is a backstop that keeps a change to either walk from
    ///     turning into unbounded work.
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
    private Int128 Execute(TProgram program, int dependencyDepth)
    {
        // Validation has checked this depth already; running checks it again, as a backstop like the work counter.
        if (dependencyDepth + this.resolver.MaximumDepth(program) > this.limits.MaximumDepth)
        {
            throw new CStructLayoutException(DepthExceeded);
        }

        SessionInstruction[] code = this.resolver.Code(program);
        Int128[] stack = ArrayPool<Int128>.Shared.Rent(this.resolver.MaximumStackSize(program));
        int count = 0;
        try
        {
            for (int pc = 0; pc < code.Length; pc++)
            {
                SessionInstruction instruction = code[pc];
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
                    TKey key = this.resolver.Key(program, instruction.Key);
                    int depth = dependencyDepth + instruction.Depth;
                    if (instruction.Conditional && this.resolver.IsDefined(key))
                    {
                        // A name in a selected arm is validated only now, even when an earlier arm already evaluated it.
                        this.Validate(this.resolver.Dependency(key), depth);
                    }

                    stack[count++] = this.EvaluateKey(key, depth);
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

            if (count != 1)
            {
                throw new InvalidOperationException("Compiled expression did not produce exactly one value.");
            }

            return stack[0];
        }
        finally
        {
            ArrayPool<Int128>.Shared.Return(stack);
        }
    }

    /// <summary>Evaluates one name once per session, failing as the resolver orders it and then on a cycle.</summary>
    /// <param name="key">The name.</param>
    /// <param name="dependencyDepth">The expression level the name's program starts at.</param>
    /// <returns>The value.</returns>
    private Int128 EvaluateKey(TKey key, int dependencyDepth)
    {
        if (this.results.TryGetValue(key, out Int128 known))
        {
            return known;
        }

        TProgram? program = this.resolver.Evaluation(key, out Int128 leaf);
        if (!this.activeKeys.Add(key))
        {
            throw new CStructLayoutException(CircularDependency + this.resolver.Name(key));
        }

        try
        {
            Int128 result = program is null ? this.ExecuteLeaf(leaf, dependencyDepth) : this.Execute(program, dependencyDepth);
            this.results.Add(key, result);
            return result;
        }
        finally
        {
            this.activeKeys.Remove(key);
        }
    }

    /// <summary>Runs a literal leaf as the one-instruction program the compiler produces for a literal.</summary>
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

    /// <summary>One step of the iterative validation walk: a program, its base depth, the next prelude entry, and the name it entered.</summary>
    /// <param name="Program">The program, or <see langword="null"/> for a leaf.</param>
    /// <param name="BaseDepth">The number of enclosing expression levels at the program's root.</param>
    /// <param name="NextReference">The index of the next prelude entry to validate.</param>
    /// <param name="EnteredKey">The name whose program this frame walks, removed from the path when it ends.</param>
    /// <param name="Entered">Whether the frame entered a name (every frame but the root's).</param>
    private readonly record struct ValidationFrame(TProgram? Program, int BaseDepth, int NextReference, TKey? EnteredKey, bool Entered);
}
