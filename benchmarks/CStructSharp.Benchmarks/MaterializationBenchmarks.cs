namespace CStructSharp.Benchmarks;

using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;
using CStructSharp.Benchmarks.GeneratedLayouts;
using CStructSharp.Benchmarks.Scenarios;
using CStructSharp.Values;

/// <summary>Owned generated array and text parsing, including the temporary storage that materialization needs.</summary>
[BenchmarkCategory("Impact", "Materialization")]
public class MaterializationBenchmarks
{
    private readonly ReadOptions trimmed = new() { TrimFixedText = true };
    private byte[] small = null!;
    private byte[] large = null!;
    private byte[] text = null!;
    private CStruct textLayout = null!;
    private CStruct matrixLayout = null!;
    private CStruct smallMatrixLayout = null!;
    private CStruct bigMatrixLayout = null!;
    private StructValue runtimeMatrix = null!;
    private MaterializedSmallMatrix.Root smallValue = null!;
    private MaterializedBigMatrix.Root bigValue = null!;
    private MaterializedLargeMatrix.Root largeValue = null!;

    /// <summary>Loads the existing small matrix, prepares larger inputs, and verifies owned values and output outside timing.</summary>
    /// <exception cref="InvalidOperationException">A generated result differs from the prepared bytes or expected text.</exception>
    [GlobalSetup]
    public void Setup()
    {
        this.small = FixtureCase.LoadMatching("multidim-16x16", typeof(MaterializedSmallMatrix)).Bytes;
        this.large = new byte[256 * 256 * 2];
        for (int index = 0; index < this.large.Length / 2; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(this.large.AsSpan(index * 2), (ushort)((index * 29) + 17));
        }

        this.text = new byte[4096];
        for (int index = 0; index < 768; index++)
        {
            byte character = (byte)('A' + (index % 26));
            this.text[index] = character;
            this.text[1024 + index] = character;
            this.text[2048 + index] = character;
        }

        for (int index = 0; index < 384; index++)
        {
            BinaryPrimitives.WriteUInt16LittleEndian(this.text.AsSpan(3072 + (index * 2)), (ushort)(0x3a0 + (index % 16)));
        }

        this.textLayout = FixtureCase.CompileLike(typeof(MaterializedText));
        this.matrixLayout = FixtureCase.CompileLike(typeof(MaterializedLargeMatrix));
        this.smallMatrixLayout = FixtureCase.CompileLike(typeof(MaterializedSmallMatrix));
        this.bigMatrixLayout = FixtureCase.CompileLike(typeof(MaterializedBigMatrix));
        this.runtimeMatrix = this.matrixLayout.Parse(this.large, "root");
        this.smallValue = MaterializedSmallMatrix.Parse(this.small);
        this.bigValue = MaterializedBigMatrix.Parse(this.large);
        this.largeValue = MaterializedLargeMatrix.Parse(this.large);
        RequireRows(this.runtimeMatrix.Get<ushort[][]>("grid"), this.largeValue.Grid);
        RequireRows(FixtureCase.CompileLike(typeof(MaterializedBigMatrix)).Parse(this.large, "root").Get<ushort[][]>("grid"), MaterializedBigMatrix.Parse(this.large).Grid);
        RequireBytes(this.small, MaterializedSmallMatrix.Serialize(MaterializedSmallMatrix.Parse(this.small)));
        RequireBytes(this.large, MaterializedLargeMatrix.Serialize(this.largeValue));
        RequireBytes(this.large, MaterializedBigMatrix.Serialize(MaterializedBigMatrix.Parse(this.large)));
        RequireBytes(this.large, this.matrixLayout.Serialize("root", this.runtimeMatrix));
        foreach (ReadOptions options in new[] { new ReadOptions(), this.trimmed })
        {
            MaterializedText.Root generated = MaterializedText.Parse(this.text, options);
            StructValue runtime = this.textLayout.Parse(this.text, "root", options: options);
            if (generated.Name != runtime.Get<string>("name") || generated.Utf != runtime.Get<string>("utf") ||
                generated.Oem != runtime.Get<string>("oem") || generated.Wide != runtime.Get<string>("wide"))
            {
                throw new InvalidOperationException("Generated text differs from the runtime.");
            }

            RequireBytes(this.text, MaterializedText.Serialize(generated));
        }

        // Each parse owns its rows; mutating a result must not change the next parse or the borrowed source.
        MaterializedLargeMatrix.Root independent = MaterializedLargeMatrix.Parse(this.large);
        independent.Grid[0][0] ^= 1;
        if (independent.Grid[0][0] == this.largeValue.Grid[0][0] || ReferenceEquals(independent.Grid[0], independent.Grid[1]))
        {
            throw new InvalidOperationException("Matrix rows must be independent owned arrays.");
        }
    }

    /// <summary>Parses the unchanged canonical 16 by 16 matrix fixture.</summary>
    /// <returns>The complete owned matrix.</returns>
    [Benchmark]
    public MaterializedSmallMatrix.Root Generated_Matrix16_Parse() => MaterializedSmallMatrix.Parse(this.small);

    /// <summary>Parses a 128 KiB little-endian matrix into independent rows.</summary>
    /// <returns>The complete owned matrix.</returns>
    [Benchmark]
    public MaterializedLargeMatrix.Root Generated_Matrix256_Parse() => MaterializedLargeMatrix.Parse(this.large);

    /// <summary>Parses the same bytes as a big-endian matrix.</summary>
    /// <returns>The complete owned matrix with byte-swapped elements.</returns>
    [Benchmark]
    public MaterializedBigMatrix.Root Generated_Matrix256Big_Parse() => MaterializedBigMatrix.Parse(this.large);

    /// <summary>Serializes the prepared matrix to check for costs shifted into writing.</summary>
    /// <returns>The complete owned encoded bytes.</returns>
    [Benchmark]
    public byte[] Generated_Matrix256_Serialize() => MaterializedLargeMatrix.Serialize(this.largeValue);

    /// <summary>Serializes the small canonical matrix, including ownership of its output.</summary>
    /// <returns>The complete owned encoded bytes.</returns>
    [Benchmark]
    public byte[] Generated_Matrix16_Serialize() => MaterializedSmallMatrix.Serialize(this.smallValue);

    /// <summary>Serializes the large matrix with explicit big-endian numeric encoding.</summary>
    /// <returns>The complete owned encoded bytes.</returns>
    [Benchmark]
    public byte[] Generated_Matrix256Big_Serialize() => MaterializedBigMatrix.Serialize(this.bigValue);

    /// <summary>Parses the large matrix through the runtime's boxed multidimensional path.</summary>
    /// <returns>The complete owned dynamic result, including every row and element.</returns>
    [Benchmark]
    public StructValue Runtime_Matrix256_Parse() => this.matrixLayout.Parse(this.large, "root");

    /// <summary>Parses the small canonical matrix through runtime row materialization.</summary>
    /// <returns>The complete owned dynamic result.</returns>
    [Benchmark]
    public StructValue Runtime_Matrix16_Parse() => this.smallMatrixLayout.Parse(this.small, "root");

    /// <summary>Parses the large matrix using big-endian runtime numeric decoding.</summary>
    /// <returns>The complete owned dynamic result.</returns>
    [Benchmark]
    public StructValue Runtime_Matrix256Big_Parse() => this.bigMatrixLayout.Parse(this.large, "root");

    /// <summary>Parses the large matrix through a non-exposable stream and returns all owned rows.</summary>
    /// <returns>The complete owned dynamic result after the input stream has been disposed.</returns>
    [Benchmark]
    public StructValue Runtime_Matrix256_Stream()
    {
        using var stream = new System.IO.MemoryStream(this.large, writable: false);
        return this.matrixLayout.Parse(stream, "root");
    }

    /// <summary>Serializes the runtime's complete matrix, including normalization and owned output.</summary>
    /// <returns>The complete owned encoded bytes.</returns>
    [Benchmark]
    public byte[] Runtime_Matrix256_Serialize() => this.matrixLayout.Serialize("root", this.runtimeMatrix);

    /// <summary>Parses fixed Latin-1, UTF-8, CP437 and wide-character buffers without trimming.</summary>
    /// <returns>The owned text record, including its NUL padding.</returns>
    [Benchmark]
    public MaterializedText.Root Generated_Text_Parse() => MaterializedText.Parse(this.text);

    /// <summary>Parses the same complete input with trailing-NUL trimming.</summary>
    /// <returns>The owned text record without trailing NUL characters.</returns>
    [Benchmark]
    public MaterializedText.Root Generated_TextTrimmed_Parse() => MaterializedText.Parse(this.text, this.trimmed);

    /// <summary>Parses the same text through the runtime for shared-decoder comparison.</summary>
    /// <returns>The complete owned dynamic record.</returns>
    [Benchmark]
    public StructValue Runtime_Text_Parse() => this.textLayout.Parse(this.text, "root");

    /// <summary>Parses the same text through the runtime with trimming.</summary>
    /// <returns>The complete owned dynamic record without trailing NUL characters.</returns>
    [Benchmark]
    public StructValue Runtime_TextTrimmed_Parse() => this.textLayout.Parse(this.text, "root", options: this.trimmed);

    /// <summary>Rejects a byte mismatch during untimed setup.</summary>
    /// <param name="expected">The complete input bytes.</param>
    /// <param name="actual">The complete serialized result.</param>
    /// <exception cref="InvalidOperationException">The byte sequences differ.</exception>
    private static void RequireBytes(byte[] expected, byte[] actual)
    {
        if (!expected.AsSpan().SequenceEqual(actual))
        {
            throw new InvalidOperationException("Materialization benchmark did not preserve every byte.");
        }
    }

    /// <summary>Compares every generated matrix element with the runtime outside timing.</summary>
    /// <param name="expected">The runtime's owned rows.</param>
    /// <param name="actual">The generated reader's owned rows.</param>
    /// <exception cref="InvalidOperationException">A dimension or element differs.</exception>
    private static void RequireRows(ushort[][] expected, ushort[][] actual)
    {
        if (expected.Length != actual.Length)
        {
            throw new InvalidOperationException("Matrix row counts differ.");
        }

        for (int row = 0; row < expected.Length; row++)
        {
            if (!expected[row].AsSpan().SequenceEqual(actual[row]))
            {
                throw new InvalidOperationException("Matrix elements differ.");
            }
        }
    }
}
