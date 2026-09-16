namespace CStructSharp;

/// <summary>Which end of a storage unit the first bitfield takes.</summary>
public enum BitfieldAllocation
{
    /// <summary>The first field takes the lowest bits (bit 0 up); Portable's default and what every little-endian ABI does.</summary>
    LowBitFirst,

    /// <summary>
    ///     The first field takes the highest bits (the most significant bit down): the reading of big-endian ABIs,
    ///     RFC packet diagrams, and dissect.cstruct's big-endian mode.
    /// </summary>
    HighBitFirst,
}
