namespace CStructSharp.Tests;

using System.Collections;
using System.Text;
using CStructSharp.Values;

/// <summary>
///     Sweeps the buffered input forms (issue #52) against the span form: for each layout, input and pointer-target limit,
///     every total read budget from zero to past the input's length gives the same value or the same failure from a
///     multi-segment sequence, an async read of a stream that cannot seek (unknown length) and an async read of a seekable
///     stream that hides its buffer (known length). The cases are the reads that move or look past bytes without charging
///     them - alignment padding, pointer targets, <c>T v[EOF]</c> counts and terminator scans - at and around the input's end.
/// </summary>
[TestClass]
public class BufferedFormSweepTests
{
    /// <summary>Every buffered form reports exactly the span form's outcome at every budget.</summary>
    /// <returns>A task that completes after every combination is checked.</returns>
    [TestMethod]
    public async Task EveryForm_MatchesTheSpanAtEveryBudget()
    {
        var mismatches = new StringBuilder();
        long?[] targetLimits = [null, 0, 1, 2, 4, long.MaxValue];
        foreach ((string name, CStruct layout, string root, byte[] bytes) in Cases())
        {
            foreach (long? targetLimit in targetLimits)
            {
                for (int budget = 0; budget <= bytes.Length + 3; budget++)
                {
                    var options = new ReadOptions { MaxTotalBytesRead = budget, MaxPointerTargetBytes = targetLimit, };
                    string span = Outcome(() => layout.Parse(bytes, root, options: options));
                    var forms = new (string Form, string Outcome)[]
                    {
                        ("sequence of 3-byte segments", Outcome(() => layout.Parse(ChunkedSequence.Of(bytes), root, options: options))),
                        ("sequence of 1-byte segments", Outcome(() => layout.Parse(ChunkedSequence.Of(bytes, 1), root, options: options))),
                        ("stream that cannot seek", await OutcomeAsync(async () => await layout.ParseAsync(new AsyncStreamBufferTests.NonSeekableStream(bytes), root, options: options))),
                        ("seekable hidden stream", await OutcomeAsync(async () => await layout.ParseAsync(new MemoryStream(bytes, writable: false), root, options: options))),
                    };

                    foreach ((string form, string outcome) in forms)
                    {
                        if (outcome != span)
                        {
                            mismatches.AppendLine($"{name}, pointer-target limit {targetLimit?.ToString() ?? "none"}, budget {budget}, {form}: span gives {span}; form gives {outcome}");
                        }
                    }
                }
            }
        }

        Assert.AreEqual(string.Empty, mismatches.ToString());
    }

    /// <summary>Lists the swept layouts and inputs; each input's bytes end where the comment on its group says.</summary>
    /// <returns>The case name, layout, root struct and input bytes.</returns>
    private static IEnumerable<(string Name, CStruct Layout, string Root, byte[] Bytes)> Cases()
    {
        // Seven bytes of uncharged padding before x; the input holds the value, ends at t, or ends inside x.
        var aligned = new CStruct("struct rec { uint8 tag; uint64 x; uint8 t; };", aligned: true);
        yield return ("aligned", aligned, "rec", [7, .. new byte[7], 1, 2, 3, 4, 5, 6, 7, 8, 9, .. new byte[7]]);
        yield return ("aligned, ends before t", aligned, "rec", [7, .. new byte[7], 1, 2, 3, 4, 5, 6, 7, 8]);
        yield return ("aligned, ends inside x", aligned, "rec", [7, .. new byte[7], 1, 2, 3]);

        // A 16-bit target far past the budget, exactly at the end, straddling the end, past it, at 2^48, at the largest
        // addresses, and null.
        var far = new CStruct("struct far { uint8 a; uint16* p; uint8 z; };", pointerSize: 8);
        yield return ("far target", far, "far", [1, 40, 0, 0, 0, 0, 0, 0, 0, 5, .. new byte[30], 0x2A, 0x2B, 0xCC]);
        yield return ("target at the end", far, "far", [1, 12, 0, 0, 0, 0, 0, 0, 0, 5, 0xAA, 0xBB]);
        yield return ("target across the end", far, "far", [1, 11, 0, 0, 0, 0, 0, 0, 0, 5, 0xAA, 0xBB]);
        yield return ("target past the end", far, "far", [1, 200, 0, 0, 0, 0, 0, 0, 0, 5, 0xAA, 0xBB]);
        yield return ("target at 2^48", far, "far", [1, 0, 0, 0, 0, 0, 0, 1, 0, 5, 0xAA, 0xBB]);
        yield return ("target at the largest address", far, "far", [1, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F, 5, 0xAA, 0xBB]);
        yield return ("target one before the largest address", far, "far", [1, 0xFE, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0x7F, 5, 0xAA, 0xBB]);
        yield return ("null target", far, "far", [1, 0, 0, 0, 0, 0, 0, 0, 0, 5, 0xAA, 0xBB]);

        // An array of pointers whose targets lie past the budget.
        var pointers = new CStruct("struct fa { uint8 n; uint32* p[n]; };", pointerSize: 4);
        yield return ("pointer array", pointers, "fa", [2, 20, 0, 0, 0, 24, 0, 0, 0, .. new byte[11], 1, 0, 0, 0, 2, 0, 0, 0]);

        // A read-to-end array after padding: whole elements, a partial last element, and padding that ends the input.
        var eof = new CStruct("struct tail { uint8 a; uint32 v[EOF]; };", aligned: true);
        yield return ("read-to-end array", eof, "tail", [1, 0xEE, 0xEE, 0xEE, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0, 0, 0]);
        yield return ("read-to-end array, partial element", eof, "tail", [1, 0xEE, 0xEE, 0xEE, 1, 0, 0, 0, 2, 0, 0, 0, 3, 0]);
        yield return ("read-to-end array, ends in padding", eof, "tail", [1, 0xEE, 0xEE]);

        // A terminated string: terminated with bytes after it, unterminated, and terminated at the input's end.
        var text = new CStruct("struct s { uint8 a; cstring name; uint8 b; };");
        yield return ("string", text, "s", [1, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o', 0, 9, 8]);
        yield return ("string, unterminated", text, "s", [1, (byte)'h', (byte)'e', (byte)'l', (byte)'l', (byte)'o']);
        yield return ("string, terminator ends the input", text, "s", [1, (byte)'h', 0]);

        // A terminated string as a far pointer target, terminated and unterminated.
        var pointedText = new CStruct("struct ps { uint8 a; cstring* p; };", pointerSize: 4);
        yield return ("string target", pointedText, "ps", [1, 20, 0, 0, 0, .. new byte[15], (byte)'x', (byte)'y', 0, 0xCC]);
        yield return ("string target, unterminated", pointedText, "ps", [1, 20, 0, 0, 0, .. new byte[15], (byte)'x', (byte)'y']);

        // An aligned zero-terminated array, terminated and unterminated.
        var terminated = new CStruct("struct w { uint8 a; uint16 v[]; uint8 t; };", aligned: true);
        yield return ("terminated array", terminated, "w", [7, 0xEE, 1, 0, 2, 0, 0, 0, 9, 0xCC, 0xCC]);
        yield return ("terminated array, unterminated", terminated, "w", [7, 0xEE, 1, 0, 2, 0, 3, 0]);
    }

    /// <summary>Renders the outcome of a read: its value, or its failure's type and message.</summary>
    /// <param name="read">The read.</param>
    /// <returns>The rendering.</returns>
    private static string Outcome(Func<object?> read)
    {
        try
        {
            return Render(read());
        }
        catch (Exception exception)
        {
            return exception.GetType().Name + ": " + exception.Message;
        }
    }

    /// <summary>Renders the outcome of an asynchronous read: its value, or its failure's type and message.</summary>
    /// <param name="read">The read.</param>
    /// <returns>The rendering.</returns>
    private static async Task<string> OutcomeAsync(Func<Task<object?>> read)
    {
        try
        {
            return Render(await read());
        }
        catch (Exception exception)
        {
            return exception.GetType().Name + ": " + exception.Message;
        }
    }

    /// <summary>Renders a decoded value, descending into structs and collections so every element is compared.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The rendering.</returns>
    private static string Render(object? value) => value switch
    {
        null => "null",
        string text => "\"" + text + "\"",
        IEnumerable<KeyValuePair<string, object?>> members => "{" + string.Join(",", members.Select(pair => pair.Key + "=" + Render(pair.Value))) + "}",
        IEnumerable items => "[" + string.Join(",", items.Cast<object?>().Select(Render)) + "]",
        _ => value.ToString() ?? "?",
    };
}
