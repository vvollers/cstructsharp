namespace CStructSharp.Benchmarks;

using System.Buffers.Binary;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Configs;
using CStructSharp.Benchmarks.GeneratedLayouts;

/// <summary>
///     Reads of a layout with field constraints against the same layout without them: the generated <c>Parse</c> with the
///     default limits, with a <see cref="ReadLimits"/> and with <see cref="ReadOptions"/>, a view's <c>Validate</c>,
///     <c>Matches</c>, 256 consecutive records through <c>Records</c> and a read from a stream. These cases guard the cost of
///     constraint handling, so a change to constraint plumbing shows here first.
/// </summary>
[BenchmarkCategory("Constraints")]
[GroupBenchmarksBy(BenchmarkLogicalGroupRule.ByCategory)]
public class ConstraintBenchmarks
{
    private const int RecordSize = 24;

    private byte[] record = null!;
    private byte[] records256 = null!;
    private byte[] variableRecord = null!;
    private MemoryStream stream = null!;
    private ReadOptions options = null!;
    private ReadLimits limits;

    /// <summary>Builds a valid superblock, 256 copies of it, a hidden-buffer stream over it and the read settings the cases pass.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.record = new byte[RecordSize];
        BinaryPrimitives.WriteUInt32LittleEndian(this.record.AsSpan(0), 8192);
        BinaryPrimitives.WriteUInt32LittleEndian(this.record.AsSpan(4), 65536);
        BinaryPrimitives.WriteUInt16LittleEndian(this.record.AsSpan(8), 0xEF53);
        BinaryPrimitives.WriteUInt16LittleEndian(this.record.AsSpan(10), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(this.record.AsSpan(12), 2);
        BinaryPrimitives.WriteUInt32LittleEndian(this.record.AsSpan(16), 1);
        BinaryPrimitives.WriteUInt32LittleEndian(this.record.AsSpan(20), 8192);
        this.records256 = new byte[RecordSize * 256];
        for (int index = 0; index < 256; index++)
        {
            this.record.CopyTo(this.records256, index * RecordSize);
        }

        this.variableRecord = new byte[4 + 2 + 1 + 4 + 4];
        BinaryPrimitives.WriteUInt32LittleEndian(this.variableRecord.AsSpan(0), 8192);
        BinaryPrimitives.WriteUInt16LittleEndian(this.variableRecord.AsSpan(4), 0xEF53);
        this.variableRecord[6] = 4;
        BinaryPrimitives.WriteUInt32LittleEndian(this.variableRecord.AsSpan(11), 1);
        this.stream = new MemoryStream(this.record, 0, this.record.Length, writable: false, publiclyVisible: false);
        this.options = new ReadOptions { MaxArrayElements = 1000 };
        this.limits = new ReadLimits { MaxArrayElements = 1000 };
    }

    /// <summary>Generated <c>Parse</c> of the constrained superblock with default limits.</summary>
    /// <returns>The superblock.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "Constraints")]
    public ExtSuperblockLayout.Sb Ext_Parse() => ExtSuperblockLayout.Parse(this.record);

    /// <summary>Generated <c>Parse</c> of the constrained superblock with a <see cref="ReadLimits"/>.</summary>
    /// <returns>The superblock.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "Constraints")]
    public ExtSuperblockLayout.Sb Ext_Parse_Limits() => ExtSuperblockLayout.Parse(this.record, this.limits);

    /// <summary>Generated <c>Parse</c> of the constrained superblock with <see cref="ReadOptions"/>.</summary>
    /// <returns>The superblock.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "Constraints")]
    public ExtSuperblockLayout.Sb Ext_Parse_Options() => ExtSuperblockLayout.Parse(this.record, null, this.options);

    /// <summary>Generated <c>Parse</c> of the same superblock declared without constraints.</summary>
    /// <returns>The superblock.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "Constraints")]
    public PlainSuperblockLayout.Sb Plain_Parse() => PlainSuperblockLayout.Parse(this.record);

    /// <summary>Generated <c>Parse</c> of the constrained superblock generated as a value type.</summary>
    /// <returns>The superblock.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "Constraints")]
    public ExtSuperblockStructLayout.Sb ExtStruct_Parse() => ExtSuperblockStructLayout.Parse(this.record);

    /// <summary>A view's <c>Validate</c> of the constrained superblock.</summary>
    [Benchmark]
    [BenchmarkCategory("Impact", "Constraints")]
    public void Ext_View_Validate() => new ExtSuperblockLayout.SbView(this.record).Validate();

    /// <summary>Generated <c>Matches</c> of the constrained superblock.</summary>
    /// <returns>Whether the bytes match.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "Constraints")]
    public bool Ext_Matches() => ExtSuperblockLayout.Matches(this.record);

    /// <summary>Enumerates 256 constrained superblocks with the generated <c>Records</c>.</summary>
    /// <returns>The number of records.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "Constraints")]
    public int Ext_Records256()
    {
        int count = 0;
        foreach (ExtSuperblockLayout.Sb item in ExtSuperblockLayout.Records(this.records256))
        {
            count++;
        }

        return count;
    }

    /// <summary>Generated <c>Parse(Stream)</c> of the constrained superblock from a hidden-buffer stream.</summary>
    /// <returns>The superblock.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "Constraints")]
    public ExtSuperblockLayout.Sb Ext_ParseStream()
    {
        this.stream.Position = 0;
        return ExtSuperblockLayout.Parse(this.stream);
    }

    /// <summary>Generated <c>Parse</c> of a constrained record with a runtime-sized array: the member-by-member reader checks each condition as it goes.</summary>
    /// <returns>The record.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "Constraints")]
    public ExtVariableLayout.Vx Var_Parse() => ExtVariableLayout.Parse(this.variableRecord);

    /// <summary>The same record read under a <see cref="ReadLimits"/>.</summary>
    /// <returns>The record.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact", "Constraints")]
    public ExtVariableLayout.Vx Var_Parse_Limits() => ExtVariableLayout.Parse(this.variableRecord, this.limits);
}
