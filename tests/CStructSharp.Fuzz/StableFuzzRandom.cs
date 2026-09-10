namespace CStructSharp.Fuzzing;

/// <summary>Provides a small fixed mutation PRNG whose replay does not depend on a runtime implementation.</summary>
internal sealed class StableFuzzRandom
{
    private ulong state;

    public StableFuzzRandom(ulong seed)
    {
        this.state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed;
    }

    public int NextInt(int exclusiveMaximum)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(exclusiveMaximum);
        return (int)(this.NextUInt64() % (uint)exclusiveMaximum);
    }

    public byte NextByte()
    {
        return (byte)this.NextUInt64();
    }

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
