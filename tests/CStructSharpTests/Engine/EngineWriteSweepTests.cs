namespace CStructSharp.Tests;

using System.Collections;
using CStructSharp.Values;
using Variant = EngineSweepLayouts.Variant;

/// <summary>
///     Sweeps the writers over the values a caller can hand them, beyond the parsed values the other sweeps write: every
///     sweep layout's value as plain dictionaries and lists (the by-name lookups and element loops), every top-level
///     member missing, and every top-level member replaced by values of the wrong kind, range or shape - through
///     <c>Serialize</c> to an array and into spans of every capacity and <c>Write</c> into a stream that already holds
///     bytes - and every member a nested path can select, written on its own to every destination with plain and update
///     options, and updated in place, under <see cref="ExecutionPath.Fastest"/> and <see cref="ExecutionPath.NoFastPaths"/>,
///     with the engine required wherever the root or the selected member is eligible.
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
    private static readonly ExecutionPath[] SweepPaths = [ExecutionPath.Fastest, ExecutionPath.NoFastPaths];

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
                    Same(variant.Name + " " + label, EngineOperations.Write(variant.Layout, Prefill(length), 3, "rec", value, variables), path);
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
    ///     Every member a nested path selects - each struct and union member at every depth, the first and last element of
    ///     every array and text (and one past the last), and paths that select no writable member - is written on its own
    ///     from the value the parse holds there, from the whole parsed root (which the write walks down), and from values
    ///     of the wrong kind: to a new array, a span too small for most members, a stream that already holds bytes (at its
    ///     start and inside it), a buffer writer with one-byte windows, and a stream under update options with and without
    ///     union storage kept. Each outcome - bytes, failures, final positions - matches its golden outcome.
    /// </summary>
    /// <param name="name">The sweep layout.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void PathWrites_WriteEveryMemberIdentically(string name)
    {
        var keep = new UpdateOptions { ClearUnionStorage = false, };
        foreach (Variant variant in EngineSweepLayouts.Both(name))
        {
            IReadOnlyDictionary<string, int>? variables = variant.Source.Variables;
            byte[] prefill = Prefill(variant.Data.Length);
            var paths = MemberPaths("rec", variant.Value).ToList();
            paths.Add(("rec.zz", null));
            paths.Add(("rec.zz.y", null));
            foreach ((string path, object? atPath) in paths)
            {
                object?[] values = [atPath, variant.Value, null, "x", 70000];
                foreach (object? value in values)
                {
                    string label = variant.Name + " " + path + " = " + Describe(value);
                    foreach (ExecutionPath execution in SweepPaths)
                    {
                        Same(label, EngineOperations.Serialize(variant.Layout, path, value!, variables), execution);
                        Same(label, EngineOperations.SerializeToSpan(variant.Layout, 3, path, value!, variables), execution);
                        Same(label, EngineOperations.Write(variant.Layout, prefill, 0, path, value!, variables), execution);
                        Same(label, EngineOperations.Write(variant.Layout, prefill, 3, path, value!, variables), execution);
                        Same(label, EngineOperations.SerializeToWindows(variant.Layout, 1, path, value!, variables), execution);
                        Same(label, EngineOperations.Write(variant.Layout, prefill, 3, path, value!, variables, new UpdateOptions()), execution);
                        Same(label, EngineOperations.Write(variant.Layout, prefill, prefill.Length - 2, path, value!, variables, keep), execution);
                    }
                }
            }
        }
    }

    /// <summary>
    ///     Every member a path selects (and the root, pointer targets and addresses, and paths that select nothing) is
    ///     updated in place with the value the parse holds there, with values of other kinds, sizes and lengths, and - for
    ///     text, arrays and unions - with replacements that change a terminated value's length or a conditional selection,
    ///     which the update's layout comparison must accept or reject exactly as the golden outcomes record: in a span, in a span
    ///     one byte short, in a stream, asynchronously, and with union storage kept.
    /// </summary>
    /// <param name="name">The sweep layout.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void PathUpdates_UpdateEveryMemberIdentically(string name)
    {
        var keep = new UpdateOptions { ClearUnionStorage = false, };
        foreach (Variant variant in EngineSweepLayouts.Both(name))
        {
            IReadOnlyDictionary<string, int>? variables = variant.Source.Variables;
            byte[] data = variant.Data;
            var paths = MemberPaths("rec", variant.Value).ToList();
            paths.Add(("rec", variant.Value));
            paths.Add(("rec.zz", null));
            foreach ((string path, object? atPath) in paths)
            {
                foreach (object? value in UpdateValues(atPath, variant.Value))
                {
                    string label = variant.Name + " " + path + " = " + Describe(value);
                    foreach (ExecutionPath execution in SweepPaths)
                    {
                        Same(label, EngineOperations.Update(variant.Layout, data, EngineInput.Span, path, value!, variables), execution);
                        Same(label, EngineOperations.Update(variant.Layout, data, EngineInput.Stream, path, value!, variables), execution);
                        Same(label, EngineOperations.Update(variant.Layout, data[..^1], EngineInput.Span, path, value!, variables), execution);
                        Same(label, EngineOperations.Update(variant.Layout, data, EngineInput.Span, path, value!, variables, keep), execution);
                        Same(label, EngineOperations.UpdateAsync(variant.Layout, data, path, value!, variables), execution);
                    }
                }
            }
        }
    }

    /// <summary>
    ///     The replacements an update sweep tries at one path: the parsed value there, the whole root (which a member path
    ///     rejects), values of the wrong kind, and - for text, integers, arrays and unions - values that keep or change the
    ///     encoded length or the selection: longer and shorter text, other numbers, one element more or fewer, a union's
    ///     other member.
    /// </summary>
    /// <param name="atPath">The parsed value at the path.</param>
    /// <param name="root">The whole parsed root.</param>
    /// <returns>The values.</returns>
    private static IEnumerable<object?> UpdateValues(object? atPath, StructValue root)
    {
        yield return atPath;
        yield return root;
        yield return null;
        yield return "x";
        yield return 0;
        yield return 70000;
        switch (atPath)
        {
        case string text:
            yield return text + "ab";
            yield return text.Length > 0 ? text[..^1] : "abc";
            break;
        case byte or ushort or uint or sbyte or short or int or long or ulong:
            yield return 1;
            yield return 2;
            break;
        case IList list when list.Count > 0:
            var elements = list.Cast<object?>().ToList();
            yield return elements.Append(elements[^1]).ToList();
            yield return elements.Take(elements.Count - 1).ToList();
            break;
        case UnionValue union:
            foreach (KeyValuePair<string, object?> member in union.Members)
            {
                yield return UnionValue.FromMember(union.UnionName, member.Key, member.Value);
            }

            break;
        }
    }

    /// <summary>
    ///     Whole roots written with update options switch on update semantics in both implementations - tail padding keeps
    ///     the stream's bytes, a bitfield unit must already be present, no static plan or block write is used, and a union
    ///     kept by <see cref="UpdateOptions.ClearUnionStorage"/> is staged over the existing bytes - into streams holding
    ///     bytes from several starts (including their end, where nothing exists yet), new arrays, spans and buffer writers.
    /// </summary>
    /// <param name="name">The sweep layout.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void UpdateOptionWrites_WriteIdentically(string name)
    {
        UpdateOptions[] updates = [new UpdateOptions(), new UpdateOptions { ClearUnionStorage = false, }];
        foreach (Variant variant in EngineSweepLayouts.Both(name))
        {
            IReadOnlyDictionary<string, int>? variables = variant.Source.Variables;
            int n = variant.Data.Length;
            byte[] prefill = Prefill(n + 4);
            object[] values = [variant.Value, ToPlain(variant.Value, lists: false)!];
            foreach (UpdateOptions update in updates)
            {
                foreach (object value in values)
                {
                    foreach (ExecutionPath path in SweepPaths)
                    {
                        foreach (long start in (long[])[0, 3, n, n + 4])
                        {
                            Same(variant.Name + " start " + start, EngineOperations.Write(variant.Layout, prefill, start, "rec", value, variables, update), path);
                        }

                        Same(variant.Name, EngineOperations.Serialize(variant.Layout, "rec", value, variables, update), path);
                        Same(variant.Name, EngineOperations.SerializeToSpan(variant.Layout, n, "rec", value, variables, update), path);
                        Same(variant.Name, EngineOperations.SerializeToWindows(variant.Layout, 3, "rec", value, variables, update), path);
                    }
                }
            }
        }
    }

    /// <summary>
    ///     The paths of every member a write can select below <paramref name="prefix"/>, with the parsed value at each: struct
    ///     and union members by name, recursively; for arrays and text the first and last element (recursively) and one
    ///     index past the last, which the write rejects; a pointer's <c>value</c> (and the members of its target) and <c>address</c>,
    ///     which a write cannot select but an update can.
    /// </summary>
    /// <param name="prefix">The path of <paramref name="value"/>.</param>
    /// <param name="value">A parsed value.</param>
    /// <returns>Each path with the parsed value there (the first element's for the index past the last).</returns>
    private static IEnumerable<(string Path, object? Value)> MemberPaths(string prefix, object? value)
    {
        switch (value)
        {
        case StructValue structValue:
            foreach (KeyValuePair<string, object?> member in structValue)
            {
                string path = prefix + "." + member.Key;
                yield return (path, member.Value);
                foreach ((string, object?) inner in MemberPaths(path, member.Value))
                {
                    yield return inner;
                }
            }

            break;

        case UnionValue union:
            foreach (KeyValuePair<string, object?> member in union.Members)
            {
                string path = prefix + "." + member.Key;
                yield return (path, member.Value);
                foreach ((string, object?) inner in MemberPaths(path, member.Value))
                {
                    yield return inner;
                }
            }

            break;

        case Pointer pointer:
            yield return (prefix + ".value", pointer.Value);
            yield return (prefix + ".address", pointer.Address);
            foreach ((string, object?) inner in MemberPaths(prefix + ".value", pointer.Value))
            {
                yield return inner;
            }

            break;

        case string text when text.Length > 0:
            yield return (prefix + "[0]", text[0]);
            yield return (prefix + "[" + text.Length + "]", text[0]);
            break;

        case IList list when list.Count > 0:
            {
                yield return (prefix + "[0]", list[0]);
                foreach ((string, object?) inner in MemberPaths(prefix + "[0]", list[0]))
                {
                    yield return inner;
                }

                if (list.Count > 1)
                {
                    string last = prefix + "[" + (list.Count - 1) + "]";
                    yield return (last, list[^1]);
                    foreach ((string, object?) inner in MemberPaths(last, list[^1]))
                    {
                        yield return inner;
                    }
                }

                yield return (prefix + "[" + list.Count + "]", list[0]);
                break;
            }
        }
    }

    /// <summary>A short description of a written value for failure labels: its type, or <c>null</c>.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The description.</returns>
    private static string Describe(object? value) => value is null ? "null" : value.GetType().Name + " " + value;

    /// <summary>Returns the bytes a destination stream holds before a write: <paramref name="length"/> non-zero bytes, so a byte the write keeps is visible.</summary>
    /// <param name="length">The stream's length in bytes.</param>
    /// <returns>A new array of 0xA5 bytes.</returns>
    private static byte[] Prefill(int length) => Enumerable.Repeat((byte)0xA5, length).ToArray();

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
