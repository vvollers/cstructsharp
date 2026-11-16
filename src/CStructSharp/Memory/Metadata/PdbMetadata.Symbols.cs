namespace CStructSharp.Memory.Metadata;

/// <content>Joins original symbol coordinates to their recorded section and optional OMAP metadata.</content>
public sealed partial class PdbMetadata
{
    private readonly Dictionary<ushort, int> symbolRecordKinds = new();
    private readonly HashSet<string> unmappedSymbols = new(StringComparer.Ordinal);

    /// <summary>File-local data candidates by name; a null address marks a name already proved ambiguous.</summary>
    private readonly Dictionary<string, (uint? Address, uint Type)> localSymbols = new(StringComparer.Ordinal);

    /// <summary>Reads an optional DBI debug-stream index, treating the recorded 0xFFFF sentinel as absent.</summary>
    /// <param name="bytes">Original optional DBI debug header.</param>
    /// <param name="index">Zero-based debug-header slot.</param>
    /// <returns>A present stream index, or -1 for an absent slot.</returns>
    private static int OptionalStream(ReadOnlySpan<byte> bytes, int index)
    {
        int offset = checked(index * 2);
        return offset >= bytes.Length || U16(bytes, offset) == ushort.MaxValue ? -1 : U16(bytes, offset);
    }

    /// <summary>Requires complete, strictly ordered source-to-image remapping entries before binary lookup.</summary>
    /// <param name="bytes">Original OMAP_FROM_SRC stream, or empty when no remapping was recorded.</param>
    /// <param name="cancellationToken">Checked between entries.</param>
    /// <exception cref="Diagnostics.CStructLayoutException">The map is truncated, duplicated or unordered.</exception>
    private static void ValidateOmap(ReadOnlySpan<byte> bytes, CancellationToken cancellationToken)
    {
        if (bytes.Length % 8 != 0)
        {
            throw Invalid("the original OMAP table is truncated");
        }

        uint previous = 0;
        for (int offset = 0; offset < bytes.Length; offset += 8)
        {
            cancellationToken.ThrowIfCancellationRequested();
            uint source = U32(bytes, offset);
            if (offset != 0 && source <= previous)
            {
                throw Invalid("the original OMAP coordinates are duplicated or unordered");
            }

            previous = source;
        }
    }

    /// <summary>Maps one original RVA without inventing bytes for deleted or uncovered OMAP intervals.</summary>
    /// <param name="source">Original section-relative coordinate after applying its section base.</param>
    /// <param name="bytes">Validated original OMAP_FROM_SRC entries.</param>
    /// <returns>The mapped image-relative coordinate, or null for an unrepresented interval.</returns>
    /// <exception cref="Diagnostics.CStructLayoutException">The mapped coordinate wraps.</exception>
    private static uint? Map(uint source, ReadOnlySpan<byte> bytes)
    {
        if (bytes.IsEmpty)
        {
            return source;
        }

        int low = 0;
        int high = bytes.Length / 8;
        while (low < high)
        {
            int middle = low + ((high - low) / 2);
            if (U32(bytes, middle * 8) <= source)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        if (low == 0)
        {
            return null;
        }

        int offset = (low - 1) * 8;
        uint destination = U32(bytes, offset + 4);
        if (destination == 0)
        {
            return null;
        }

        ulong mapped = (ulong)destination + source - U32(bytes, offset);
        return mapped <= uint.MaxValue ? (uint)mapped : throw Invalid("an OMAP coordinate wraps");
    }

    /// <summary>Resolves the recorded logical segment through DBI before applying PE section bases.</summary>
    /// <param name="segment">One-based original logical segment.</param>
    /// <param name="offset">Original offset within that segment.</param>
    /// <param name="sectionMap">Original DBI segment descriptors, or empty for direct PE segment indices.</param>
    /// <param name="sections">Original PE section headers used by the symbol producer.</param>
    /// <returns>The original absolute value or image-relative coordinate, explicitly distinguished.</returns>
    /// <exception cref="Diagnostics.CStructLayoutException">A segment, frame or resulting coordinate is inconsistent.</exception>
    private static (bool Absolute, uint Coordinate) SectionCoordinate(ushort segment, uint offset, ReadOnlySpan<byte> sectionMap, ReadOnlySpan<byte> sections)
    {
        if (segment == 0)
        {
            return (true, offset);
        }

        ulong coordinate = offset;
        if (!sectionMap.IsEmpty)
        {
            if (segment > U16(sectionMap, 0))
            {
                throw Invalid("a symbol names a nonexistent logical segment");
            }

            int entry = 4 + ((segment - 1) * 20);
            ushort flags = U16(sectionMap, entry);
            coordinate += U32(sectionMap, entry + 12);
            if ((flags & 0x200) != 0)
            {
                return coordinate <= uint.MaxValue ? (true, (uint)coordinate) : throw Invalid("an absolute symbol coordinate wraps");
            }

            if (U16(sectionMap, entry + 2) != 0 || U16(sectionMap, entry + 4) != 0)
            {
                throw Invalid("overlay or grouped logical segments are not supported");
            }

            segment = U16(sectionMap, entry + 6);
        }

        if (segment == 0 || segment > sections.Length / 40)
        {
            throw Invalid("a symbol names a nonexistent original section");
        }

        coordinate += U32(sections, ((segment - 1) * 40) + 12);
        return coordinate <= uint.MaxValue ? (false, (uint)coordinate) : throw Invalid("an image-relative symbol coordinate wraps");
    }

    /// <summary>Reads only the DBI substream index, section table, optional OMAP and global symbol records.</summary>
    /// <remarks>
    /// A PDB without a symbol-record stream (DBI slot 0xFFFF, as in a stripped or types-only file) has no symbols;
    /// its type information remains available. Public and global records must agree on one address per name.
    /// File-local data (<c>S_LDATA32</c>) can legitimately repeat a name across compilation units, so such a
    /// name is exported only when it has one coordinate and no public or global record contradicts it.
    /// </remarks>
    /// <param name="file">Validated borrowed MSF file.</param>
    /// <param name="debug">Original fixed DBI header.</param>
    /// <param name="cancellationToken">Checked between symbol and remapping records.</param>
    /// <exception cref="Diagnostics.CStructLayoutException">A coordinate, substream or record is inconsistent.</exception>
    private void ReadSymbols(PdbMsfFile file, ReadOnlySpan<byte> debug, CancellationToken cancellationToken)
    {
        int symbolIndex = U16(debug, 20);
        if (symbolIndex == ushort.MaxValue)
        {
            return;
        }

        ulong optionalOffset = 64;
        ReadOnlySpan<int> substreamSizes = [24, 28, 32, 36, 40, 52];
        foreach (int offset in substreamSizes)
        {
            uint size = U32(debug, offset);
            if (size > int.MaxValue)
            {
                throw Invalid("a DBI substream has a negative length");
            }

            optionalOffset += size;
        }

        uint optionalLength = U32(debug, 48);
        if (optionalLength > int.MaxValue || (optionalLength & 1) != 0
            || optionalOffset + optionalLength > file.StreamLength(3))
        {
            throw Invalid("the DBI substreams extend beyond their original stream");
        }

        uint sectionMapLength = U32(debug, 32);
        ulong sectionMapOffset = 64UL + U32(debug, 24) + U32(debug, 28);
        byte[] sectionMap = file.ReadStreamRange(3, (uint)sectionMapOffset, (int)sectionMapLength);
        if (sectionMap.Length != 0
            && (sectionMap.Length < 4 || 4UL + (20UL * U16(sectionMap, 0)) != (ulong)sectionMap.Length
                || U16(sectionMap, 2) > U16(sectionMap, 0)))
        {
            throw Invalid("the original DBI section-map table is inconsistent");
        }

        byte[] optional = file.ReadStreamRange(3, (uint)optionalOffset, (int)optionalLength);
        int omapIndex = OptionalStream(optional, 4);
        int sectionIndex = OptionalStream(optional, omapIndex >= 0 ? 10 : 5);
        if (sectionIndex < 0)
        {
            throw Invalid("original section headers required to resolve symbol coordinates are absent");
        }

        byte[] sections = file.ReadStream(sectionIndex);
        if (sections.Length == 0 || sections.Length % 40 != 0 || sections.Length / 40 > 65_535)
        {
            throw Invalid("the original section-header table is truncated or oversized");
        }

        byte[] mapping = omapIndex >= 0 ? file.ReadStream(omapIndex) : [];
        if (mapping.Length / 8 > this.maximumRecords)
        {
            throw Invalid("the original OMAP table exceeds its record budget");
        }

        ValidateOmap(mapping, cancellationToken);
        byte[] bytes = file.ReadStream(symbolIndex);
        int cursor = 0;
        int records = 0;
        while (cursor < bytes.Length)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (++records > this.maximumRecords || bytes.Length - cursor < 4)
            {
                throw Invalid("the symbol table is truncated or exceeds its record budget");
            }

            int length = U16(bytes, cursor);
            ushort kind = U16(bytes, cursor + 2);
            if (length < 2 || length > bytes.Length - cursor - 2)
            {
                throw Invalid("a symbol record extends beyond its original stream");
            }

            this.symbolRecordKinds[kind] = this.symbolRecordKinds.GetValueOrDefault(kind) + 1;
            if (kind is 0x110C or 0x110D or 0x110E)
            {
                var reader = new PdbRecordReader(bytes.AsSpan(cursor + 4, length - 2));
                uint flagsOrType = reader.UInt32();
                uint offset = reader.UInt32();
                ushort segment = reader.UInt16();
                string name = reader.Name();
                reader.EndSymbol();
                (bool absolute, uint original) = SectionCoordinate(segment, offset, sectionMap, sections);
                if (absolute)
                {
                    if (this.absoluteSymbols.TryGetValue(name, out ulong previous) && previous != original)
                    {
                        throw Invalid($"absolute symbol '{name}' has conflicting original values");
                    }

                    this.absoluteSymbols[name] = original;
                    this.unmappedSymbols.Add(name);
                }
                else
                {
                    uint? mapped = Map(original, mapping);
                    if (mapped is not { } address)
                    {
                        this.unmappedSymbols.Add(name);
                    }
                    else if (kind == 0x110C)
                    {
                        this.AddLocalSymbol(name, address, flagsOrType);
                    }
                    else
                    {
                        this.AddSymbol(name, address, kind == 0x110E ? null : flagsOrType);
                    }
                }
            }

            cursor += length + 2;
        }

        this.MergeLocalSymbols();
    }

    /// <summary>Records one file-local data coordinate, marking the name ambiguous when another coordinate or type disagrees.</summary>
    /// <param name="name">Original undecorated local name.</param>
    /// <param name="address">Resolved image-relative coordinate.</param>
    /// <param name="type">Original storage type.</param>
    private void AddLocalSymbol(string name, uint address, uint type)
    {
        if (!this.localSymbols.TryGetValue(name, out (uint? Address, uint Type) previous))
        {
            this.localSymbols.Add(name, (address, type));
        }
        else if (previous.Address is { } known && (known != address || previous.Type != type))
        {
            this.localSymbols[name] = (null, 0);
        }
    }

    /// <summary>Publishes unambiguous file-local names after every public and global record has been seen.</summary>
    /// <remarks>Merging last makes the outcome independent of record order: a global always wins over a local.</remarks>
    private void MergeLocalSymbols()
    {
        foreach ((string name, (uint? address, uint type)) in this.localSymbols.OrderBy(entry => entry.Key, StringComparer.Ordinal))
        {
            if (address is not { } local)
            {
                this.resolutionDiagnostics.Add($"File-local symbol '{name}' has several original coordinates or storage types; none of its file-local records is exported.");
            }
            else if (this.symbols.TryGetValue(name, out ulong global))
            {
                if (global != local)
                {
                    this.resolutionDiagnostics.Add($"File-local symbol '{name}' differs from the public or global symbol of that name; only the public or global address is exported.");
                }
                else
                {
                    // A public record carries no type; an agreeing local record can still supply the storage type.
                    this.symbolTypes.TryAdd(name, type);
                }
            }
            else
            {
                this.symbols.Add(name, local);
                this.symbolTypes.Add(name, type);
            }
        }
    }

    /// <summary>Preserves equal-address aliases while rejecting contradictory definitions with one lookup name.</summary>
    /// <param name="name">Complete original symbol name.</param>
    /// <param name="address">Resolved original image-relative coordinate.</param>
    /// <param name="type">Original storage type, if the source recorded one.</param>
    /// <exception cref="Diagnostics.CStructLayoutException">One name has conflicting coordinates or storage identities.</exception>
    private void AddSymbol(string name, uint address, uint? type)
    {
        if (this.symbols.TryGetValue(name, out ulong previous) && previous != address)
        {
            throw Invalid($"symbol '{name}' has conflicting original coordinates");
        }

        this.symbols[name] = address;
        if (type is { } index)
        {
            if (this.symbolTypes.TryGetValue(name, out uint previousType) && previousType != index)
            {
                throw Invalid($"symbol '{name}' has conflicting original storage types");
            }

            this.symbolTypes[name] = index;
        }
    }
}
