namespace CStructSharp.Docs.Examples;

using System.Collections.Generic;
using global::CStructSharp;
using global::CStructSharp.Diagnostics;
using global::CStructSharp.Values;

/// <summary>The executable samples of the record-sequence lesson (<c>docs/guides/generated/sequences-and-try-parse.md</c>).</summary>
internal static partial class Program
{
    #region recipe-record-sequence
    private static async Task RecordSequence()
    {
        // Three headers, one after another, and nothing else: Records reads them lazily, one per step of the loop.
        byte[] bytes = [1, 0, 6, 0, 0, 0, 2, 0, 7, 0, 0, 0, 3, 0, 8, 0, 0, 0];
        var kinds = new List<ushort>();
        foreach (Wire.Header header in Wire.Records(bytes))
        {
            kinds.Add(header.Kind);
        }

        Equal("1,2,3", string.Join(",", kinds));

        // The view enumerator walks the same records without allocating a single object: each step is a view over
        // the next six bytes. The record's size (Sizes.Header) is the stride.
        uint total = 0;
        foreach (Wire.HeaderView view in Wire.HeaderView.Enumerate(bytes))
        {
            total += view.Length;
        }

        Equal(21u, total);

        // The runtime's ParseMany reads the same records as StructValues, from memory or from a stream.
        Equal(3, Wire.Layout.ParseMany(bytes, "header").Count());
        var lengths = new List<uint>();
        await foreach (Wire.Header header in Wire.RecordsAsync(new MemoryStream(bytes)))
        {
            lengths.Add(header.Length);
        }

        Equal("6,7,8", string.Join(",", lengths));

        // Trailing bytes shorter than one record are not ignored: the step that meets them fails, naming the record.
        byte[] trailing = [.. bytes, 9, 9];
        int whole = 0;
        try
        {
            foreach (Wire.Header header in Wire.Records(trailing))
            {
                whole++;
            }

            True(false, "unreachable");
        }
        catch (CStructReadException failure)
        {
            Equal(3, whole);
            True(failure.Message.Contains("remaining 2 bytes are not a whole number of 6-byte elements", StringComparison.Ordinal), failure.Message);
            Equal("[3].header", failure.Path);
        }
    }
    #endregion
}
