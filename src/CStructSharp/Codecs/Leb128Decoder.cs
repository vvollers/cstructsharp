namespace CStructSharp.Codecs;

/// <summary>
///     The LEB128 decoding rule, one byte at a time, so a stream reader (which must stop at the terminating byte)
///     and a span reader share it: payload bits accumulate seven at a time, the final byte may not carry bits past
///     the declared width, and a signed value is sign-extended from its last payload bit.
/// </summary>
internal struct Leb128Decoder
{
    /// <summary>
    ///     The read failure message for an integer whose bytes keep setting the continuation bit past its width.
    /// </summary>
    public const string Unterminated = "Unterminated LEB128 integer.";

    /// <summary>
    ///     The read failure message for a final byte that carries payload bits beyond the declared width.
    /// </summary>
    public const string ExceedsWidth = "LEB128 integer exceeds its declared width.";

    /// <summary>The read failure message for input that ends before the integer's terminating byte.</summary>
    public static readonly string ShortRead = Diagnostics.ReadFailures.ShortRead(1, 0);

    private readonly int width;
    private readonly bool signed;
    private ulong value;
    private int shift;

    /// <summary>Starts decoding one integer with no bytes consumed.</summary>
    /// <param name="width">The declared integer width in bits: 32 or 64.</param>
    /// <param name="signed">Whether the value is signed LEB128 and is sign-extended from its last payload bit.</param>
    public Leb128Decoder(int width, bool signed)
    {
        this.width = width;
        this.signed = signed;
    }

    /// <summary>Consumes one encoded byte; returns <see langword="true"/> with the value when it was the last one.</summary>
    /// <param name="octet">The next encoded byte: seven payload bits and the continuation bit (bit 7).</param>
    /// <param name="result">
    ///     The decoded value as raw 64-bit two's-complement bits when the method returns <see langword="true"/>;
    ///     otherwise 0.
    /// </param>
    /// <returns><see langword="true"/> when <paramref name="octet"/> was the final byte of the integer.</returns>
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
