namespace CStructSharp.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
///     Pins the block reads and writes inside the general path - a <c>char[n]</c> read and written as one
///     block, and a typed numeric array written as one block - against the element-by-element paths they replace
///     (the internal <see cref="ExecutionPath.GeneralOnly"/> option): the same values, bytes, destination contents, exception types,
///     messages, paths and offsets for every truncation, budget, capacity and invalid value.
/// </summary>
[TestClass]
public class GeneralPathBlockTests
{
    private const string Layout = """
        struct packet {
            uint32 id;
            uint16 count;
            int32 samples[count];
            uint8 name_length;
            char name[name_length];
            char tag[4];
            uint8 kind;
            if (kind == 1) { float64 value; } else { uint32 code; }
            cstring note;
        };
        """;

    /// <summary>Parsing from memory and from streams matches the element-by-element reader for every truncation and limit.</summary>
    [TestMethod]
    public void BlockReads_MatchTheElementReader()
    {
        foreach (bool aligned in new[] { false, true })
        {
            var layout = new CStruct(Layout, aligned: aligned);
            byte[] bytes = Sample(layout);
            var cases = new List<(string Label, byte[] Input, ReadOptions? Options)>();
            for (int length = 0; length <= bytes.Length; length++)
            {
                cases.Add(($"length {length}", bytes[..length], null));
            }

            for (long budget = 1; budget <= bytes.Length; budget += 3)
            {
                cases.Add(($"budget {budget}", bytes, new ReadOptions { MaxTotalBytesRead = budget }));
            }

            cases.Add(("arrays 3", bytes, new ReadOptions { MaxArrayElements = 3 }));
            cases.Add(("strings 4", bytes, new ReadOptions { MaxStringBytes = 4 }));
            cases.Add(("trimmed", bytes, new ReadOptions { TrimFixedText = true }));
            foreach ((string label, byte[] input, ReadOptions? options) in cases)
            {
                string caseLabel = $"aligned={aligned} {label}";
                AssertSameRead(o => OperationOutcome.Render(layout.Parse(input, "packet", options: o)), options, caseLabel + " / span");
                AssertSameRead(o => OperationOutcome.Render(layout.Parse(new MemoryStream(input, writable: false), "packet", options: o)), options, caseLabel + " / stream");
                AssertSameRead(o => OperationOutcome.Render(layout.Parse(new ChunkedMemoryStream(input, 3, writable: false), "packet", options: o)), options, caseLabel + " / chunked");
            }
        }
    }

    /// <summary>Serializing into spans, new arrays and streams matches the element-by-element writer for every value, budget and capacity.</summary>
    [TestMethod]
    public void BlockWrites_MatchTheElementWriter()
    {
        foreach (bool aligned in new[] { false, true })
        {
            var layout = new CStruct(Layout, aligned: aligned);
            StructValue parsed = layout.Parse(Sample(layout), "packet");
            int size = layout.Serialize("packet", parsed).Length;
            var values = new List<(string Label, object Value)>
            {
                ("parsed", parsed),
                ("plain arrays", With(parsed, "samples", new[] { 1, -2, 3 })),
                ("wide character", With(parsed, "name", "grüāp")),
                ("long name", With(parsed, "name", "toolong!")),
                ("short array", With(parsed, "samples", new[] { 1, 2 })),
                ("converted array", With(parsed, "samples", new long[] { 1, 2, 3 })),
                ("covariant array", With(parsed, "samples", (object)new uint[] { 1, 2, uint.MaxValue })),
            };
            var options = new List<(string Label, WriteOptions? Options)> { ("default", null), ("strings 2", new WriteOptions { MaxStringBytes = 2 }) };
            for (long budget = 1; budget <= size; budget += 4)
            {
                options.Add(($"budget {budget}", new WriteOptions { MaxTotalBytesWritten = budget }));
            }

            foreach ((string valueLabel, object value) in values)
            {
                foreach ((string optionLabel, WriteOptions? writeOptions) in options)
                {
                    string label = $"aligned={aligned} {valueLabel} {optionLabel}";
                    AssertSameWrite(o => Convert.ToHexString(layout.Serialize("packet", value, options: o)), writeOptions, label + " / array");
                    foreach (int capacity in new[] { 0, 5, size - 1, size, size + 3 })
                    {
                        byte[] destination = Enumerable.Repeat((byte)0xCC, capacity).ToArray();
                        AssertSameWrite(
                            o =>
                            {
                                // The same starting contents for both runs, then the count and every destination byte.
                                Array.Fill(destination, (byte)0xCC);
                                Exception? failure = null;
                                int written = -1;
                                try
                                {
                                    written = layout.Serialize(destination, "packet", value, options: o);
                                }
                                catch (CStructException exception)
                                {
                                    failure = exception;
                                }

                                return written + ":" + Convert.ToHexString(destination) + ":" + Describe(failure);
                            },
                            writeOptions,
                            $"{label} / span of {capacity}");
                    }

                    AssertSameWrite(
                        o =>
                        {
                            using var stream = new MemoryStream();
                            layout.Write(stream, "packet", value, options: o);
                            return Convert.ToHexString(stream.ToArray());
                        },
                        writeOptions,
                        label + " / stream");
                }
            }
        }
    }

    /// <summary>Encodes the sample packet with the element-by-element writer.</summary>
    private static byte[] Sample(CStruct layout)
    {
        var value = new StructValue
        {
            ["id"] = 77u,
            ["count"] = (ushort)3,
            ["samples"] = new[] { -120, 4, 65_000 },
            ["name_length"] = (byte)5,
            ["name"] = "therm",
            ["tag"] = "ab",
            ["kind"] = (byte)1,
            ["value"] = 21.5,
            ["note"] = "calibrated",
        };
        return layout.Serialize("packet", value, options: new WriteOptions { ExecutionPath = ExecutionPath.GeneralOnly });
    }

    /// <summary>A copy of <paramref name="source"/> with one member replaced.</summary>
    private static StructValue With(StructValue source, string name, object value)
    {
        var copy = new StructValue();
        foreach (KeyValuePair<string, object?> pair in source)
        {
            copy[pair.Key] = pair.Key == name ? value : pair.Value;
        }

        return copy;
    }

    /// <summary>Runs a read with the caller's options and with only the general path, and asserts the same outcome.</summary>
    private static void AssertSameRead(Func<ReadOptions?, string> operation, ReadOptions? options, string label)
    {
        OperationOutcome block = OperationOutcome.Of(() => operation(options));
        OperationOutcome element = OperationOutcome.Of(() => operation((options ?? new ReadOptions()) with { ExecutionPath = ExecutionPath.GeneralOnly }));
        OperationOutcome.AssertSame(element, block, label);
    }

    /// <summary>Runs a write with the caller's options and with only the general path, and asserts the same outcome.</summary>
    private static void AssertSameWrite(Func<WriteOptions?, string> operation, WriteOptions? options, string label)
    {
        OperationOutcome block = OperationOutcome.Of(() => operation(options));
        OperationOutcome element = OperationOutcome.Of(() => operation((options ?? new WriteOptions()) with { ExecutionPath = ExecutionPath.GeneralOnly }));
        OperationOutcome.AssertSame(element, block, label);
    }

    /// <summary>A failure's type, message, path and offset, or <c>none</c>.</summary>
    private static string Describe(Exception? exception)
    {
        return exception is CStructException failure
                   ? failure.GetType().Name + ": " + failure.Message + " | " + failure.Path + " | " + failure.Offset
                   : exception?.GetType().Name ?? "none";
    }
}
