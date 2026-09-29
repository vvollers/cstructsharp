namespace CStructSharp.Tests;

using System.Collections;
using CStructSharp.Values;
using Variant = EngineSweepLayouts.Variant;

/// <summary>
///     Sweeps the writers over the values a caller can hand them, beyond the parsed values the other sweeps write: every
///     sweep layout's value as plain dictionaries and lists (the by-name lookups and element loops), every top-level
///     member missing, and every top-level member replaced by values of the wrong kind, range or shape - through
///     <c>Serialize</c> to an array and into spans of every capacity, under <see cref="ExecutionPath.Fastest"/> and
///     <see cref="ExecutionPath.GeneralOnly"/>, with the engine required wherever the root is eligible.
/// </summary>
/// <remarks>
///     The replacements cover the conversions the codecs apply (numeric text, fractions, out-of-range numbers, booleans,
///     characters), the collection shapes an array accepts or rejects (arrays, lists, byte arrays, too many or too few
///     elements), text given as characters, and values of the wrong kind entirely (dictionaries, <see langword="null"/>).
/// </remarks>
[TestClass]
public class EngineWriteSweepTests
{
    /// <summary>The execution paths every sweep runs under.</summary>
    private static readonly ExecutionPath[] SweepPaths = [ExecutionPath.Fastest, ExecutionPath.GeneralOnly];

    /// <summary>Gets the sweep layout names as data rows.</summary>
    public static IEnumerable<object[]> Layouts => EngineSweepLayouts.Names;

    /// <summary>
    ///     Every value variant - dictionaries, lists, a member missing, a member of the wrong kind, an undeclared member
    ///     under both unknown-member policies - writes identically to a new array and to spans of the output's length
    ///     and of half of it.
    /// </summary>
    /// <param name="name">The sweep layout.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void SuppliedValues_WriteIdentically(string name)
    {
        var reject = new WriteOptions { UnknownMembers = UnknownMemberPolicy.Reject, };
        foreach (Variant variant in EngineSweepLayouts.Both(name))
        {
            IReadOnlyDictionary<string, int>? variables = variant.Source.Variables;
            int length = variant.Data.Length;
            foreach ((string label, object value) in ValueVariants(variant.Value))
            {
                foreach (ExecutionPath path in SweepPaths)
                {
                    Same(variant.Name + " " + label, EngineOperations.Serialize(variant.Layout, "rec", value, variables), path);
                    Same(variant.Name + " " + label, EngineOperations.SerializeToSpan(variant.Layout, length, "rec", value, variables), path);
                    Same(variant.Name + " " + label, EngineOperations.SerializeToSpan(variant.Layout, length / 2, "rec", value, variables), path);
                    Same(variant.Name + " " + label, EngineOperations.Serialize(variant.Layout, "rec", value, variables, reject), path);
                }
            }
        }
    }

    /// <summary>
    ///     A span of every capacity from 0 to one past the output's length receives the same prefix, count and failure, for
    ///     the parsed value and its dictionary form.
    /// </summary>
    /// <param name="name">The sweep layout.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void SpanCapacities_LeaveIdenticalPrefixes(string name)
    {
        foreach (Variant variant in EngineSweepLayouts.Both(name))
        {
            IReadOnlyDictionary<string, int>? variables = variant.Source.Variables;
            object[] values = [variant.Value, ToPlain(variant.Value, lists: false)!];
            foreach (object value in values)
            {
                foreach (ExecutionPath path in SweepPaths)
                {
                    for (int capacity = 0; capacity <= variant.Data.Length + 1; capacity++)
                    {
                        Same(variant.Name + " capacity " + capacity, EngineOperations.SerializeToSpan(variant.Layout, capacity, "rec", value, variables), path);
                    }
                }
            }
        }
    }

    /// <summary>
    ///     The value variants of one parsed value: the value itself, its dictionary form, its dictionary form with every
    ///     collection a list of boxed elements, and per top-level member a copy without it and copies with it replaced.
    /// </summary>
    /// <param name="parsed">The parsed value.</param>
    /// <returns>Labelled values.</returns>
    private static IEnumerable<(string Label, object Value)> ValueVariants(StructValue parsed)
    {
        var plain = (Dictionary<string, object?>)ToPlain(parsed, lists: false)!;
        yield return ("parsed", parsed);
        yield return ("dictionary", plain);
        yield return ("lists", ToPlain(parsed, lists: true)!);
        yield return ("unknown member", new Dictionary<string, object?>(plain) { ["zz"] = 1, });
        foreach (string key in plain.Keys)
        {
            var without = new Dictionary<string, object?>(plain);
            without.Remove(key);
            yield return ("without " + key, without);
            foreach ((string label, object? replacement) in Replacements(plain[key]))
            {
                yield return (key + " = " + label, new Dictionary<string, object?>(plain) { [key] = replacement, });
            }

            if (plain[key] is Dictionary<string, object?> nested && nested.Count > 0)
            {
                // One level down: a nested struct missing its first member, and with it replaced by text.
                string first = nested.Keys.First();
                var inner = new Dictionary<string, object?>(nested);
                inner.Remove(first);
                yield return (key + " without " + first, new Dictionary<string, object?>(plain) { [key] = inner, });
                yield return (key + "." + first + " = text", new Dictionary<string, object?>(plain) { [key] = new Dictionary<string, object?>(nested) { [first] = "x", }, });
            }
        }
    }

    /// <summary>The replacements tried for one member: values of other kinds, ranges and shapes than the parsed one.</summary>
    /// <param name="original">The member's value in dictionary form.</param>
    /// <returns>Labelled replacements.</returns>
    private static IEnumerable<(string Label, object? Value)> Replacements(object? original)
    {
        yield return ("null", null);
        yield return ("numeric text", "7");
        yield return ("text", "x");
        yield return ("empty text", string.Empty);
        yield return ("fraction", 1.5);
        yield return ("minus one", -1);
        yield return ("large", 70000);
        yield return ("int64 max", long.MaxValue);
        yield return ("true", true);
        yield return ("character", 'c');
        yield return ("wide character", 'Ā');
        yield return ("objects", new object?[] { 1, 2, });
        yield return ("int array", new[] { 1, });
        yield return ("bytes", new byte[] { 65, 66, });
        yield return ("dictionary", new Dictionary<string, object?>());
        if (original is IList list)
        {
            // The same elements with one too many and one too few, as a list.
            var elements = list.Cast<object?>().ToList();
            yield return ("one more element", elements.Append(elements.LastOrDefault()).ToList());
            if (elements.Count > 0)
            {
                yield return ("one fewer element", elements.Take(elements.Count - 1).ToList());
            }
        }

        if (original is string text)
        {
            yield return ("longer text", text + "abcdefgh");
            yield return ("characters", text.ToCharArray());
        }
    }

    /// <summary>
    ///     Converts a parsed value to plain data: struct values become string-keyed dictionaries (recursively); with
    ///     <paramref name="lists"/>, every collection becomes a list of its boxed elements, otherwise only collections
    ///     holding struct values are rebuilt. Text, unions, pointers and scalars are kept.
    /// </summary>
    /// <param name="value">The parsed value or member.</param>
    /// <param name="lists">Whether to turn every collection into a list.</param>
    /// <returns>The plain value.</returns>
    private static object? ToPlain(object? value, bool lists)
    {
        switch (value)
        {
        case StructValue structValue:
            {
                var plain = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (KeyValuePair<string, object?> member in structValue)
                {
                    plain[member.Key] = ToPlain(member.Value, lists);
                }

                return plain;
            }

        case string or UnionValue:
            return value;
        case IEnumerable elements when lists || elements.Cast<object?>().Any(element => element is StructValue or IList):
            return elements.Cast<object?>().Select(element => ToPlain(element, lists)).ToList();
        default:
            return value;
        }
    }

    /// <summary>Compares one operation through the harness, naming the value variant in a failure.</summary>
    /// <param name="label">The variant and value the operation writes.</param>
    /// <param name="operation">The operation.</param>
    /// <param name="path">The execution path both sides use.</param>
    private static void Same(string label, DifferentialOperation operation, ExecutionPath path)
    {
        try
        {
            _ = EngineDifferential.AssertSame(operation, path: path);
        }
        catch (AssertFailedException failure)
        {
            throw new AssertFailedException(label + ": " + failure.Message, failure);
        }
    }
}
