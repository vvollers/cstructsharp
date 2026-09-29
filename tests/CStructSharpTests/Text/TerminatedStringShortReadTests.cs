namespace CStructSharp.Tests;

using System.Text.Json;

/// <summary>
///     Verifies that a terminated string reads the same from a stream that returns fewer bytes per read as from a span:
///     a UTF-16 code unit (and so a UTF-16 terminator) split across two reads is assembled, and every truncation and
///     string limit fails with the span's type, message, path and offset. Parsing, address and length
///     queries, and updates, which all measure the string through the same reader, are compared.
/// </summary>
[TestClass]
public class TerminatedStringShortReadTests
{
    /// <summary>The largest number of bytes a stream returns per read in each comparison.</summary>
    private static readonly int[] ReadSizes = [1, 2, 3, 7];

    /// <summary>Gets every terminated string spelling of the Portable contract as data rows.</summary>
    public static IEnumerable<object[]> Spellings
    {
        get
        {
            using JsonDocument contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "portable-v1.json")));
            return contract.RootElement.GetProperty("terminatedPrimitives").EnumerateArray()
                           .Select(item => new object[] { item.GetProperty("spelling").GetString()!, })
                           .ToArray();
        }
    }

    /// <summary>
    ///     Every prefix of a record holding the string (a surrogate pair included), from empty to complete, reads from
    ///     1-, 2-, 3- and 7-byte streams exactly as from a span, through <c>Parse</c>, <c>ResolveAddress</c> of the field
    ///     after the string, and <c>GetArrayLength</c> of an array the string precedes.
    /// </summary>
    /// <param name="spelling">The terminated string type.</param>
    [TestMethod]
    [DynamicData(nameof(Spellings))]
    public void EveryTruncation_ReadsLikeTheSpan(string spelling)
    {
        (CStruct layout, byte[] data) = Build(spelling);
        var differences = new List<string>();
        for (int length = 0; length <= data.Length; length++)
        {
            byte[] prefix = data[..length];
            Compare(differences, spelling + " length " + length, layout, prefix, new ReadOptions());
        }

        Assert.AreEqual(0, differences.Count, string.Join("\n", differences));
    }

    /// <summary>
    ///     Every string byte limit around the string's size fails or succeeds on a short-read stream exactly as on a
    ///     span, with the same message and offset.
    /// </summary>
    /// <param name="spelling">The terminated string type.</param>
    [TestMethod]
    [DynamicData(nameof(Spellings))]
    public void EveryStringLimit_ReadsLikeTheSpan(string spelling)
    {
        (CStruct layout, byte[] data) = Build(spelling);
        var differences = new List<string>();
        for (int limit = 0; limit <= data.Length + 1; limit++)
        {
            Compare(differences, spelling + " MaxStringBytes " + limit, layout, data, new ReadOptions { MaxStringBytes = limit, });
        }

        Assert.AreEqual(0, differences.Count, string.Join("\n", differences));
    }

    /// <summary>An update after the string finds its target through a short-read stream as through a span.</summary>
    /// <param name="spelling">The terminated string type.</param>
    [TestMethod]
    [DynamicData(nameof(Spellings))]
    public void Update_FindsTheFieldAfterTheString(string spelling)
    {
        (CStruct layout, byte[] data) = Build(spelling);
        byte[] expected = (byte[])data.Clone();
        layout.Update(expected.AsSpan(), "root.tail", (byte)0x42);
        foreach (int readSize in ReadSizes)
        {
            byte[] copy = (byte[])data.Clone();
            using var stream = new ChunkedMemoryStream(copy, readSize, writable: true);
            layout.Update(stream, "root.tail", (byte)0x42);
            CollectionAssert.AreEqual(expected, copy, spelling + " (" + readSize + "-byte reads)");
        }
    }

    /// <summary>The string <c>A😀B</c> in the spelling's encoding followed by the two-element array and the tail.</summary>
    /// <param name="spelling">The terminated string type.</param>
    /// <returns>The layout and a complete record.</returns>
    private static (CStruct Layout, byte[] Data) Build(string spelling)
    {
        var layout = new CStruct("struct root { " + spelling + " value; uint8 n; uint8 d[n]; uint8 tail; };");
        bool ascii = spelling.StartsWith("ascii", StringComparison.Ordinal) || spelling == "cstring";
        var value = new Dictionary<string, object?>
        {
            ["value"] = ascii ? "AB" : "A\U0001F600B",
            ["n"] = (byte)2,
            ["d"] = new byte[] { 7, 8, },
            ["tail"] = (byte)0x7E,
        };
        return (layout, layout.Serialize("root", value));
    }

    /// <summary>Compares each short-read stream's outcomes with the span's and records every difference.</summary>
    /// <param name="differences">The differences found so far.</param>
    /// <param name="label">The case.</param>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="data">The input.</param>
    /// <param name="read">The read options.</param>
    private static void Compare(List<string> differences, string label, CStruct layout, byte[] data, ReadOptions read)
    {
        OperationOutcome[] span =
        [
            OperationOutcome.Of(() => layout.Parse(data.AsSpan(), "root", null, read)),
            OperationOutcome.Of(() => layout.ResolveAddress(data.AsSpan(), "root.tail", null, read)),
            OperationOutcome.Of(() => layout.GetArrayLength(data.AsSpan(), "root.d", null, read)),
        ];
        foreach (int readSize in ReadSizes)
        {
            // Each operation reads its own stream from the start.
            OperationOutcome Run(Func<Stream, object?> operation)
            {
                using var stream = new ChunkedMemoryStream((byte[])data.Clone(), readSize, writable: false);
                return OperationOutcome.Of(() => operation(stream));
            }

            OperationOutcome[] chunked =
            [
                Run(stream => layout.Parse(stream, "root", null, read)),
                Run(stream => layout.ResolveAddress(stream, "root.tail", null, read)),
                Run(stream => layout.GetArrayLength(stream, "root.d", null, read)),
            ];
            string[] names = ["Parse", "ResolveAddress", "GetArrayLength"];
            for (int index = 0; index < span.Length; index++)
            {
                try
                {
                    OperationOutcome.AssertSame(span[index], chunked[index], label + ", " + names[index] + " (" + readSize + "-byte reads)");
                }
                catch (AssertFailedException exception)
                {
                    differences.Add(exception.Message);
                }
            }
        }
    }
}
