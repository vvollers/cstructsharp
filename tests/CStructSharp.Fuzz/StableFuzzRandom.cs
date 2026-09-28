namespace CStructSharp.Fuzzing;

/// <summary>Provides a small fixed mutation PRNG whose replay does not depend on a runtime implementation.</summary>
internal sealed class StableFuzzRandom
{
    private ulong state;

    /// <summary>Creates a generator whose sequence is fixed by the seed.</summary>
    /// <param name="seed">The seed; zero becomes a fixed non-zero constant, because a zero state never changes.</param>
    public StableFuzzRandom(ulong seed)
    {
        this.state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
    }

    /// <summary>Returns the next integer in the range [0, <paramref name="exclusiveMaximum"/>).</summary>
    /// <param name="exclusiveMaximum">The exclusive upper bound; must be positive.</param>
    /// <returns>The next value, reduced modulo the bound.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The bound is zero or negative.</exception>
    public int NextInt(int exclusiveMaximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMaximum);
        return (int)(this.NextUInt64() % (uint)exclusiveMaximum);
    }

    /// <summary>Returns the low byte of the next 64-bit value.</summary>
    /// <returns>A byte from the sequence.</returns>
    public byte NextByte()
    {
        return (byte)this.NextUInt64();
    }

    /// <summary>Advances the xorshift64* state and returns its scrambled output.</summary>
    /// <returns>The next 64-bit value.</returns>
    private ulong NextUInt64()
    {
        ulong value = this.state;
        value ^= value >> 12;
        value ^= value << 25;
        value ^= value >> 27;
        this.state = value;
        return value * 0x2545F4914F6CDD1DUL;
    }
}
