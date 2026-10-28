namespace DiscoveryLibraryProbe;

using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using CStructSharp;
using CStructSharp.Memory;

/// <summary>Owns identical synthetic inputs and prepared schema decisions; each call constructs its complete result.</summary>
internal sealed class Workload
{
    private const string Declaration = "struct Header { uint64 tag @magic(tag == 0xFEDCBA9876543210); uint64 unused; };";
    private readonly string mode;
    private readonly byte[] bytes;
    private readonly byte[] textStorage;
    private readonly CStruct layout = new(Declaration);
    private readonly MemoryAccessContext context = new() { MaxTotalBytes = long.MaxValue, MaxRequests = int.MaxValue };
    private readonly MemorySession session;
    private readonly MemoryRegion region;
    private readonly MemoryFieldReader<uint>[] fields;
#if PREPARED
    private readonly PreparedMatcher matcher;
    private readonly MemoryFieldProbe<ulong> probe;
    private readonly MemoryReadSet readSet;
    private readonly MemoryByteReader text;
#endif

    /// <summary>Builds stable success, rejection or short data and the exact same field layouts on both versions.</summary>
    /// <param name="mode">Measured operation.</param>
    /// <param name="scenario">Recognition input or small/large field count.</param>
    public Workload(string mode, string scenario)
    {
        this.mode = mode;
        this.bytes = new byte[scenario == "short" ? 4 : 16];
        if (scenario == "success")
        {
            BinaryPrimitives.WriteUInt64LittleEndian(this.bytes, 0xFEDCBA9876543210);
        }

        int count = scenario == "large" ? 32 : 2;

        // Produce matching fixed fields for the small and larger transport controls.
        string declaration = "struct Fields { " + string.Join(" ", Enumerable.Range(0, count).Select(index => $"uint32 f{index};")) + " int8 text[16]; };";
        MemorySchema schema = PortableMemorySchema.Create(new CStruct(declaration), "Fields");
        byte[] storage = new byte[(count * 4) + 16];
        for (int index = 0; index < count; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(storage.AsSpan(index * 4), (uint)index + 100);
        }

        "synthetic text\0\0"u8.CopyTo(storage.AsSpan(count * 4));
        this.textStorage = storage.AsSpan(count * 4, 16).ToArray();
        this.Source = new CountingSource(storage);
        this.region = new MemoryRegion(this.Source, 0, storage.Length);
        this.session = new MemorySession(schema);

        // Bind every selected scalar before repeated execution so planning is labeled separately.
        this.fields = Enumerable.Range(0, count).Select(index => schema.Bind<uint>("Fields", $"f{index}")).ToArray();
#if PREPARED
        this.matcher = this.layout.PrepareMatcher("Header");
        MemorySchema header = PortableMemorySchema.Create(this.layout, "Header");

        // This pure gate recognizes only the stored scalar; it says nothing about object liveness.
        this.probe = new MemoryFieldProbe<ulong>("header.tag", header.Bind<ulong>("Header", "tag"), static value => value == 0xFEDCBA9876543210);

        // The read set receives the same ordered paths as the individual-field control.
        this.readSet = new MemoryReadSet(schema, "Fields", this.fields.Select(field => field.Selection.Path));
        this.text = schema.BindBytes("Fields", "text");
#endif
    }

    /// <summary>Gets actual leaf transport counters; local decode and preparation are separately labeled.</summary>
    public CountingSource Source { get; }

    /// <summary>Executes one complete measured operation and includes all result values in its checksum.</summary>
    /// <returns>Exact recognition or complete owned result checksum.</returns>
    public ulong Run()
    {
        switch (this.mode)
        {
        case "cold":
        case "prepare":
            return new CStruct(Declaration).Matches("Header", this.bytes) ? 1UL : 0;
        case "runtime":
            return this.layout.Matches("Header", this.bytes) ? 1UL : 0;
        case "manual":
            return this.bytes.Length >= 8 && BinaryPrimitives.ReadUInt64LittleEndian(this.bytes) == 0xFEDCBA9876543210 ? 1UL : 0;
        case "generated":
            return WireHeader.Matches(this.bytes) ? 1UL : 0;
#if PREPARED
        case "prepared":
            return this.matcher.Matches(this.bytes) ? 1UL : 0;
        case "probe":
            return this.probe.Evaluate(this.bytes, this.context) == MemoryProbeOutcome.Match ? 1UL : 0;
        case "bind-bytes":
            return (ulong)this.session.Schema.BindBytes("Fields", "text").Length;
        case "lookup":
            return (ulong)this.layout.PrepareMatcher("Header").Rules.Count;
        case "plan":
            return this.layout.PrepareMatcher("Header").Matches(this.bytes) ? 1UL : 0;
        case "coalesced":
        case "elementwise":
            MemorySnapshot captured = this.readSet.Capture(this.region, this.context, this.mode == "coalesced" ? MemoryReadMode.Coalesced : MemoryReadMode.Elementwise);
            var result = new uint[this.fields.Length];
            for (int index = 0; index < result.Length; index++)
            {
                if (!captured.TryRead(this.fields[index], this.context, out result[index]))
                {
                    throw new InvalidOperationException("Synthetic field unavailable.");
                }
            }

            return Sum(result);
        case "bytes":
            byte[] raw = new byte[this.text.Length];
            if (!this.text.Read(this.session, this.region, raw, this.context).IsComplete)
            {
                throw new InvalidOperationException("Synthetic text unavailable.");
            }

            return TextChecksum(Encoding.ASCII.GetString(raw));
#endif
        case "fields":
            var values = new uint[this.fields.Length];
            for (int index = 0; index < values.Length; index++)
            {
                values[index] = this.fields[index].Read(this.session, this.region, this.context);
            }

            return Sum(values);
        case "text-only":
            return TextChecksum(Encoding.ASCII.GetString(this.textStorage));
        case "strings":
            return TextChecksum(Encoding.ASCII.GetString(this.session.ReadBytes(this.region, "Fields", "text", this.context)));
        default:
            throw new ArgumentException("Unknown or unavailable operation: " + this.mode);
        }
    }

    /// <summary>Consumes every owned numeric result.</summary>
    /// <param name="values">Complete returned fields.</param>
    /// <returns>Sum of every exact value.</returns>
    private static ulong Sum(uint[] values)
    {
        ulong sum = 0;
        foreach (uint value in values)
        {
            sum += value;
        }

        return sum;
    }

    /// <summary>Consumes every character without trimming or normalizing raw evidence.</summary>
    /// <param name="text">Complete owned text, including NUL storage.</param>
    /// <returns>Sum of all code units.</returns>
    private static ulong TextChecksum(string text)
    {
        ulong sum = 0;
        foreach (char character in text)
        {
            sum += character;
        }

        return sum;
    }
}
