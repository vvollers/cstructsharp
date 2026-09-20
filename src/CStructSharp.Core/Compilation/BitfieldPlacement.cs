namespace CStructSharp.Compilation;

using System;
using CStructSharp.Diagnostics;

/// <summary>
///     The one bitfield placement rule, shared by the compile-time offsets, the runtime cursor, and therefore
///     every operation: given the byte position a composite has reached and the run of bitfields placed so far,
///     it decides where the next bitfield's bits go and which storage unit holds them.
/// </summary>
internal struct BitfieldPlacement
{
    private readonly BitfieldPacking packing;
    private readonly bool aligned;
    private readonly bool highBitFirst;

    // A run of adjacent bitfields. The cursor that owns this tracker is allocated per composite per operation, so the
    // two rules share storage: SysV tracks the absolute bit position, the run's first byte, its compiled bit length,
    // and the furthest whole cell; MSVC tracks the open unit's start, size, and bits used.
    private bool runActive;
    private long bitPosition;
    private long unitStart;
    private int unitSize;
    private long cellEnd;

    public BitfieldPlacement(BitfieldPacking packing, bool aligned, bool highBitFirst)
    {
        this.packing = packing;
        this.aligned = aligned;
        this.highBitFirst = highBitFirst;
    }

    private long RunStart
    {
        readonly get => this.unitStart;
        set => this.unitStart = value;
    }

    private int RunBits
    {
        readonly get => this.unitSize;
        set => this.unitSize = value;
    }

    private int BitsUsed
    {
        readonly get => (int)this.bitPosition;
        set => this.bitPosition = value;
    }

    /// <summary>Whether the last placed field was a bitfield whose run the next bitfield may continue.</summary>
    public readonly bool InRun => this.runActive;

    /// <summary>The byte position after the storage placed so far: where the next ordinary field starts (before its own alignment).</summary>
    public readonly long RunEnd => this.packing == BitfieldPacking.Msvc
                                       ? this.unitStart + this.unitSize
                                       : Math.Max((this.bitPosition + 7) / 8, this.cellEnd);

    /// <summary>An ordinary field or a composite boundary ends the run.</summary>
    public void Close()
    {
        this.runActive = false;
        this.bitPosition = 0;
        this.unitSize = 0;
        this.cellEnd = 0;
    }

    /// <summary>
    ///     Places one bitfield. <paramref name="current"/> is the composite's byte position when no run is active
    ///     (the unit of the first field of a run starts there, aligned when placement is aligned);
    ///     <paramref name="runBits"/> is the compiled bit length of the run the field belongs to (packed SysV
    ///     placement clamps units to it).
    /// </summary>
    /// <returns>The unit's start, its size in bytes, and the field's bit offset inside it.</returns>
    public (long UnitStart, int UnitSize, int BitOffset) Place(long current, int declaredSize, int alignment, int width, int runBits, bool littleEndian, string name)
    {
        if (this.packing == BitfieldPacking.Msvc)
        {
            return this.PlaceMsvc(current, declaredSize, alignment, width);
        }

        return this.PlaceSysV(current, declaredSize, width, runBits, littleEndian == !this.highBitFirst, name);
    }

    /// <summary>A zero-width bitfield: no storage, but the next bitfield starts on a boundary of the declared type.</summary>
    public void PlaceSeparator(long current, int declaredSize, int alignment, int runBits)
    {
        if (this.packing == BitfieldPacking.Msvc)
        {
            // Close the unit; the next unit opens at the byte after it, aligned to the separator's type.
            long next = this.runActive ? this.unitStart + this.unitSize : current;
            this.Close();
            this.runActive = true;
            this.unitStart = this.aligned ? LayoutMath.AlignUp(next, alignment) : next;
            this.unitSize = 0;
            return;
        }

        if (!this.runActive)
        {
            this.RunStart = current;
            this.RunBits = runBits;
            this.bitPosition = current * 8;
            this.cellEnd = 0;
        }

        this.bitPosition = Math.Max(LayoutMath.AlignUp(this.bitPosition, declaredSize * 8L), this.cellEnd * 8);
        this.runActive = true;
    }

    private (long UnitStart, int UnitSize, int BitOffset) PlaceMsvc(long current, int declaredSize, int alignment, int width)
    {
        bool startsNewUnit = !this.runActive || this.unitSize != declaredSize || this.BitsUsed + width > declaredSize * 8;
        if (startsNewUnit)
        {
            long next = this.runActive ? this.unitStart + this.unitSize : current;
            this.unitStart = this.aligned ? LayoutMath.AlignUp(next, alignment) : next;
            this.unitSize = declaredSize;
            this.BitsUsed = 0;
            this.runActive = true;
        }

        int bitOffset = this.BitsUsed;
        this.BitsUsed += width;
        return (this.unitStart, this.unitSize, bitOffset);
    }

    /// <summary>
    ///     SysV placement computes bit positions the way GCC does; <paramref name="forward"/> says whether those bits
    ///     fill a unit from its first byte (little-endian storage with low-bit-first numbering, or big-endian with
    ///     high-bit-first - the two combinations real compilers produce). When they fill it from the last byte
    ///     instead, a unit is always a whole declared cell and a field that would cross its cell moves to the next.
    /// </summary>
    private (long UnitStart, int UnitSize, int BitOffset) PlaceSysV(long current, int declaredSize, int width, int runBits, bool forward, string name)
    {
        if (!this.runActive)
        {
            this.RunStart = current;
            this.RunBits = runBits;
            this.bitPosition = current * 8;
            this.cellEnd = 0;
        }

        long position = this.bitPosition;
        long cellBits = declaredSize * 8L;
        if (this.aligned || !forward)
        {
            // Cells of the declared size: type-aligned when placement is aligned, anchored at the run start when
            // packed. The field moves to the next cell when its bits would cross the cell's end; the cell is the unit.
            long anchor = this.aligned ? 0 : this.RunStart * 8;
            long relative = position - anchor;
            if (relative / cellBits != (relative + width - 1) / cellBits)
            {
                relative = LayoutMath.AlignUp(relative, cellBits);
                position = anchor + relative;
            }

            long cellStart = (anchor / 8) + ((relative / cellBits) * declaredSize);
            this.bitPosition = position + width;
            this.runActive = true;
            if (!forward)
            {
                // Bits fill the cell from its last byte, so the whole cell is in use before anything can follow.
                this.cellEnd = Math.Max(this.cellEnd, cellStart + declaredSize);
            }

            return (cellStart, declaredSize, (int)(position - (cellStart * 8)));
        }

        // Packed, forward-filling placement never moves a field: bits are contiguous from the run's first byte. The
        // storage unit is the declared-size cell anchored at the run start that holds the bits, clamped to the run's
        // own bytes so it never reaches past the data; a field that straddles two cells gets the smallest byte window.
        long offsetInRun = position - (this.RunStart * 8);
        long runEnd = this.RunStart + ((this.RunBits + 7) / 8);
        long windowStart;
        long windowEnd;
        if (offsetInRun / cellBits != (offsetInRun + width - 1) / cellBits)
        {
            windowStart = position / 8;
            windowEnd = (position + width + 7) / 8;
        }
        else
        {
            windowStart = this.RunStart + ((offsetInRun / cellBits) * declaredSize);
            windowEnd = Math.Min(windowStart + declaredSize, runEnd);
        }

        if (windowEnd - windowStart > 8)
        {
            throw new CStructLayoutException(
                $"Bitfield '{name}' ({width} bits at bit offset {position % 8}) would span {windowEnd - windowStart} bytes in packed SysV placement; at most 8 are supported. Use aligned placement, BitfieldPacking.Msvc, or a zero-width separator before it.");
        }

        this.bitPosition = position + width;
        this.runActive = true;
        return (windowStart, (int)(windowEnd - windowStart), (int)(position - (windowStart * 8)));
    }
}
