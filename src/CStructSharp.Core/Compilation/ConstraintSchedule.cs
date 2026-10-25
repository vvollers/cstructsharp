namespace CStructSharp.Compilation;

using System.Collections.Generic;
using System.Collections.Immutable;

/// <summary>
///     The order in which a read checks the <c>@expect</c>, <c>@supports</c> and <c>@magic</c> conditions of one struct, as a list
///     of the conditions to check after each member was read. Without a <c>@magic</c> every member's conditions are checked right
///     after the member, in declaration order. A struct with <c>@magic</c> conditions checks them first: the conditions of the
///     members before the last magic member are held back and checked right after the last magic condition, so a value that is
///     not this format is reported by its magic and not by a garbage field before it; on one member the magic conditions come
///     before the others. A magic on a member inside an <c>if</c>/<c>switch</c> arm is not guaranteed to be read, so a struct
///     with one keeps the declaration order.
/// </summary>
internal sealed class ConstraintSchedule
{
    private static readonly ImmutableArray<Entry> NoEntries = ImmutableArray<Entry>.Empty;

    private readonly ImmutableArray<Entry>[] after;

    private ConstraintSchedule(ImmutableArray<Entry>[] after, bool reordersConstraints)
    {
        this.after = after;
        this.ReordersConstraints = reordersConstraints;
    }

    /// <summary>Gets a value indicating whether the schedule differs from the declaration order, which a read that checks conditions by another route (a static plan) must therefore not use.</summary>
    public bool ReordersConstraints { get; }

    /// <summary>Builds the schedule of a struct's members.</summary>
    /// <param name="fields">The struct's members in declaration order.</param>
    /// <returns>The schedule; <see cref="After"/> is indexed like <paramref name="fields"/>.</returns>
    public static ConstraintSchedule Create(IReadOnlyList<CompiledField> fields)
    {
        int lastMagic = -1;
        bool conditionalMagic = false;
        for (int index = 0; index < fields.Count; index++)
        {
            if (HasMagic(fields[index]))
            {
                lastMagic = index;
                conditionalMagic |= fields[index].IsConditional;
            }
        }

        var after = new ImmutableArray<Entry>[fields.Count];
        var deferred = new List<Entry>();
        bool reorders = lastMagic >= 0 && !conditionalMagic;
        for (int index = 0; index < fields.Count; index++)
        {
            ImmutableArray<CompiledConstraint> constraints = fields[index].Constraints;
            if (constraints.IsDefaultOrEmpty && deferred.Count == 0)
            {
                after[index] = NoEntries;
                continue;
            }

            ImmutableArray<Entry>.Builder entries = ImmutableArray.CreateBuilder<Entry>();
            if (!reorders)
            {
                for (int ordinal = 0; ordinal < (constraints.IsDefaultOrEmpty ? 0 : constraints.Length); ordinal++)
                {
                    entries.Add(new Entry(index, ordinal));
                }
            }
            else
            {
                // The magic conditions of the member first; the others wait for the last magic when it comes later and
                // the member is read unconditionally (a held-back check of a skipped member would run for a value never read).
                for (int ordinal = 0; ordinal < (constraints.IsDefaultOrEmpty ? 0 : constraints.Length); ordinal++)
                {
                    if (constraints[ordinal].IsMagic)
                    {
                        entries.Add(new Entry(index, ordinal));
                    }
                }

                if (index == lastMagic)
                {
                    entries.AddRange(deferred);
                    deferred.Clear();
                }

                for (int ordinal = 0; ordinal < (constraints.IsDefaultOrEmpty ? 0 : constraints.Length); ordinal++)
                {
                    if (constraints[ordinal].IsMagic)
                    {
                        continue;
                    }

                    if (index < lastMagic && !fields[index].IsConditional)
                    {
                        deferred.Add(new Entry(index, ordinal));
                    }
                    else
                    {
                        entries.Add(new Entry(index, ordinal));
                    }
                }
            }

            after[index] = entries.ToImmutable();
        }

        return new ConstraintSchedule(after, reorders);
    }

    /// <summary>The conditions to check once a member has been read.</summary>
    /// <param name="field">The member's index in the list the schedule was built from.</param>
    /// <returns>The member and ordinal of each condition, in the order to check them.</returns>
    public ImmutableArray<Entry> After(int field) => this.after[field];

    /// <summary>Whether a member carries a <c>@magic</c> condition.</summary>
    /// <param name="field">The member.</param>
    /// <returns>Whether any of its conditions is a magic.</returns>
    private static bool HasMagic(CompiledField field)
    {
        if (field.Constraints.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (CompiledConstraint constraint in field.Constraints)
        {
            if (constraint.IsMagic)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>One condition to check.</summary>
    /// <param name="Field">The index of the member that declares it.</param>
    /// <param name="Ordinal">Its position among that member's conditions.</param>
    internal readonly record struct Entry(int Field, int Ordinal);
}
