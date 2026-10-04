namespace CStructSharp.Benchmarks;

using System.Buffers.Binary;
using System.Text;
using BenchmarkDotNet.Attributes;
using CStructSharp.Benchmarks.GeneratedLayouts;
using CStructSharp.Benchmarks.Scenarios;
using CStructSharp.Values;

/// <summary>Complete short-text records from memory, physical stream calls and asynchronously opened files, with owned write controls.</summary>
[BenchmarkCategory("Impact", "TextRecords")]
public class TextRecordBenchmarks
{
    private CStruct layout = null!;
    private byte[] input = null!;
    private StructValue runtime = null!;
    private TextRecordLayout.Root generated = null!;
    private FileStream file = null!;
    private string filePath = null!;

    /// <summary>Gets or sets the number of fully materialized records per operation.</summary>
    [Params(1, 256)]
    public int Count { get; set; }

    /// <summary>Builds deterministic short multilingual fields with NUL padding, without running either library reader or writer.</summary>
    /// <param name="count">Number of records; each occupies exactly 100 bytes.</param>
    /// <returns>The count header and complete records.</returns>
    public static byte[] CreateInput(int count)
    {
        byte[] bytes = new byte[4 + (count * 100)];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, (uint)count);
        for (int index = 0; index < count; index++)
        {
            Span<byte> row = bytes.AsSpan(4 + (index * 100), 100);
            BinaryPrimitives.WriteUInt32LittleEndian(row, (uint)index);
            Encoding.Unicode.GetBytes("Résumé世界😀", row.Slice(4, 48));
            Encoding.BigEndianUnicode.GetBytes("目录😀", row.Slice(52, 16));
            Encoding.UTF8.GetBytes("Grüße世界", row.Slice(68, 32));
        }

        return bytes;
    }

    /// <summary>Consumes and checks every runtime field outside the measured operations.</summary>
    /// <param name="value">The complete parsed graph.</param>
    /// <param name="count">The expected record count.</param>
    /// <exception cref="InvalidOperationException">A record count or field differs.</exception>
    public static void Verify(StructValue value, int count)
    {
        var rows = (IList<object?>)value["records"]!;
        if (value.Get<uint>("count") != count || rows.Count != count)
        {
            throw new InvalidOperationException("Record count differs.");
        }

        for (int index = 0; index < count; index++)
        {
            var row = (StructValue)rows[index]!;
            if (row.Get<uint>("id") != index || row.Get<string>("name") != "Résumé世界😀".PadRight(24, '\0') ||
                row.Get<string>("tag") != "目录😀".PadRight(8, '\0') || row.Get<string>("label") != "Grüße世界".PadRight(26, '\0'))
            {
                throw new InvalidOperationException("Runtime text record differs from the complete input.");
            }
        }
    }

    /// <summary>Prepares identical bytes, verifies every owned value and output byte, then opens a file for cached asynchronous reads.</summary>
    /// <exception cref="InvalidOperationException">A complete value, byte sequence or ownership check fails.</exception>
    [GlobalSetup]
    public void Setup()
    {
        this.input = CreateInput(this.Count);
        this.layout = FixtureCase.CompileLike(typeof(TextRecordLayout));
        this.runtime = this.layout.Parse(this.input, "root");
        this.generated = TextRecordLayout.Parse(this.input);
        Verify(this.runtime, this.Count);
        for (int index = 0; index < this.Count; index++)
        {
            TextRecordLayout.Item row = this.generated.Records[index];
            if (row.Id != index || row.Name != "Résumé世界😀".PadRight(24, '\0') || row.Tag != "目录😀".PadRight(8, '\0') || row.Label != "Grüße世界".PadRight(26, '\0'))
            {
                throw new InvalidOperationException("Generated text record differs from the input.");
            }
        }

        if (!this.input.AsSpan().SequenceEqual(this.layout.Serialize("root", this.runtime)) ||
            !this.input.AsSpan().SequenceEqual(TextRecordLayout.Serialize(this.generated)))
        {
            throw new InvalidOperationException("Text record serialization differs from the complete input.");
        }

        byte[] ownedInput = (byte[])this.input.Clone();
        StructValue owned = this.layout.Parse(ownedInput, "root");
        TextRecordLayout.Root generatedOwned = TextRecordLayout.Parse(ownedInput);
        Array.Fill(ownedInput, (byte)0xFF);
        Verify(owned, this.Count);
        if (generatedOwned.Records[0].Name != this.generated.Records[0].Name)
        {
            throw new InvalidOperationException("Generated text borrowed mutable input.");
        }

        this.filePath = Path.Combine(Path.GetTempPath(), $"cstructsharp-text-{Guid.NewGuid():N}.bin");
        File.WriteAllBytes(this.filePath, this.input);
        this.file = new FileStream(this.filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);
        Verify(this.Runtime_FileAsync().GetAwaiter().GetResult(), this.Count);
        Verify(this.Runtime_Stream(), this.Count);
    }

    /// <summary>Closes and deletes the exact temporary input created by setup.</summary>
    [GlobalCleanup]
    public void Cleanup()
    {
        this.file?.Dispose();
        if (this.filePath is not null)
        {
            File.Delete(this.filePath);
        }
    }

    /// <summary>Parses every record from memory into an owned runtime graph.</summary>
    /// <returns>The complete graph.</returns>
    [Benchmark]
    public StructValue Runtime_Parse() => this.layout.Parse(this.input, "root");

    /// <summary>Parses through a non-exposable stream; its character reads retain the original callback path.</summary>
    /// <returns>The complete owned graph.</returns>
    [Benchmark]
    public StructValue Runtime_Stream()
    {
        using var stream = new MemoryStream(this.input, writable: false);
        return this.layout.Parse(stream, "root");
    }

    /// <summary>Reads the complete file through the async buffer adapter, then materializes every record.</summary>
    /// <returns>The awaited owned graph; the file is reused with a warm operating-system cache.</returns>
    [Benchmark]
    public ValueTask<StructValue> Runtime_FileAsync()
    {
        this.file.Position = 0;
        return this.layout.ParseAsync(this.file, "root");
    }

    /// <summary>Serializes the owned runtime graph as an opposite-direction control.</summary>
    /// <returns>All encoded bytes in a newly owned array.</returns>
    [Benchmark]
    public byte[] Runtime_Serialize() => this.layout.Serialize("root", this.runtime);

    /// <summary>Parses the identical record layout through unchanged generated readers.</summary>
    /// <returns>Every record and owned string.</returns>
    [Benchmark]
    public TextRecordLayout.Root Generated_Parse() => TextRecordLayout.Parse(this.input);

    /// <summary>Serializes the complete generated graph as an unchanged control.</summary>
    /// <returns>All encoded bytes in a newly owned array.</returns>
    [Benchmark]
    public byte[] Generated_Serialize() => TextRecordLayout.Serialize(this.generated);
}
