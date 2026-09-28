namespace CStructSharp.Comparison;

using System.Reflection;
using System.Text.Json;
using BenchmarkDotNet.Attributes;
using CStructSharp.Comparison.FlatBuffers;
using CStructSharp.Comparison.Model;
using FlatSharp;
using MemoryPack;
using MessagePack;

/// <summary>
///     Runs every benchmark method once, outside BenchmarkDotNet, and checks that it does the work the tables claim:
///     each deserializer reports the sample record's fingerprint, each "same bytes" serializer writes exactly the
///     hand-written 79 bytes, and each own-format serializer writes bytes its own library reads back to the same
///     record. A benchmark method without a check here fails verification, so a new case cannot skip it.
/// </summary>
public static class Verification
{
    /// <summary>Runs the checks, prints one line per case, and optionally records the encoded sizes.</summary>
    /// <param name="sizesPath">Where to write the encoded size of each format as JSON, or <see langword="null" />.</param>
    /// <returns>0 when every check passes; 1 otherwise.</returns>
    public static int Run(string? sizesPath)
    {
        var failures = new List<string>();
        var payloads = new Payloads();
        ulong expected = Fingerprints.Of(SampleRecord.CreateDto());

        CheckDeserializers(expected, failures);
        CheckSerializers(payloads, expected, failures);
        CheckVariable(failures);

        if (sizesPath is not null)
        {
            var sizes = new SortedDictionary<string, int>(StringComparer.Ordinal)
            {
                ["Layout"] = payloads.Layout.Length,
                ["MemoryPack"] = payloads.MemoryPack.Length,
                ["MessagePack"] = payloads.MessagePack.Length,
                ["Protobuf"] = payloads.Protobuf.Length,
                ["FlatBuffers"] = payloads.FlatBuffers.Length,
                ["Json"] = payloads.Json.Length,
            };
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(sizesPath))!);
            File.WriteAllText(sizesPath, JsonSerializer.Serialize(sizes, new JsonSerializerOptions { WriteIndented = true }));
        }

        foreach (string failure in failures)
        {
            Console.Error.WriteLine($"FAIL {failure}");
        }

        Console.WriteLine(failures.Count == 0 ? "All comparison cases verified." : $"{failures.Count} comparison case(s) failed verification.");
        return failures.Count == 0 ? 0 : 1;
    }

    /// <summary>Checks that every deserialize case reports the sample record's fingerprint.</summary>
    /// <param name="expected">The fingerprint of the sample record.</param>
    /// <param name="failures">Receives one message per failed check.</param>
    private static void CheckDeserializers(ulong expected, List<string> failures)
    {
        var benchmarks = new DeserializeBenchmarks();
        benchmarks.Setup();
        var cases = new Dictionary<string, Func<ulong>>(StringComparer.Ordinal)
        {
            [nameof(benchmarks.CStructSharp_GeneratedView)] = benchmarks.CStructSharp_GeneratedView,
            [nameof(benchmarks.CStructSharp_GeneratedParse)] = benchmarks.CStructSharp_GeneratedParse,
            [nameof(benchmarks.CStructSharp_RuntimeReadValue)] = benchmarks.CStructSharp_RuntimeReadValue,
            [nameof(benchmarks.CStructSharp_RuntimeParse)] = benchmarks.CStructSharp_RuntimeParse,
            [nameof(benchmarks.CStructSharp_RuntimeView)] = benchmarks.CStructSharp_RuntimeView,
            [nameof(benchmarks.CStructSharp_RuntimeParseAccessors)] = benchmarks.CStructSharp_RuntimeParseAccessors,
            [nameof(benchmarks.HandWritten_BinaryPrimitives)] = benchmarks.HandWritten_BinaryPrimitives,
            [nameof(benchmarks.Stock_MemoryMarshal)] = benchmarks.Stock_MemoryMarshal,
            [nameof(benchmarks.Stock_BinaryReader)] = benchmarks.Stock_BinaryReader,
            [nameof(benchmarks.Stock_MarshalPtrToStructure)] = benchmarks.Stock_MarshalPtrToStructure,
            [nameof(benchmarks.Kaitai_Struct)] = benchmarks.Kaitai_Struct,
            [nameof(benchmarks.Own_MemoryPack)] = benchmarks.Own_MemoryPack,
            [nameof(benchmarks.Own_MessagePack)] = benchmarks.Own_MessagePack,
            [nameof(benchmarks.Own_ProtobufNet)] = benchmarks.Own_ProtobufNet,
            [nameof(benchmarks.Own_FlatSharp)] = benchmarks.Own_FlatSharp,
            [nameof(benchmarks.Own_SystemTextJson)] = benchmarks.Own_SystemTextJson,
        };
        RequireEveryBenchmark(typeof(DeserializeBenchmarks), cases.Keys, failures);

        foreach ((string name, Func<ulong> run) in cases)
        {
            Report($"{nameof(DeserializeBenchmarks)}.{name}", () => run() == expected ? null : "read different values", failures);
        }

        benchmarks.Cleanup();
    }

    /// <summary>
    ///     Checks that every "same bytes" serialize case writes the reference layout, and that every own-format case
    ///     writes the payload its library produced during setup and reads back to the sample record.
    /// </summary>
    /// <param name="payloads">The reference encodings.</param>
    /// <param name="expected">The fingerprint of the sample record.</param>
    /// <param name="failures">Receives one message per failed check.</param>
    private static void CheckSerializers(Payloads payloads, ulong expected, List<string> failures)
    {
        var benchmarks = new SerializeBenchmarks();
        benchmarks.Setup();

        // Each case writes, then its output is compared with the reference bytes and decoded by its own library.
        // Span writers leave their output at the start of Destination; appending writers in BufferWriter.
        // Copies the first length bytes a span writer left in the destination.
        byte[] SpanOutput(int length) => benchmarks.Destination[..length];

        // Copies what an appending writer added; the reported length is implied by the writer.
        byte[] AppendedOutput(int length) => benchmarks.BufferWriter.WrittenSpan.ToArray();

        var cases = new Dictionary<string, (Func<int> Run, Func<int, byte[]> Output, byte[] Reference, Func<byte[], ulong> Decode)>(StringComparer.Ordinal)
        {
            [nameof(benchmarks.CStructSharp_GeneratedSerialize)] = (benchmarks.CStructSharp_GeneratedSerialize, SpanOutput, payloads.Layout, DecodeLayout),
            [nameof(benchmarks.CStructSharp_RuntimeSerializeMapped)] = (benchmarks.CStructSharp_RuntimeSerializeMapped, SpanOutput, payloads.Layout, DecodeLayout),
            [nameof(benchmarks.CStructSharp_RuntimeSerializeStructValue)] = (benchmarks.CStructSharp_RuntimeSerializeStructValue, SpanOutput, payloads.Layout, DecodeLayout),
            [nameof(benchmarks.HandWritten_BinaryPrimitives)] = (benchmarks.HandWritten_BinaryPrimitives, SpanOutput, payloads.Layout, DecodeLayout),
            [nameof(benchmarks.Stock_MemoryMarshal)] = (benchmarks.Stock_MemoryMarshal, SpanOutput, payloads.Layout, DecodeLayout),
            [nameof(benchmarks.Stock_BinaryWriter)] = (benchmarks.Stock_BinaryWriter, SpanOutput, payloads.Layout, DecodeLayout),
            [nameof(benchmarks.Stock_MarshalStructureToPtr)] = (benchmarks.Stock_MarshalStructureToPtr, SpanOutput, payloads.Layout, DecodeLayout),
            [nameof(benchmarks.Own_MemoryPack)] = (benchmarks.Own_MemoryPack, AppendedOutput, payloads.MemoryPack, bytes => Fingerprints.Of(MemoryPackSerializer.Deserialize<ReadingDto>(bytes)!)),
            [nameof(benchmarks.Own_MessagePack)] = (benchmarks.Own_MessagePack, AppendedOutput, payloads.MessagePack, bytes => Fingerprints.Of(MessagePackSerializer.Deserialize<ReadingDto>(bytes))),
            [nameof(benchmarks.Own_ProtobufNet)] = (benchmarks.Own_ProtobufNet, AppendedOutput, payloads.Protobuf, bytes => Fingerprints.Of(ProtoBuf.Serializer.Deserialize<ReadingDto>((ReadOnlySpan<byte>)bytes))),
            [nameof(benchmarks.Own_FlatSharp)] = (benchmarks.Own_FlatSharp, SpanOutput, payloads.FlatBuffers, bytes => Fingerprints.Of(FlatReading.Serializer.Parse(bytes))),
            [nameof(benchmarks.Own_SystemTextJson)] = (benchmarks.Own_SystemTextJson, AppendedOutput, payloads.Json, bytes => Fingerprints.Of(JsonSerializer.Deserialize(bytes, ReadingJsonContext.Default.ReadingDto)!)),
        };
        RequireEveryBenchmark(typeof(SerializeBenchmarks), cases.Keys, failures);

        foreach ((string name, (Func<int> run, Func<int, byte[]> output, byte[] reference, Func<byte[], ulong> decode)) in cases)
        {
            Report(
                $"{nameof(SerializeBenchmarks)}.{name}",
                () =>
                {
                    // Clear both destinations so that bytes left by an earlier case cannot pass for this one's output.
                    Array.Clear(benchmarks.Destination);
                    benchmarks.BufferWriter.Clear();
                    byte[] written = output(run());
                    if (!written.AsSpan().SequenceEqual(reference))
                    {
                        return $"wrote {written.Length} bytes that differ from the {reference.Length}-byte reference";
                    }

                    return decode(written) == expected ? null : "wrote bytes that read back as different values";
                },
                failures);
        }

        benchmarks.Cleanup();
    }

    /// <summary>
    ///     Checks the variable-length packet cases: every reader reports the sample packet, and every writer writes the
    ///     hand-written reference bytes.
    /// </summary>
    /// <param name="failures">Receives one message per failed check.</param>
    private static void CheckVariable(List<string> failures)
    {
        var benchmarks = new Variable.VariableBenchmarks();
        benchmarks.Setup();
        ulong expected = Variable.PacketSample.Of(Variable.PacketSample.CreateDto());
        var reads = new Dictionary<string, Func<ulong>>(StringComparer.Ordinal)
        {
            [nameof(benchmarks.Runtime_Parse)] = benchmarks.Runtime_Parse,
            [nameof(benchmarks.Runtime_ParseStream)] = benchmarks.Runtime_ParseStream,
            [nameof(benchmarks.Runtime_ReadValueMapped)] = benchmarks.Runtime_ReadValueMapped,
            [nameof(benchmarks.Generated_Parse)] = benchmarks.Generated_Parse,
            [nameof(benchmarks.HandWritten_Read)] = benchmarks.HandWritten_Read,
        };
        var writes = new Dictionary<string, Func<int>>(StringComparer.Ordinal)
        {
            [nameof(benchmarks.Runtime_SerializeStructValue)] = benchmarks.Runtime_SerializeStructValue,
            [nameof(benchmarks.Runtime_SerializeMapped)] = benchmarks.Runtime_SerializeMapped,
            [nameof(benchmarks.Generated_Serialize)] = benchmarks.Generated_Serialize,
            [nameof(benchmarks.HandWritten_Write)] = benchmarks.HandWritten_Write,
        };
        RequireEveryBenchmark(typeof(Variable.VariableBenchmarks), reads.Keys.Concat(writes.Keys), failures);
        foreach ((string name, Func<ulong> run) in reads)
        {
            Report($"{nameof(Variable.VariableBenchmarks)}.{name}", () => run() == expected ? null : "read different values", failures);
        }

        foreach ((string name, Func<int> run) in writes)
        {
            Report(
                $"{nameof(Variable.VariableBenchmarks)}.{name}",
                () =>
                {
                    Array.Clear(benchmarks.Destination);
                    byte[] written = benchmarks.Destination[..run()];
                    return written.AsSpan().SequenceEqual(benchmarks.Bytes) ? null : $"wrote {written.Length} bytes that differ from the {benchmarks.Bytes.Length}-byte reference";
                },
                failures);
        }
    }

    /// <summary>Decodes the C layout with the hand-written reader, for checking the layout writers.</summary>
    /// <param name="bytes">The layout bytes.</param>
    /// <returns>The fingerprint of the decoded record.</returns>
    private static ulong DecodeLayout(byte[] bytes) => Fingerprints.Of(HandWrittenCodec.Read(bytes));

    /// <summary>Records a failure for every <c>[Benchmark]</c> method of <paramref name="type" /> without a check.</summary>
    /// <param name="type">The benchmark class.</param>
    /// <param name="checkedNames">The method names that have a check.</param>
    /// <param name="failures">Receives one message per unchecked method.</param>
    private static void RequireEveryBenchmark(Type type, IEnumerable<string> checkedNames, List<string> failures)
    {
        var known = new HashSet<string>(checkedNames, StringComparer.Ordinal);
        foreach (MethodInfo method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance))
        {
            if (method.GetCustomAttribute<BenchmarkAttribute>() is not null && !known.Contains(method.Name))
            {
                failures.Add($"{type.Name}.{method.Name}: no verification check");
            }
        }
    }

    /// <summary>Runs one check, printing its result; an exception counts as a failure with its message.</summary>
    /// <param name="name">The case name to print.</param>
    /// <param name="check">Returns <see langword="null" /> on success or a description of the problem.</param>
    /// <param name="failures">Receives the failure message.</param>
    private static void Report(string name, Func<string?> check, List<string> failures)
    {
        string? problem;
        try
        {
            problem = check();
        }
        catch (Exception exception)
        {
            problem = $"{exception.GetType().Name}: {exception.Message}";
        }

        if (problem is null)
        {
            Console.WriteLine($"ok   {name}");
        }
        else
        {
            failures.Add($"{name}: {problem}");
        }
    }
}
