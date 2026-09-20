namespace CStructSharp.Codecs;

/// <summary>
///     The LEB128 decoding rule, one byte at a time, so a stream reader (which must stop at the terminating byte)
///     and a span reader share it: payload bits accumulate seven at a time, the final byte may not carry bits past
///     the declared width, and a signed value is sign-extended from its last payload bit.
/// </summary>
internal struct Leb128Decoder
{
    public const string Unterminated = "Unterminated LEB128 integer.";
    public const string ExceedsWidth = "LEB128 integer exceeds its declared width.";
    public static readonly string ShortRead = Diagnostics.ReadFailures.ShortRead(1, 0);

    private readonly int width;
    private readonly bool signed;
    private ulong value;
    private int shift;

    public Leb128Decoder(int width, bool signed)
    {
        this.width = width;
        this.signed = signed;
    }

    /// <summary>Consumes one encoded byte; returns <see langword="true"/> with the value when it was the last one.</summary>
    /// <exception cref="Diagnostics.CStructReadException">The integer runs past its width.</exception>
    public bool Push(byte octet, out ulong result)
    {
        if (this.shift >= this.width)
        {
            throw new Diagnostics.CStructReadException(Unterminated);
        }

        int payload = octet & 127;
        int remaining = this.width - this.shift;
        if (remaining < 7)
        {
            int positiveLimit = 1 << (this.signed ? remaining - 1 : remaining);
            bool valid = payload < positiveLimit || (this.signed && payload >= 128 - positiveLimit);
            if (!valid || (octet & 128) != 0)
            {
                throw new Diagnostics.CStructReadException(ExceedsWidth);
            }
        }

        this.value |= (ulong)payload << this.shift;
        if ((octet & 128) == 0)
        {
            if (this.signed && (octet & 64) != 0 && this.shift + 7 < 64)
            {
                this.value |= ulong.MaxValue << (this.shift + 7);
            }

            result = this.value;
            return true;
        }

        this.shift += 7;
        result = 0;
        return false;
    }
}
