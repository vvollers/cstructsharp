namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>
///     <see cref="Matches(string, ReadOnlySpan{byte}, IReadOnlyDictionary{string, int}?)"/>: the recognition check of the generated
///     layouts' <c>Matches</c>, for a layout that is compiled at run time.
/// </summary>
public sealed partial class CStruct
{
    /// <summary>
    ///     Tells whether <paramref name="source"/> looks like a <paramref name="declaration"/> without reading it: it decodes only the numbers
    ///     and enums at build-time offsets that the declaration's <c>@expect</c> conditions name (its <c>@magic</c> conditions alone when it has
    ///     any that can be evaluated), builds no value, and answers <see langword="false"/> instead of throwing for a source that is too short or a condition that
    ///     cannot be evaluated. It is the runtime counterpart of the <c>Matches</c> method of a generated layout and has the same limits:
    ///     a condition on a member after a runtime-sized member, inside an array, a conditional arm or a nested struct is not evaluated,
    ///     <c>@supports</c> conditions are not checked (an unsupported value is still recognized), and a match is not a promise that
    ///     a read succeeds. Comparisons with a byte string are evaluated on the array's bytes.
    /// </summary>
    /// <param name="declaration">The case-sensitive name of a struct declaration of the layout.</param>
    /// <param name="source">The bytes; offset 0 is the struct's first byte.</param>
    /// <param name="variables">Optional integer layout variables, which override a layout <c>#define</c> of the same name; the dictionary is not changed.</param>
    /// <returns><see langword="true"/> when every condition that could be evaluated holds.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="declaration"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A condition names a variable that <paramref name="variables"/> does not supply (and the layout does not define); a missing variable is a call error, not a mismatch.</exception>
    /// <exception cref="CStructPathException"><paramref name="declaration"/> is unknown or is not a struct.</exception>
    /// <exception cref="CStructLayoutException">The struct has no condition that <c>Matches</c> can evaluate (the generated layouts have no <c>Matches</c> method then).</exception>
    public bool Matches(string declaration, ReadOnlySpan<byte> source, IReadOnlyDictionary<string, int>? variables = null)
        => this.PrepareMatcher(declaration).Matches(source, variables);

    /// <summary>Prepares recognition once, retaining selected conditions, offsets and typed expression slots.</summary>
    /// <param name="declaration">Case-sensitive struct declaration name.</param>
    /// <returns>The immutable matcher owned by this layout; inspect its rules for recognition limits.</returns>
    /// <exception cref="ArgumentNullException">The declaration is null.</exception>
    /// <exception cref="CStructPathException">The name is not a known struct.</exception>
    /// <exception cref="CStructLayoutException">No condition can be evaluated by recognition.</exception>
    public PreparedMatcher PrepareMatcher(string declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        CompiledCompositeType composite = this.compilation.SizeQueries.GetCompiledComposite(this.compilation.GetStruct(declaration));
        return composite.CachedMatcher ?? composite.PublishMatcher(this.CreateMatcher(declaration));
    }

    /// <summary>Binds the existing recognition selection rules to the shared expression slot evaluator.</summary>
    /// <param name="declaration">Known struct declaration to prepare.</param>
    /// <returns>Complete immutable recognition decisions, including excluded rules.</returns>
    private PreparedMatcher CreateMatcher(string declaration)
    {
        CompiledCompositeType composite = this.compilation.SizeQueries.GetCompiledComposite(this.compilation.GetStruct(declaration));
        var needed = new HashSet<string>(StringComparer.Ordinal);
        List<CompiledConstraint> selected = this.Evaluable(composite, magic: true, needed);
        if (selected.Count == 0)
        {
            selected = this.Evaluable(composite, magic: false, needed);
        }

        if (composite.IsUnion || selected.Count == 0)
        {
            throw new CStructLayoutException("'" + declaration + "' has no condition that Matches can evaluate: its conditions name members that are not numbers or enums at build-time offsets.");
        }

        SlotTable table = this.compilation.SlotTable;
        var initial = new SlotValue[table.Count];
        foreach (KeyValuePair<string, Expr> constant in this.compilation.StaticLayoutVariables)
        {
            if (table.TryGetSlot(constant.Key, out int slot))
            {
                initial[slot] = table.ToSlotValue(constant.Value);
            }
        }

        var numbers = new List<PreparedMatcher.Numeric>();
        var comparisons = new List<PreparedMatcher.Bytes>();
        var rules = new List<MatchRule>();
        foreach (CompiledField field in composite.Fields)
        {
            if (needed.Contains(field.Name) && Decodable(field))
            {
                _ = table.TryGetSlot(field.Name, out int slot);
                numbers.Add(new PreparedMatcher.Numeric(slot, field.FixedOffset!.Value, field.Codec));
            }

            foreach (ByteMatch comparison in field.ByteMatches)
            {
                if (needed.Contains(comparison.SlotName))
                {
                    _ = table.TryGetSlot(comparison.SlotName, out int slot);
                    comparisons.Add(new PreparedMatcher.Bytes(slot, field.FixedOffset!.Value, comparison.Bytes));
                }
            }

            foreach (CompiledConstraint constraint in field.Constraints)
            {
                bool check = selected.Contains(constraint);
                rules.Add(new MatchRule(field.Name, constraint.Keyword, constraint.Label, check, check ? "Selected for recognition." : constraint.Keyword == "supports" ? "Supports rules are excluded from recognition." : "Excluded by magic preference or unsupported field placement."));
            }
        }

        // Compile programs during preparation, including short-circuit and missing-variable semantics.
        return new PreparedMatcher(table, initial, numbers.ToArray(), comparisons.ToArray(), selected.Select(condition => table.Compile(condition.Expression)).ToArray(), rules.AsReadOnly());
    }

    /// <summary>Selects the conditions of one kind that <c>Matches</c> can evaluate and records the names they use.</summary>
    /// <param name="composite">The struct.</param>
    /// <param name="magic"><see langword="true"/> for the <c>@magic</c> conditions, <see langword="false"/> for the <c>@expect</c> ones.</param>
    /// <param name="needed">Receives the names the selected conditions read.</param>
    /// <returns>The conditions whose members are all numbers or enums at build-time offsets, byte comparisons, layout constants or caller variables.</returns>
    private List<CompiledConstraint> Evaluable(CompiledCompositeType composite, bool magic, HashSet<string> needed)
    {
        var selected = new List<CompiledConstraint>();
        foreach (CompiledField field in composite.Fields)
        {
            foreach (CompiledConstraint constraint in field.Constraints)
            {
                if (magic ? !constraint.IsMagic : constraint.Kind != ConstraintKind.Malformed)
                {
                    continue;
                }

                IReadOnlyCollection<string> names = this.compilation.ExpressionEvaluator.GetDependencies(constraint.Expression);

                // Hidden byte-comparison slots are fields too: an unavailable dynamic offset must
                // not be mistaken for a caller variable just because its slot name is synthetic.
                if (names.All(name => CanEvaluateDependency(composite, name)))
                {
                    selected.Add(constraint);
                    needed.UnionWith(names);
                }
            }
        }

        return selected;
    }

    /// <summary>Separates true caller names from hidden byte comparisons whose source placement must be fixed.</summary>
    /// <param name="composite">Recognized struct.</param>
    /// <param name="name">Expression dependency.</param>
    /// <returns>Whether recognition can supply this dependency, or it is a caller/layout variable.</returns>
    private static bool CanEvaluateDependency(CompiledCompositeType composite, string name)
    {
        if (composite.FieldsByName.TryGetValue(name, out CompiledField? field))
        {
            return Decodable(field) || HasByteMatch(composite, name);
        }

        foreach (CompiledField member in composite.Fields)
        {
            foreach (ByteMatch comparison in member.ByteMatches)
            {
                if (comparison.SlotName == name)
                {
                    return member.FixedOffset is not null && !member.IsConditional;
                }
            }
        }

        return true;
    }

    /// <summary>Whether a member is a number or enum at a build-time offset, which <see cref="Matches(string, ReadOnlySpan{byte}, IReadOnlyDictionary{string, int}?)"/> can decode.</summary>
    /// <param name="field">The member.</param>
    /// <returns><see langword="true"/> for a scalar integer, bool or enum member without bit size, pointer or conditional placement.</returns>
    private static bool Decodable(CompiledField field)
    {
        return field.FixedOffset is not null && field.Array.Kind == CompiledArrayKind.Scalar && field.PointerDepth == 0 && field.BitSize == 0 && !field.IsConditional
               && field.Composite is null && field.Codec.IsFixedWidthNumeric && field.Codec.Kind is not (PrimitiveCodecKind.Float32 or PrimitiveCodecKind.Float64);
    }

    /// <summary>Whether a hidden byte-comparison name belongs to an array of the composite that sits at a build-time offset.</summary>
    /// <param name="composite">The composite.</param>
    /// <param name="slotName">A name a condition reads.</param>
    /// <returns><see langword="true"/> when some member's <see cref="ByteMatch"/> publishes the name and the member has a fixed offset.</returns>
    private static bool HasByteMatch(CompiledCompositeType composite, string slotName)
    {
        return composite.Fields.Any(field => field.FixedOffset is not null && !field.IsConditional && field.ByteMatches.Any(match => match.SlotName == slotName));
    }
}
