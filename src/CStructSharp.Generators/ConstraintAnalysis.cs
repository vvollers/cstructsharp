namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Introspection;
using CStructSharp.Syntax;

/// <summary>
///     What the analyzer finds in the <c>@expect</c> and <c>@supports</c> conditions of a compiled layout: conditions on one member
///     that no value can satisfy (CSG018) and one label used for different conditions (CSG019). The analysis is conservative: it
///     understands comparisons of the member itself with constants (and the logical operators over them) and says nothing about a
///     condition it cannot reduce to a set of values, so a finding is always certain.
/// </summary>
internal static class ConstraintAnalysis
{
    /// <summary>The kinds of finding.</summary>
    internal enum FindingKind
    {
        /// <summary>The conditions on a member cannot all hold (CSG018).</summary>
        Unsatisfiable,

        /// <summary>One label names conditions that differ (CSG019).</summary>
        LabelReused,
    }

    /// <summary>Runs every check over a compiled layout.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <returns>The findings in declaration order; empty for a layout with no conditions.</returns>
    internal static IReadOnlyList<Finding> Analyze(LayoutCompilation layout)
    {
        var findings = new List<Finding>();
        var labels = new Dictionary<string, List<(string Where, string Text)>>(StringComparer.Ordinal);
        foreach (CompiledCompositeType composite in Composites(layout))
        {
            foreach (CompiledField field in composite.Fields)
            {
                if (field.Constraints.IsDefaultOrEmpty)
                {
                    continue;
                }

                string where = composite.Name + "." + field.Name;
                foreach (CompiledConstraint constraint in field.Constraints)
                {
                    if (constraint.Label is { } label)
                    {
                        if (!labels.TryGetValue(label, out List<(string Where, string Text)>? uses))
                        {
                            labels[label] = uses = new List<(string Where, string Text)>();
                        }

                        uses.Add((where, constraint.Text));
                    }
                }

                if (Unsatisfiable(layout, field) is { } reason)
                {
                    findings.Add(new Finding(FindingKind.Unsatisfiable, where, reason));
                }
            }
        }

        foreach (KeyValuePair<string, List<(string Where, string Text)>> label in labels)
        {
            if (label.Value.Select(use => use.Text).Distinct(StringComparer.Ordinal).Count() > 1)
            {
                string places = string.Join("; ", label.Value.Select(use => use.Where + " (" + use.Text + ")"));
                findings.Add(new Finding(FindingKind.LabelReused, label.Key, places));
            }
        }

        return findings;
    }

    /// <summary>The distinct structs and unions of a layout, in declaration order.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <returns>Their compiled definitions.</returns>
    private static IEnumerable<CompiledCompositeType> Composites(LayoutCompilation layout)
    {
        var seen = new HashSet<CompiledCompositeType>();
        foreach (CompiledTypeSymbol symbol in layout.CompiledModel.Composites.Values)
        {
            if (symbol.Definition is CompiledCompositeType composite && seen.Add(composite))
            {
                yield return composite;
            }
        }
    }

    /// <summary>Why no value of the member can satisfy all of its conditions, or <see langword="null"/> when some can or the analysis cannot tell.</summary>
    /// <param name="layout">The compiled layout, for its constants.</param>
    /// <param name="field">A member with conditions.</param>
    /// <returns>The reason, or <see langword="null"/>.</returns>
    private static string? Unsatisfiable(LayoutCompilation layout, CompiledField field)
    {
        if (field.PointerDepth > 0 || field.Array.Kind != CompiledArrayKind.Scalar || DomainOf(field) is not { } domain)
        {
            return null;
        }

        IntervalSet allowed = IntervalSet.Of(domain.Minimum, domain.Maximum);
        foreach (CompiledConstraint constraint in field.Constraints)
        {
            IntervalSet? set = Satisfying(layout, field.Name, domain, constraint.Expression);
            if (set is null)
            {
                // A condition the analysis cannot reduce leaves the member's values open, so it proves nothing.
                continue;
            }

            if (set.IsEmpty)
            {
                return "'" + constraint.Text + "' holds for no value of " + Describe(domain);
            }

            allowed = allowed.Intersect(set);
            if (allowed.IsEmpty)
            {
                return "no value of " + Describe(domain) + " satisfies '" + string.Join("' and '", field.Constraints.Select(c => c.Text)) + "' together";
            }
        }

        return null;
    }

    /// <summary>The range of the values an integer member can hold, or <see langword="null"/> for any other member.</summary>
    /// <param name="field">The member.</param>
    /// <returns>The inclusive bounds, or <see langword="null"/>.</returns>
    private static (BigInteger Minimum, BigInteger Maximum, int Bits, bool Signed)? DomainOf(CompiledField field)
    {
        (int bytes, bool signed) = field.Codec.Kind switch
        {
            PrimitiveCodecKind.UInt8 => (1, false),
            PrimitiveCodecKind.Int8 => (1, true),
            PrimitiveCodecKind.UInt16 => (2, false),
            PrimitiveCodecKind.Int16 => (2, true),
            PrimitiveCodecKind.UInt24 => (3, false),
            PrimitiveCodecKind.Int24 => (3, true),
            PrimitiveCodecKind.UInt32 => (4, false),
            PrimitiveCodecKind.Int32 => (4, true),
            PrimitiveCodecKind.UInt48 => (6, false),
            PrimitiveCodecKind.Int48 => (6, true),
            PrimitiveCodecKind.UInt64 => (8, false),
            PrimitiveCodecKind.Int64 => (8, true),
            _ => (0, false),
        };
        if (bytes == 0)
        {
            return null;
        }

        int bits = field.BitSize > 0 ? field.BitSize : bytes * 8;
        BigInteger span = BigInteger.One << bits;
        return signed ? (-(span >> 1), (span >> 1) - 1, bits, true) : (BigInteger.Zero, span - 1, bits, false);
    }

    /// <summary>Spells a member's range for a message.</summary>
    /// <param name="domain">The range.</param>
    /// <returns>For example <c>uint8 (0 to 255)</c>.</returns>
    private static string Describe((BigInteger Minimum, BigInteger Maximum, int Bits, bool Signed) domain)
        => (domain.Signed ? "int" : "uint") + domain.Bits + " (" + domain.Minimum + " to " + domain.Maximum + ")";

    /// <summary>The values of the member for which a condition is true, as a set of intervals.</summary>
    /// <param name="layout">The compiled layout, for its constants.</param>
    /// <param name="member">The member's name.</param>
    /// <param name="domain">The member's range.</param>
    /// <param name="expression">The condition.</param>
    /// <returns>The set, or <see langword="null"/> when the condition is not a comparison of the member with constants.</returns>
    private static IntervalSet? Satisfying(LayoutCompilation layout, string member, (BigInteger Minimum, BigInteger Maximum, int Bits, bool Signed) domain, Expr expression)
    {
        IntervalSet full = IntervalSet.Of(domain.Minimum, domain.Maximum);
        switch (expression)
        {
        case BinaryOp { Type: BinaryOperatorType.LogicalAnd } and:
            return Combine(Satisfying(layout, member, domain, and.Left), Satisfying(layout, member, domain, and.Right), (a, b) => a.Intersect(b), emptyWins: true);
        case BinaryOp { Type: BinaryOperatorType.LogicalOr } or:
            {
                // A disjunction with one unknown side is unknown; with both known it is their union.
                IntervalSet? left = Satisfying(layout, member, domain, or.Left);
                IntervalSet? right = Satisfying(layout, member, domain, or.Right);
                return left is null || right is null ? null : left.Union(right);
            }

        case UnaryOp { Type: UnaryOperatorType.LogicalNot } not:
            return Satisfying(layout, member, domain, not.Expr) is { } inner ? full.Subtract(inner) : null;
        case Identifier identifier when identifier.Name == member && !identifier.IsPointer:
            return full.Subtract(IntervalSet.Of(0, 0));
        case BinaryOp comparison when IsComparison(comparison.Type):
            {
                bool memberOnLeft = IsMember(comparison.Left, member);
                if (!memberOnLeft && !IsMember(comparison.Right, member))
                {
                    return null;
                }

                BigInteger? constant = Evaluate(layout, memberOnLeft ? comparison.Right : comparison.Left);
                if (constant is not { } value)
                {
                    return null;
                }

                BinaryOperatorType type = memberOnLeft ? comparison.Type : Mirror(comparison.Type);
                return type switch
                {
                    BinaryOperatorType.Equal => full.Intersect(IntervalSet.Of(value, value)),
                    BinaryOperatorType.NotEqual => full.Subtract(IntervalSet.Of(value, value)),
                    BinaryOperatorType.Less => full.Intersect(IntervalSet.Of(domain.Minimum, value - 1)),
                    BinaryOperatorType.LessOrEqual => full.Intersect(IntervalSet.Of(domain.Minimum, value)),
                    BinaryOperatorType.Greater => full.Intersect(IntervalSet.Of(value + 1, domain.Maximum)),
                    _ => full.Intersect(IntervalSet.Of(value, domain.Maximum)),
                };
            }

        default:
            return null;
        }
    }

    /// <summary>Combines the two sides of a conjunction; a side that is unknown only constrains nothing, but an empty known side makes the whole empty.</summary>
    /// <param name="left">The left set or <see langword="null"/>.</param>
    /// <param name="right">The right set or <see langword="null"/>.</param>
    /// <param name="combine">How two known sets combine.</param>
    /// <param name="emptyWins">Whether an empty known side decides the result.</param>
    /// <returns>The combined set, or <see langword="null"/> when neither side is known.</returns>
    private static IntervalSet? Combine(IntervalSet? left, IntervalSet? right, Func<IntervalSet, IntervalSet, IntervalSet> combine, bool emptyWins)
    {
        if (left is not null && right is not null)
        {
            return combine(left, right);
        }

        IntervalSet? known = left ?? right;
        return known is { IsEmpty: true } && emptyWins ? known : null;
    }

    /// <summary>Whether an operator compares two values.</summary>
    /// <param name="type">The operator.</param>
    /// <returns>Whether it is <c>==</c>, <c>!=</c>, <c>&lt;</c>, <c>&lt;=</c>, <c>&gt;</c> or <c>&gt;=</c>.</returns>
    private static bool IsComparison(BinaryOperatorType type)
        => type is BinaryOperatorType.Equal or BinaryOperatorType.NotEqual or BinaryOperatorType.Less or BinaryOperatorType.LessOrEqual or BinaryOperatorType.Greater or BinaryOperatorType.GreaterOrEqual;

    /// <summary>The comparison that holds when its operands swap places.</summary>
    /// <param name="type">The comparison.</param>
    /// <returns>The mirrored comparison (<c>&lt;</c> becomes <c>&gt;</c>).</returns>
    private static BinaryOperatorType Mirror(BinaryOperatorType type) => type switch
    {
        BinaryOperatorType.Less => BinaryOperatorType.Greater,
        BinaryOperatorType.LessOrEqual => BinaryOperatorType.GreaterOrEqual,
        BinaryOperatorType.Greater => BinaryOperatorType.Less,
        BinaryOperatorType.GreaterOrEqual => BinaryOperatorType.LessOrEqual,
        _ => type,
    };

    /// <summary>Whether an expression is the member itself.</summary>
    /// <param name="expression">The expression.</param>
    /// <param name="member">The member's name.</param>
    /// <returns>Whether it is a plain reference to the member.</returns>
    private static bool IsMember(Expr expression, string member) => expression is Identifier { IsPointer: false } identifier && identifier.Name == member;

    /// <summary>The value of an expression made of literals, integer <c>#define</c>s and arithmetic, or <see langword="null"/> when it is anything else.</summary>
    /// <param name="layout">The compiled layout, for its constants.</param>
    /// <param name="expression">The expression.</param>
    /// <returns>The exact value, or <see langword="null"/>.</returns>
    private static BigInteger? Evaluate(LayoutCompilation layout, Expr expression)
    {
        switch (expression)
        {
        case Literal literal:
            return literal.ExactValue;
        case Identifier identifier when !identifier.IsPointer && layout.Constants.TryGetValue(identifier.Name, out LayoutConstant? constant) && constant is { Kind: LayoutConstantKind.Integer, Value: BigInteger value }:
            return value;
        case UnaryOp { Type: UnaryOperatorType.Neg } negation:
            return -Evaluate(layout, negation.Expr);
        case BinaryOp arithmetic:
            {
                BigInteger? left = Evaluate(layout, arithmetic.Left);
                BigInteger? right = Evaluate(layout, arithmetic.Right);
                if (left is not { } a || right is not { } b)
                {
                    return null;
                }

                return arithmetic.Type switch
                {
                    BinaryOperatorType.Add => a + b,
                    BinaryOperatorType.Minus => a - b,
                    BinaryOperatorType.Mul => a * b,
                    BinaryOperatorType.ShiftLeft when b >= 0 && b < 128 => a << (int)b,
                    BinaryOperatorType.Or => a | b,
                    BinaryOperatorType.And => a & b,
                    _ => null,
                };
            }

        default:
            return null;
        }
    }

    /// <summary>One thing the analysis found.</summary>
    /// <param name="Kind">Which check found it.</param>
    /// <param name="Subject">The member (<c>struct.member</c>) or the label.</param>
    /// <param name="Detail">The reason, for the message.</param>
    internal sealed record Finding(FindingKind Kind, string Subject, string Detail);

    /// <summary>A set of integers as disjoint, sorted, inclusive intervals.</summary>
    private sealed class IntervalSet
    {
        private readonly List<(BigInteger Low, BigInteger High)> intervals;

        private IntervalSet(List<(BigInteger Low, BigInteger High)> intervals) => this.intervals = intervals;

        /// <summary>Gets a value indicating whether the set holds no integer.</summary>
        public bool IsEmpty => this.intervals.Count == 0;

        /// <summary>Creates the set of the integers from <paramref name="low"/> to <paramref name="high"/>.</summary>
        /// <param name="low">The smallest value.</param>
        /// <param name="high">The largest value.</param>
        /// <returns>The set; empty when <paramref name="low"/> exceeds <paramref name="high"/>.</returns>
        public static IntervalSet Of(BigInteger low, BigInteger high)
            => new(low > high ? new List<(BigInteger, BigInteger)>() : new List<(BigInteger, BigInteger)> { (low, high) });

        /// <summary>The integers in both sets.</summary>
        /// <param name="other">The other set.</param>
        /// <returns>The intersection.</returns>
        public IntervalSet Intersect(IntervalSet other)
        {
            var result = new List<(BigInteger, BigInteger)>();
            foreach ((BigInteger low, BigInteger high) in this.intervals)
            {
                foreach ((BigInteger otherLow, BigInteger otherHigh) in other.intervals)
                {
                    BigInteger from = BigInteger.Max(low, otherLow);
                    BigInteger to = BigInteger.Min(high, otherHigh);
                    if (from <= to)
                    {
                        result.Add((from, to));
                    }
                }
            }

            return new IntervalSet(result.OrderBy(interval => interval.Item1).ToList());
        }

        /// <summary>The integers in either set.</summary>
        /// <param name="other">The other set.</param>
        /// <returns>The union.</returns>
        public IntervalSet Union(IntervalSet other)
        {
            var merged = new List<(BigInteger Low, BigInteger High)>();
            foreach ((BigInteger Low, BigInteger High) interval in this.intervals.Concat(other.intervals).OrderBy(interval => interval.Low))
            {
                if (merged.Count > 0 && interval.Low <= merged[^1].High + 1)
                {
                    merged[^1] = (merged[^1].Low, BigInteger.Max(merged[^1].High, interval.High));
                }
                else
                {
                    merged.Add(interval);
                }
            }

            return new IntervalSet(merged);
        }

        /// <summary>The integers of this set that are not in <paramref name="other"/>.</summary>
        /// <param name="other">The integers to remove.</param>
        /// <returns>The difference.</returns>
        public IntervalSet Subtract(IntervalSet other)
        {
            var result = new List<(BigInteger Low, BigInteger High)>(this.intervals);
            foreach ((BigInteger removeLow, BigInteger removeHigh) in other.intervals)
            {
                var next = new List<(BigInteger Low, BigInteger High)>();
                foreach ((BigInteger low, BigInteger high) in result)
                {
                    if (removeHigh < low || removeLow > high)
                    {
                        next.Add((low, high));
                        continue;
                    }

                    if (removeLow > low)
                    {
                        next.Add((low, removeLow - 1));
                    }

                    if (removeHigh < high)
                    {
                        next.Add((removeHigh + 1, high));
                    }
                }

                result = next;
            }

            return new IntervalSet(result);
        }
    }
}
