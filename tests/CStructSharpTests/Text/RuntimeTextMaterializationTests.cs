namespace CStructSharp.Tests;

using System.Buffers.Binary;
using System.Text;
using CStructSharp.Diagnostics;

/// <summary>Compares owned runtime text with the original character and stream readers at success and failure boundaries.</summary>
[TestClass]
public class RuntimeTextMaterializationTests
{
    /// <summary>Whole wide buffers retain all characters, ownership, byte order and selected-field reads.</summary>
    /// <param name="littleEndian">Whether code units are stored least-significant byte first.</param>
    /// <param name="trim">Whether trailing NUL characters are removed.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void WideText_PreservesOwnedValues(bool littleEndian, bool trim)
    {
        foreach (string text in new[] { string.Empty, "\0\0", "é世界😀\0Z\0", "\uFEFFΩ\0", new string('Ω', 4096) + "\0\0", })
        {
            var layout = WideLayout(littleEndian);
            byte[] input = WideInput(text, littleEndian);
            var options = new ReadOptions { TrimFixedText = trim, MaxStringBytes = 0, };
            string expected = trim ? text.TrimEnd('\0') : text;
            var value = layout.Parse(input, "root", options: options);
            Assert.AreEqual(expected, value.Get<string>("text"));
            Assert.AreEqual((byte)77, value.Get<byte>("tail"));
            Assert.AreEqual(expected, layout.ReadValue<string>(input, "root.text", options: options));
            Assert.AreEqual(Observe(layout, input, options, false), Observe(layout, input, options, true));
            Array.Fill(input, (byte)42);
            Assert.AreEqual(expected, value.Get<string>("text"));
        }
    }

    /// <summary>Truncation, total budgets and invalid UTF-16 keep the original position and complete exception metadata.</summary>
    /// <param name="littleEndian">Whether code units are stored least-significant byte first.</param>
    /// <param name="trim">Whether trailing NUL characters are removed before validation.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(false, true)]
    [DataRow(true, false)]
    [DataRow(true, true)]
    public void WideText_PreservesFailureBoundaries(bool littleEndian, bool trim)
    {
        var layout = WideLayout(littleEndian);
        foreach (string text in new[] { "é😀\0", "A\uD800\0", "\uDC00", "😀A\uD800", "\uD800\uD800\uDC00", })
        {
            byte[] input = WideInput(text, littleEndian);
            for (int length = 0; length <= input.Length; length++)
            {
                for (int budget = 0; budget <= input.Length + 1; budget++)
                {
                    byte[] truncated = input[..length];
                    var options = new ReadOptions { TrimFixedText = trim, MaxTotalBytesRead = budget, };
                    Assert.AreEqual(Observe(layout, truncated, options, false), Observe(layout, truncated, options, true), $"length={length}, budget={budget}");
                }
            }
        }
    }

    /// <summary>Bounded memory text matches the unchanged physical stream decoder, including every budget and short read.</summary>
    /// <param name="type">The bounded text spelling.</param>
    [TestMethod]
    [DataRow("utf8")]
    [DataRow("latin1")]
    [DataRow("cp437")]
    [DataRow("utf16le")]
    [DataRow("utf16be")]
    public void BoundedText_MatchesStreamDecoder(string type)
    {
        var layout = new CStruct($"struct root {{ uint8 tag; uint16< count; {type} text[count]; uint8 tail; }};", aligned: false);
        foreach (byte[] payload in new byte[][] { [], [0, 0,], [0xC3, 0xA9, 0, 0xE4, 0xB8, 0x96,], [65, 0xFF,], [0, 0xD8, 0, 0,], [0xFE, 0xFF, 0, 65,], [65,] })
        {
            byte[] input = [99, (byte)payload.Length, 0, .. payload, 77,];
            foreach (bool trim in new[] { false, true, })
            {
                for (int length = 0; length <= input.Length; length++)
                {
                    for (int budget = 0; budget <= input.Length; budget++)
                    {
                        foreach (int stringLimit in new[] { 0, Math.Max(payload.Length - 1, 0), payload.Length, })
                        {
                            byte[] truncated = input[..length];
                            var options = new ReadOptions { TrimFixedText = trim, MaxTotalBytesRead = budget, MaxStringBytes = stringLimit, };
                            Assert.AreEqual(Observe(layout, truncated, options, false, exposed: false), Observe(layout, truncated, options, true));
                        }
                    }
                }
            }
        }
    }

    /// <summary>Builds a packed dynamic wide field behind an odd-byte header, with its count published by the input.</summary>
    /// <param name="littleEndian">The text code-unit byte order.</param>
    /// <returns>The prepared layout.</returns>
    private static CStruct WideLayout(bool littleEndian)
        => new("typedef wchar" + (littleEndian ? "<" : ">") + " unit; struct root { uint8 tag; uint16< count; unit text[count]; uint8 tail; };", aligned: false);

    /// <summary>Encodes raw code units, including invalid surrogates, without normalizing or validating them.</summary>
    /// <param name="text">The code units stored in the field.</param>
    /// <param name="littleEndian">Their byte order.</param>
    /// <returns>Header, complete field and following marker.</returns>
    private static byte[] WideInput(string text, bool littleEndian)
    {
        byte[] input = new byte[4 + (text.Length * 2)];
        input[0] = 99;
        BinaryPrimitives.WriteUInt16LittleEndian(input.AsSpan(1), (ushort)text.Length);
        for (int index = 0; index < text.Length; index++)
        {
            if (littleEndian)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(input.AsSpan(3 + (index * 2)), text[index]);
            }
            else
            {
                BinaryPrimitives.WriteUInt16BigEndian(input.AsSpan(3 + (index * 2)), text[index]);
            }
        }

        input[^1] = 77;
        return input;
    }

    /// <summary>Records the complete result or failure and final position at a nonzero stream origin.</summary>
    /// <param name="layout">The prepared layout.</param>
    /// <param name="input">The record bytes, possibly truncated.</param>
    /// <param name="options">Read limits and text settings.</param>
    /// <param name="fast">Whether memory shortcuts are enabled.</param>
    /// <param name="exposed">Whether the stream exposes its bytes to the memory cursor.</param>
    /// <returns>A comparable result including encoding-specific inner exception data.</returns>
    private static string Observe(CStruct layout, byte[] input, ReadOptions options, bool fast, bool exposed = true)
    {
        byte[] source = [13, .. input,];
        using var stream = new MemoryStream(source, 0, source.Length, writable: false, publiclyVisible: exposed);
        stream.Position = 1;
        options = options with { ExecutionPath = fast ? ExecutionPath.Fastest : ExecutionPath.NoFastPaths, };
        try
        {
            var value = layout.Parse(stream, "root", options: options);
            return $"{stream.Position}|{value.Get<ushort>("count")}|{value.Get<string>("text")}|{value.Get<byte>("tail")}";
        }
        catch (CStructException exception)
        {
            return $"{stream.Position}|{exception.GetType()}|{exception.Message}|{exception.Offset}|{exception.Path}|{exception.Member}|{exception.MemberType}|{Inner(exception.InnerException)}";
        }
    }

    /// <summary>Includes the encoding failure's exact offending character or bytes and their field-relative index.</summary>
    /// <param name="exception">The inner failure, or null.</param>
    /// <returns>All relevant failure data for comparison.</returns>
    private static string Inner(Exception? exception) => exception switch
    {
        EncoderFallbackException encoder => $"{encoder.GetType()}|{encoder.Message}|{encoder.Index}|{(int)encoder.CharUnknown}|{(int)encoder.CharUnknownHigh}|{(int)encoder.CharUnknownLow}|{encoder.IsUnknownSurrogate()}",
        DecoderFallbackException decoder => $"{decoder.GetType()}|{decoder.Message}|{decoder.Index}|{Convert.ToHexString(decoder.BytesUnknown ?? [])}",
        null => string.Empty,
        _ => $"{exception.GetType()}|{exception.Message}|{Inner(exception.InnerException)}",
    };
}
