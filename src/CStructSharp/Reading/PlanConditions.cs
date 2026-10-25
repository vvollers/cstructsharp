namespace CStructSharp.Reading;

using CStructSharp.Compilation.Programs;

/// <summary>
///     Finds the compiled <c>@expect</c> and <c>@supports</c> checks a <see cref="StaticReadPlan"/> has to run. The plan
///     flattens anonymous members into the struct, but the conditions are compiled into the read programs of the
///     composites that declare them: this class walks from the root program to those programs.
/// </summary>
internal static class PlanConditions
{
    private const int Undecided = 0;
    private const int Covered = 1;
    private const int Uncovered = 2;

    /// <summary>Returns the program of the composite that declares the operation's field.</summary>
    /// <param name="root">The program of the plan's root composite (or of the nested composite the operation belongs to).</param>
    /// <param name="operation">An operation of that plan.</param>
    /// <returns>The program that holds the field's check steps, found through the anonymous members the operation was flattened out of.</returns>
    public static ReadProgram Declaring(ReadProgram root, StaticReadOperation operation)
    {
        ReadProgram program = root;
        foreach (int holder in operation.HolderPath)
        {
            program = program.NestedByField[holder]!;
        }

        return program;
    }

    /// <summary>
    ///     Whether the program has a check step for every condition of the plan, so that running the plan with checks reads
    ///     exactly what the member-by-member reader reads. The answer is computed once per program.
    /// </summary>
    /// <param name="program">The program of the plan's root composite.</param>
    /// <param name="plan">The composite's plan, which carries conditions.</param>
    /// <returns><see langword="true"/> when every condition is reachable.</returns>
    public static bool Covers(ReadProgram program, StaticReadPlan plan)
    {
        int verdict = program.PlanChecksVerdict;
        if (verdict == Undecided)
        {
            verdict = Reaches(program, plan) ? Covered : Uncovered;
            program.PlanChecksVerdict = verdict;
        }

        return verdict == Covered;
    }

    /// <summary>Walks the plan's operations, comparing the programs they map to with the conditions they declare.</summary>
    /// <param name="program">The program of the composite the plan reads.</param>
    /// <param name="plan">The plan.</param>
    /// <returns>Whether every reachable program covers its conditions and no nested struct carries one.</returns>
    private static bool Reaches(ReadProgram program, StaticReadPlan plan)
    {
        // A struct with @magic conditions checks them in the order of its ConstraintSchedule, which the plan's per-operation checks do
        // not follow, so it keeps the member-by-member reader.
        if (!program.CoversConstraints || program.Composite is { HasMagic: true } or { HasByteMatches: true })
        {
            return false;
        }

        foreach (StaticReadOperation operation in plan.Operations)
        {
            // An anonymous member a debug program reads another way has no entry; such a program keeps the member-by-member path.
            ReadProgram? declaring = program;
            foreach (int holder in operation.HolderPath)
            {
                declaring = declaring.NestedByField[holder];
                if (declaring is null || !declaring.CoversConstraints || declaring.Composite is { HasMagic: true } or { HasByteMatches: true })
                {
                    return false;
                }
            }

            // A struct nested in the plan is read by its own plan without checks, so its conditions keep the struct member by member.
            if (operation.NestedPlan is { HasConstraints: true })
            {
                return false;
            }
        }

        return true;
    }
}
