namespace CStructSharp.Memory;

/// <summary>One physical piece of a patch: a final-source range, the bytes expected there, the bytes to write, and the generation seen while planning.</summary>
/// <remarks>
/// A logical field that crosses a mapping boundary becomes several fragments, one per backing range, while the
/// parent <see cref="MemoryPatch"/> keeps the logical selection. <see cref="Expected"/> and
/// <see cref="Generation"/> let commit detect that the image changed since planning; <see cref="Replacement"/> is
/// the proposed content. The public byte properties return copies so a preview user interface cannot alter a
/// prepared write by editing an array it was shown. These checks are repeated at commit, but they cannot prevent a
/// mutation that happens after validation on a source without external synchronization.
/// </remarks>
public sealed class MemoryPatchFragment
{
    /// <summary>Copies both byte sequences and records the generation observed while planning.</summary>
    /// <param name="region">Final-source range this fragment writes.</param>
    /// <param name="expected">Bytes read from that range while planning.</param>
    /// <param name="replacement">Bytes to write there.</param>
    /// <param name="generation">Source generation observed while the expected bytes were read.</param>
    internal MemoryPatchFragment(MemoryRegion region, byte[] expected, byte[] replacement, long generation)
    {
        this.Region = region;
        this.ExpectedBytes = (byte[])expected.Clone();
        this.ReplacementBytes = (byte[])replacement.Clone();
        this.Generation = generation;
    }

    /// <summary>Gets the expected bytes without copying, for the library's own comparisons.</summary>
    internal byte[] ExpectedBytes { get; }

    /// <summary>Gets the replacement bytes without copying, for the library's own writes.</summary>
    internal byte[] ReplacementBytes { get; }

    /// <summary>Gets the final-source range this fragment writes.</summary>
    public MemoryRegion Region { get; }

    /// <summary>Gets the source generation observed while planning; zero means the source relies on external snapshot consistency.</summary>
    public long Generation { get; }

    /// <summary>Gets a copy of the bytes that must still be present at commit.</summary>
    public byte[] Expected => (byte[])this.ExpectedBytes.Clone();

    /// <summary>Gets a copy of the bytes to write.</summary>
    public byte[] Replacement => (byte[])this.ReplacementBytes.Clone();
}
