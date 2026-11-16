namespace CStructSharp.Memory.Metadata;

using System.Buffers.Binary;
using System.Collections.ObjectModel;
using CStructSharp.Diagnostics;

/// <summary>Reads native MSF 7/CodeView PDB metadata for explicit, portable ISF preparation.</summary>
/// <remarks>
/// Only metadata streams are retained; the containing PDB is never copied as a whole. The input stays owned by its
/// caller and its position is restored. No file, network connection, symbol server or native debugger is opened.
/// Exported ISF uses the existing <see cref="IsfMetadata"/> importer and memory-schema codecs for inspection.
/// </remarks>
public sealed partial class PdbMetadata
{
    private readonly TypeRecord[] types;
    private readonly uint firstType;
    private readonly Dictionary<uint, NamedType> namedTypes = new();
    private readonly Dictionary<uint, string> names = new();
    private readonly Dictionary<uint, uint> forwardTypes = new();
    private readonly Dictionary<uint, List<NamedType>> ambiguousViews = new();
    private readonly List<NamedType> definitions = new();
    private readonly List<string> resolutionDiagnostics = new();
    private readonly Dictionary<string, ulong> symbols = new(StringComparer.Ordinal);
    private readonly Dictionary<string, uint> symbolTypes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, ulong> absoluteSymbols = new(StringComparer.Ordinal);
    private readonly int maximumRecords;

    /// <summary>Parses the original stream directory, identity, type records and global/public symbol records.</summary>
    /// <param name="input">Readable, seekable original PDB stream, borrowed only during construction.</param>
    /// <param name="maxMetadataBytes">Shared retained directory/stream byte limit; not the containing file's length.</param>
    /// <param name="maxRecords">Maximum records per input table: type records, symbol records, OMAP entries and members of one composite.</param>
    /// <param name="cancellationToken">Checked during original block reads and record enumeration.</param>
    /// <param name="targetMachine">Explicit target PE machine when the original DBI machine is zero; a recorded nonzero machine must agree.</param>
    /// <exception cref="ArgumentNullException">The input is null.</exception>
    /// <exception cref="ArgumentException">The input cannot be read and sought.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A budget is not positive or an explicit target machine is unsupported.</exception>
    /// <exception cref="CStructLayoutException">Metadata is malformed, exceeds a budget or uses an unsupported native revision.</exception>
    /// <exception cref="OperationCanceledException">Cancellation was requested.</exception>
    public PdbMetadata(Stream input, int maxMetadataBytes = 64 * 1024 * 1024, int maxRecords = 1_000_000, CancellationToken cancellationToken = default, ushort? targetMachine = null)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxMetadataBytes);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxRecords);
        if (targetMachine is not null and not (0x014C or 0x8664 or 0xAA64))
        {
            throw new ArgumentOutOfRangeException(nameof(targetMachine), "The explicit target machine must be x86, x64 or ARM64.");
        }

        cancellationToken.ThrowIfCancellationRequested();
        this.maximumRecords = maxRecords;
        if (!input.CanSeek)
        {
            throw new ArgumentException("Native PDB metadata requires a seekable stream.", nameof(input));
        }

        long originalPosition = input.Position;
        try
        {
            var file = new PdbMsfFile(input, maxMetadataBytes, cancellationToken);

            // ReadStreamRange returns exactly the requested extent or throws, so only the header values need checks.
            byte[] information = file.ReadStreamRange(1, 0, 28);
            byte[] debug = file.ReadStreamRange(3, 0, 64);
            if (U32(information, 0) < 20000404 || U32(debug, 0) != uint.MaxValue
                || U32(debug, 4) is not (19990903 or 20091201))
            {
                throw Invalid("the identity or debug-information stream has an unsupported header");
            }

            this.Guid = new Guid(information.AsSpan(12, 16));
            this.InformationAge = U32(information, 8);
            this.DbiAge = U32(debug, 8);
            this.Machine = U16(debug, 58);
            if (this.Machine != 0 && targetMachine is { } expectedMachine && this.Machine != expectedMachine)
            {
                throw Invalid($"recorded machine 0x{this.Machine:X4} contradicts explicit target 0x{expectedMachine:X4}");
            }

            this.TargetMachine = targetMachine ?? this.Machine;
            this.PointerSize = this.TargetMachine switch
            {
                0x014C => 4,
                0x8664 or 0xAA64 => 8,
                0 when this.Machine == 0 => throw Invalid("the DBI machine is unspecified; provide an explicit targetMachine instead of inferring the host architecture"),
                _ => throw Invalid($"machine 0x{this.Machine:X4} is not supported"),
            };
            if (this.Guid == System.Guid.Empty || this.InformationAge == 0 || this.DbiAge == 0)
            {
                throw Invalid("the original GUID or age fields are empty");
            }

            byte[] typeBytes = file.ReadStream(2);
            if (typeBytes.Length < 56 || U32(typeBytes, 0) is not (19990903 or 20040203))
            {
                throw Invalid("the type stream does not use a supported CodeView V70/V80 header");
            }

            uint headerSize = U32(typeBytes, 4);
            this.firstType = U32(typeBytes, 8);
            uint typeEnd = U32(typeBytes, 12);
            uint typeLength = U32(typeBytes, 16);
            if (headerSize < 56 || headerSize > typeBytes.Length || this.firstType != 0x1000
                || typeEnd < this.firstType || typeEnd - this.firstType > maxRecords
                || typeLength > (ulong)typeBytes.Length - headerSize)
            {
                throw Invalid("the type index interval or record extent is inconsistent");
            }

            this.types = new TypeRecord[typeEnd - this.firstType];
            int cursor = (int)headerSize;
            int end = checked(cursor + (int)typeLength);
            for (int index = 0; index < this.types.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (end - cursor < 4)
                {
                    throw Invalid("the type record table is truncated");
                }

                int length = U16(typeBytes, cursor);
                if (length < 2 || length > end - cursor - 2)
                {
                    throw Invalid("a type record extends beyond its declared table");
                }

                this.types[index] = new(U16(typeBytes, cursor + 2), typeBytes.AsMemory(cursor + 4, length - 2));
                cursor += length + 2;
            }

            if (cursor != end)
            {
                throw Invalid("the type count and original record-byte extent disagree");
            }

            this.IndexNames(cancellationToken);
            this.ReadSymbols(file, debug, cancellationToken);
            this.UserTypeNames = Array.AsReadOnly(this.definitions.Where(type => type.Kind != 0x1507).Select(type => this.names[type.Index])
                .Concat(this.ambiguousViews.Keys.Select(index => this.names[index])).Order(StringComparer.Ordinal).ToArray());
            this.Symbols = new ReadOnlyDictionary<string, ulong>(this.symbols);
            this.SymbolRecordKinds = new ReadOnlyDictionary<ushort, int>(this.symbolRecordKinds);
            this.Diagnostics = this.resolutionDiagnostics.AsReadOnly();
            this.AbsoluteSymbols = new ReadOnlyDictionary<string, ulong>(this.absoluteSymbols);
        }
        finally
        {
            input.Position = originalPosition;
        }
    }

    /// <summary>Gets the exact GUID in the original PDB-information stream.</summary>
    public Guid Guid { get; }

    /// <summary>Gets the original PDB-information stream's write counter, without substituting the DBI value.</summary>
    public uint InformationAge { get; }

    /// <summary>Gets the separate original DBI age, without hiding a difference from <see cref="InformationAge"/>.</summary>
    public uint DbiAge { get; }

    /// <summary>Gets the original DBI PE machine identifier, including zero when the producer did not record one.</summary>
    public ushort Machine { get; }

    /// <summary>Gets the recorded or explicitly supplied target machine without replacing the original <see cref="Machine"/> value.</summary>
    public ushort TargetMachine { get; }

    /// <summary>Gets the target pointer width from <see cref="TargetMachine"/>, never the analyzing host.</summary>
    public int PointerSize { get; }

    /// <summary>Gets complete composite names in deterministic ordinal order; ambiguous names retain their type indices.</summary>
    public IReadOnlyList<string> UserTypeNames { get; }

    /// <summary>Gets original public, global and unambiguous file-local image-relative addresses after any recorded OMAP transformation.</summary>
    public IReadOnlyDictionary<string, ulong> Symbols { get; }

    /// <summary>Gets original absolute symbol values, kept separate from image-relative memory coordinates.</summary>
    public IReadOnlyDictionary<string, ulong> AbsoluteSymbols { get; }

    /// <summary>Gets counts of every original global-symbol record kind, including non-address records not exported to ISF.</summary>
    public IReadOnlyDictionary<ushort, int> SymbolRecordKinds { get; }

    /// <summary>Gets the number of distinct symbol names that are absolute or OMAP-unrepresented, so none is misrepresented as an image-relative address.</summary>
    /// <remarks>A name recorded by both a public and a global record counts once.</remarks>
    public int UnmappedSymbolCount => this.unmappedSymbols.Count;

    /// <summary>Gets source identity ambiguities that were preserved or omitted rather than silently resolved by name.</summary>
    /// <remarks>
    /// Type entries describe display names shared by several complete definitions. Symbol entries describe
    /// file-local data names (<c>S_LDATA32</c>) that have several addresses, or that differ from a public or global
    /// symbol of the same name; such a local name is omitted from <see cref="Symbols"/>.
    /// </remarks>
    public IReadOnlyList<string> Diagnostics { get; }

    /// <summary>Keeps struct/class, union and enum identity namespaces separate during forward resolution.</summary>
    /// <param name="type">Original named type.</param>
    /// <returns>A lookup key, not an exported type name.</returns>
    private static string TypeKey(NamedType type) => $"{(type.Kind is 0x1504 or 0x1505 ? 0x1505 : type.Kind):X4}:{type.UniqueName}";

    /// <summary>Reads a bounded original little-endian word.</summary>
    /// <param name="bytes">Containing metadata record.</param>
    /// <param name="offset">Byte offset within that record.</param>
    /// <returns>The original unsigned value.</returns>
    private static ushort U16(ReadOnlySpan<byte> bytes, int offset)
    {
        if ((uint)offset > (uint)bytes.Length || bytes.Length - offset < 2)
        {
            throw Invalid("a two-byte metadata field is truncated");
        }

        return BinaryPrimitives.ReadUInt16LittleEndian(bytes[offset..]);
    }

    /// <summary>Reads a bounded original little-endian double word.</summary>
    /// <param name="bytes">Containing metadata record.</param>
    /// <param name="offset">Byte offset within that record.</param>
    /// <returns>The original unsigned value.</returns>
    private static uint U32(ReadOnlySpan<byte> bytes, int offset)
    {
        if ((uint)offset > (uint)bytes.Length || bytes.Length - offset < 4)
        {
            throw Invalid("a four-byte metadata field is truncated");
        }

        return BinaryPrimitives.ReadUInt32LittleEndian(bytes[offset..]);
    }

    /// <summary>Preserves a source-specific malformed-metadata diagnostic.</summary>
    /// <param name="reason">The violated native format invariant.</param>
    /// <returns>The layout exception reported by the public metadata boundary.</returns>
    private static CStructLayoutException Invalid(string reason) => new($"Native PDB metadata is invalid: {reason}.");

    /// <summary>Indexes complete and forward-declared identities without conflating equal display names.</summary>
    /// <param name="cancellationToken">Checked between type records.</param>
    /// <exception cref="CStructLayoutException">A named record is malformed or its native extent is invalid.</exception>
    private void IndexNames(CancellationToken cancellationToken)
    {
        var complete = new Dictionary<string, List<NamedType>>(StringComparer.Ordinal);
        var displayCounts = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int index = 0; index < this.types.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            TypeRecord record = this.types[index];
            if (record.Kind is not (0x1504 or 0x1505 or 0x1506 or 0x1507))
            {
                continue;
            }

            uint identity = this.firstType + (uint)index;
            var reader = new PdbRecordReader(record.Bytes.Span);
            ushort count = reader.UInt16();
            ushort properties = reader.UInt16();
            uint underlying = 0;
            uint fields;
            int size = 0;
            if (record.Kind == 0x1507)
            {
                underlying = reader.UInt32();
                fields = reader.UInt32();
            }
            else
            {
                fields = reader.UInt32();
                if (record.Kind is 0x1504 or 0x1505)
                {
                    _ = reader.UInt32();
                    _ = reader.UInt32();
                }

                size = reader.Size();
            }

            string name = reader.Name();
            string unique = (properties & 0x200) != 0 ? reader.Name() : name;
            reader.End();
            var type = new NamedType(identity, record.Kind, count, fields, underlying, size, name, unique, (properties & 0x80) != 0);
            this.namedTypes.Add(identity, type);
            if (type.Forward)
            {
                continue;
            }

            this.definitions.Add(type);
            string key = TypeKey(type);
            if (!complete.TryGetValue(key, out List<NamedType>? alternatives))
            {
                alternatives = [];
                complete.Add(key, alternatives);
            }

            alternatives.Add(type);
            displayCounts[name] = displayCounts.GetValueOrDefault(name) + 1;
        }

        foreach (NamedType type in this.definitions)
        {
            string name = type.Name.Length == 0 ? $"__cv_{type.Index:X8}" : type.Name;
            this.names.Add(type.Index, displayCounts[type.Name] > 1 ? $"{name}@cv-{type.Index:X8}" : name);
        }

        foreach (NamedType type in this.namedTypes.Values)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!type.Forward)
            {
                continue;
            }

            if (complete.TryGetValue(TypeKey(type), out List<NamedType>? alternatives))
            {
                if (alternatives.Count != 1)
                {
                    this.names.Add(type.Index, $"{type.Name}@cv-{type.Index:X8}");
                    bool sameExtent = alternatives[0].Kind != 0x1507 && alternatives[0].Size > 0
                        && alternatives.All(candidate => candidate.Kind != 0x1507 && candidate.Size == alternatives[0].Size);
                    if (sameExtent)
                    {
                        this.ambiguousViews.Add(type.Index, alternatives);
                    }

                    string interpretation = sameExtent ? "an explicit same-extent union of the original complete views" : "address-only because the complete extents disagree";
                    this.resolutionDiagnostics.Add($"Forward type 0x{type.Index:X8} '{type.Name}' has several complete identities ({string.Join(", ", alternatives.Select(candidate => $"0x{candidate.Index:X8}"))}); each is preserved separately and the target is {interpretation}.");
                    continue;
                }

                this.names.Add(type.Index, this.names[alternatives[0].Index]);
                this.forwardTypes.Add(type.Index, alternatives[0].Index);
            }
            else
            {
                this.names.Add(type.Index, $"{type.Name}@cv-{type.Index:X8}");
            }
        }
    }

    /// <summary>Finds one original type record by its CodeView identity.</summary>
    /// <param name="index">Original type index, not a display-name lookup.</param>
    /// <returns>The bounded original record.</returns>
    /// <exception cref="CStructLayoutException">The reference is outside the declared type table.</exception>
    private TypeRecord Type(uint index)
    {
        if (index < this.firstType || index - this.firstType >= this.types.Length)
        {
            throw Invalid($"type reference 0x{index:X8} is outside the type table");
        }

        return this.types[index - this.firstType];
    }

    /// <summary>A type leaf and its original payload, sharing the retained TPI stream.</summary>
    /// <param name="Kind">CodeView leaf code.</param>
    /// <param name="Bytes">Payload excluding the length and leaf code.</param>
    private readonly record struct TypeRecord(ushort Kind, ReadOnlyMemory<byte> Bytes);

    /// <summary>Original named storage metadata, before forward resolution or output-name qualification.</summary>
    /// <param name="Index">Native type index.</param>
    /// <param name="Kind">Native storage leaf.</param>
    /// <param name="Count">Declared immediate-member count.</param>
    /// <param name="Fields">Native field-list index.</param>
    /// <param name="Underlying">Enum storage type, or zero.</param>
    /// <param name="Size">Declared composite byte size.</param>
    /// <param name="Name">Original display name.</param>
    /// <param name="UniqueName">Original unique name, or the display name if none was recorded.</param>
    /// <param name="Forward">Whether this is an incomplete forward declaration.</param>
    private sealed record NamedType(uint Index, ushort Kind, ushort Count, uint Fields, uint Underlying, int Size, string Name, string UniqueName, bool Forward);
}
