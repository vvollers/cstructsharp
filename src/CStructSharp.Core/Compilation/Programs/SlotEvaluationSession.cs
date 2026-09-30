namespace CStructSharp.Compilation.Programs;

using System;
using System.Diagnostics.CodeAnalysis;
using CStructSharp.Expressions;

/// <summary>
///     The session path of <see cref="ProgramExpression"/>: the session rules every layout expression is evaluated by
///     (<see cref="ExpressionSession{TKey, TProgram, TResolver}"/>, which the dictionary evaluator's session also runs) with
///     names replaced by slots, so every check happens where the dictionary session makes it, with the same message.
/// </summary>
/// <remarks>
///     A literal slot is a leaf, which runs as the one-instruction program the dictionary evaluator compiles for a literal.
///     A reached live expression whose program is not native ends the session: the caller then runs the dictionary
///     evaluator from the start, which gives the same result because evaluation has no side effects.
/// </remarks>
internal sealed class SlotEvaluationSession : ExpressionSession<int, ProgramExpression, SlotEvaluationSession.SlotResolver>
{
    /// <summary>Creates a session over one operation's slots.</summary>
    /// <param name="table">The table the programs were compiled against.</param>
    /// <param name="values">The slot array.</param>
    private SlotEvaluationSession(SlotTable table, SlotValue[] values)
        : base(new SlotResolver(table, values))
    {
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
            result = session.EvaluateRoot(program);
            return true;
        }
        catch (DictionaryRequiredException)
        {
            result = Int128.Zero;
            return false;
        }
    }

    /// <summary>Resolves a slot session's names: a slot's program is its live expression's native program; a literal is a leaf.</summary>
    internal readonly struct SlotResolver : IExpressionSessionResolver<int, ProgramExpression>
    {
        private readonly SlotTable table;
        private readonly SlotValue[] values;

        /// <summary>Creates the resolver of one session.</summary>
        /// <param name="table">The table the programs were compiled against.</param>
        /// <param name="values">The slot array.</param>
        public SlotResolver(SlotTable table, SlotValue[] values)
        {
            this.table = table;
            this.values = values;
            this.Limits = table.Evaluator.Limits;
        }

        /// <inheritdoc/>
        public ExpressionEvaluationLimits Limits { get; }

        /// <inheritdoc/>
        public SessionInstruction[] Code(ProgramExpression program) => program.Code;

        /// <inheritdoc/>
        public int MaximumDepth(ProgramExpression program) => program.MaximumDepth;

        /// <inheritdoc/>
        public int MaximumStackSize(ProgramExpression program) => program.MaximumStackSize;

        /// <inheritdoc/>
        public SessionReference[] Prelude(ProgramExpression program) => program.Prelude;

        /// <inheritdoc/>
        public int Key(ProgramExpression program, int reference) => reference;

        /// <inheritdoc/>
        public string Name(int key) => this.table.GetName(key);

        /// <inheritdoc/>
        public bool IsDefined(int key) => this.values[key].State is not (SlotState.Undefined or SlotState.Unusable);

        /// <inheritdoc/>
        public ProgramExpression? Dependency(int key)
        {
            SlotValue value = this.values[key];
            if (value.State == SlotState.Undefined)
            {
                throw ProgramExpression.Undefined(this.table.GetName(key));
            }

            if (value.State == SlotState.Unusable)
            {
                throw ((UnusableVariable)value.Payload!).CreateFailure(this.table.GetName(key));
            }

            return Program(value);
        }

        /// <inheritdoc/>
        public ProgramExpression? Evaluation(int key, out Int128 leaf)
        {
            SlotValue value = this.values[key];
            switch (value.State)
            {
            case SlotState.Undefined:
                throw ProgramExpression.Undefined(this.table.GetName(key));
            case SlotState.Unusable:
                throw ((UnusableVariable)value.Payload!).CreateFailure(this.table.GetName(key));
            case SlotState.OutOfDomain:
                // A constant beyond the domain is reported under the name the expression used.
                throw new InvalidOperationException(WideValueVariable.DescribeOutOfRange(this.table.GetName(key), value.Payload!));
            }

            leaf = value.Value;
            return Program(value);
        }

        /// <summary>
        ///     Returns the program a defined, usable slot runs: <see langword="null"/> for a literal or out-of-domain literal
        ///     (a leaf), the live expression's program, or the end of the session.
        /// </summary>
        /// <param name="value">A slot value that is neither undefined nor unusable.</param>
        /// <returns>The program, or <see langword="null"/> for a leaf.</returns>
        /// <exception cref="DictionaryRequiredException">The slot needs the dictionary evaluator.</exception>
        private static ProgramExpression? Program(in SlotValue value)
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
    }

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
