namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
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
    {
        CompiledCompositeType composite = this.compilation.SizeQueries.GetCompiledComposite(this.compilation.GetStruct(declaration));
        if (composite.IsUnion || !composite.HasConstraints)
        {
            throw new CStructLayoutException("'" + declaration + "' has no @expect or @magic condition for Matches to check.");
        }

        // A condition is evaluated only when everything it names is known: a layout constant, a caller variable, a member decoded from its
        // build-time offset, or a byte-string comparison on an array at one. Any other member of the struct is undefined.
        var values = new Dictionary<string, Expr>(this.compilation.StaticLayoutVariables, StringComparer.Ordinal);
        if (variables is not null)
        {
            foreach (KeyValuePair<string, int> entry in variables)
            {
                values[entry.Key] = new Literal(entry.Value);
            }
        }

        // The @magic conditions that can be evaluated decide alone; a struct without one (its magic sits in a conditional arm, say)
        // is recognized by its @expect conditions.
        var needed = new HashSet<string>(StringComparer.Ordinal);
        List<CompiledConstraint> evaluated = this.Evaluable(composite, magic: true, needed);
        if (evaluated.Count == 0)
        {
            evaluated = this.Evaluable(composite, magic: false, needed);
        }

        if (evaluated.Count == 0)
        {
            throw new CStructLayoutException("'" + declaration + "' has no condition that Matches can evaluate: its conditions name members that are not numbers or enums at build-time offsets.");
        }

        foreach (CompiledField field in composite.Fields)
        {
            if (needed.Contains(field.Name) && Decodable(field))
            {
                int end = field.FixedOffset!.Value + field.Codec.Size;
                if (source.Length < end)
                {
                    return false;
                }

                if (!TryToInt128(field.Codec.ReadNumeric(source.Slice(field.FixedOffset.Value, field.Codec.Size)), out Int128 number))
                {
                    return false;
                }

                values[field.Name] = new Literal(number);
            }

            foreach (ByteMatch match in field.ByteMatches)
            {
                if (needed.Contains(match.SlotName))
                {
                    int start = field.FixedOffset!.Value;
                    if (source.Length < start + match.Bytes.Length)
                    {
                        return false;
                    }

                    values[match.SlotName] = new Literal(source.Slice(start, match.Bytes.Length).SequenceEqual(match.Bytes) ? 1 : 0);
                }
            }
        }

        try
        {
            foreach (CompiledConstraint condition in evaluated)
            {
                if (this.compilation.ExpressionEvaluator.Evaluate(condition.Expression, values) == 0)
                {
                    return false;
                }
            }
        }
        catch (KeyNotFoundException exception)
        {
            // Every member the conditions name was decoded above, so an unknown name is a variable the caller left out.
            throw new ArgumentException("A condition of Matches names a variable that 'variables' does not supply. " + exception.Message, nameof(variables), exception);
        }
        catch (Exception exception) when (LayoutExpressionEvaluator.IsExpressionFailure(exception))
        {
            return false;
        }

        return true;
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
                if (names.All(name => !composite.FieldsByName.ContainsKey(name) || Decodable(composite.FieldsByName[name]) || HasByteMatch(composite, name)))
                {
                    selected.Add(constraint);
                    needed.UnionWith(names);
                }
            }
        }

        return selected;
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

    /// <summary>Converts a decoded number to the expression domain.</summary>
    /// <param name="value">The boxed value a codec read.</param>
    /// <param name="number">The value as a 128-bit integer.</param>
    /// <returns><see langword="false"/> when the value is not an integer.</returns>
    private static bool TryToInt128(object value, out Int128 number)
    {
        if (value is bool flag)
        {
            number = flag ? 1 : 0;
            return true;
        }

        try
        {
            number = WideIntegerConversion.ToInt128(value);
            return true;
        }
        catch (Exception exception) when (exception is InvalidCastException or OverflowException)
        {
            number = Int128.Zero;
            return false;
        }
    }
}
