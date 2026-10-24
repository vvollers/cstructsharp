namespace CStructSharp.Engine;

using System.Runtime.CompilerServices;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;

/// <summary>The checks a static read plan runs between its operations: none, or the root composite's <c>@expect</c> and <c>@supports</c> conditions.</summary>
internal static partial class ReadEngine
{
    /// <summary>What a static plan runs after each of its operations.</summary>
    private interface IPlanChecks
    {
        /// <summary>Runs after <paramref name="operation"/> stored (and captured) its value.</summary>
        /// <typeparam name="TCursor">The cursor type.</typeparam>
        /// <param name="cursor">The operation's cursor, after the plan's bytes.</param>
        /// <param name="state">The operation's state, whose slots hold every value captured so far.</param>
        /// <param name="operation">The operation just run.</param>
        void After<TCursor>(ref TCursor cursor, ref ReadEngineState state, StaticReadOperation operation)
            where TCursor : struct, IReadCursor;
    }

    /// <summary>No checks: a plan without conditions, and every nested plan. The call compiles away.</summary>
    private readonly struct NoPlanChecks : IPlanChecks
    {
        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void After<TCursor>(ref TCursor cursor, ref ReadEngineState state, StaticReadOperation operation)
            where TCursor : struct, IReadCursor
        {
        }
    }

    /// <summary>
    ///     The conditions of a plan's composite and of the anonymous members flattened into it, checked in field order right
    ///     after each field's operation:
    ///     the member-by-member reader's <see cref="ReadOpCode.CheckConstraint"/> steps, run over the decoded values.
    /// </summary>
    private readonly struct RootPlanChecks : IPlanChecks
    {
        private readonly ReadProgram program;
        private readonly long start;

        /// <summary>Creates the checks of one root read.</summary>
        /// <param name="program">The read program of the composite the plan reads, which holds the compiled conditions.</param>
        /// <param name="start">The absolute position of the composite's first byte.</param>
        public RootPlanChecks(ReadProgram program, long start)
        {
            this.program = program;
            this.start = start;
        }

        /// <inheritdoc/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void After<TCursor>(ref TCursor cursor, ref ReadEngineState state, StaticReadOperation operation)
            where TCursor : struct, IReadCursor
        {
            if (operation.HasConstraints)
            {
                this.Check(ref cursor, ref state, operation);
            }
        }

        /// <summary>
        ///     Evaluates one field's conditions. A failure reports the end of the field, where the member-by-member reader's
        ///     cursor stands when it checks, and names the member as that reader does.
        /// </summary>
        /// <typeparam name="TCursor">The cursor type.</typeparam>
        /// <param name="cursor">The operation's cursor, moved to the field's end only to report a failure.</param>
        /// <param name="state">The operation's state.</param>
        /// <param name="operation">The field's operation.</param>
        /// <exception cref="CStructConstraintException">A condition evaluates to zero.</exception>
        [MethodImpl(MethodImplOptions.NoInlining)]
        private void Check<TCursor>(ref TCursor cursor, ref ReadEngineState state, StaticReadOperation operation)
            where TCursor : struct, IReadCursor
        {
            CompiledField field = operation.Field;
            ReadProgram owner = PlanConditions.Declaring(this.program, operation);
            ReadStep[] steps = owner.ConstraintSteps[operation.MemberIndex]!;
            try
            {
                foreach (ReadStep step in steps)
                {
                    if (state.Slots.Evaluate(owner.Expressions[step.A], owner.ExpressionContexts[step.A], ExpressionFailureDomain.Read) == 0)
                    {
                        CompiledConstraint failed = field.Constraints[step.B];
                        throw new CStructConstraintException(failed.Kind, failed.Text, ReadFailures.ConstraintNotSatisfied(failed.Kind, field.Name, failed.Text));
                    }
                }
            }
            catch (CStructException exception)
            {
                cursor.Position = this.start + operation.Offset + (field.FixedStorageSize ?? 0);
                exception.NoteMember(field.Name, field.DisplayTypeSpelling);
                throw;
            }
        }
    }
}
