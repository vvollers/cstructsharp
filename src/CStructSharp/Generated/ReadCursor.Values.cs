namespace CStructSharp.Generated;

using System;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;

/// <summary>Consuming single values: fixed-size composites, byte runs, caller codecs, identifiers and LEB128 integers.</summary>
public ref partial struct ReadCursor
{
    /// <summary>
    ///     Consumes <paramref name="count"/> bytes: checks the total read budget, checks that the bytes exist, and
    ///     advances. A short read moves the cursor to the end of the input (where a short read of a stream also
    ///     leaves it), and its message states how many bytes the item needed and how many were left.
    /// </summary>
    /// <param name="count">The number of bytes the item occupies.</param>
    /// <param name="member">The layout field being read, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The consumed bytes.</returns>
    /// <exception cref="CStructReadLimitException">The total read budget is exceeded.</exception>
    /// <exception cref="CStructReadException">Fewer than <paramref name="count"/> bytes remain.</exception>
    public ReadOnlySpan<byte> Take(int count, string member, string? memberType)
    {
        this.Charge(count, member, memberType);
        if (count < 0 || count > this.Remaining)
        {
            throw this.ShortRead(count, member, memberType);
        }

        ReadOnlySpan<byte> bytes = this.source.Slice(this.position, count);
        this.position += count;
        return bytes;
    }

    /// <summary>
    ///     Consumes a whole fixed-layout struct in one step for the generated fixed reader, but only when the
    ///     member-by-member reader would read exactly these bytes without a failure: the token is not cancelled, the
    ///     struct's bytes are present, the budget covers the bytes that reader charges, the nesting and array limits
    ///     hold, and the start meets the struct's alignment. Otherwise nothing changes and the caller reads member by
    ///     member, which reports any failure at the member where it always has.
    /// </summary>
    /// <param name="size">The struct's storage size in bytes, tail padding included.</param>
    /// <param name="alignment">The alignment the start must meet (1 in a packed layout).</param>
    /// <param name="chargedBytes">The bytes the member-by-member reader charges to the read budget (members, not padding).</param>
    /// <param name="nestingLevels">The struct levels the member-by-member reader would enter, the struct itself included.</param>
    /// <param name="maximumArrayCount">The largest fixed array count inside the struct.</param>
    /// <param name="bytes">The struct's bytes when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the bytes were consumed and charged.</returns>
    public bool TryTakeFixed(int size, int alignment, long chargedBytes, int nestingLevels, int maximumArrayCount, out ReadOnlySpan<byte> bytes)
    {
        if (this.settings.CancellationToken.IsCancellationRequested || size < 0 || size > this.Remaining ||
            chargedBytes > this.settings.MaxTotalBytesRead - this.bytesRead ||
            this.nestingDepth + nestingLevels > this.settings.MaxNestingDepth || maximumArrayCount > this.settings.MaxArrayElements ||
            (alignment > 1 && this.position % alignment != 0))
        {
            bytes = default;
            return false;
        }

        bytes = this.source.Slice(this.position, size);
        this.position += size;
        this.bytesRead += chargedBytes;
        return true;
    }

    /// <summary>
    ///     Reads one value of a caller-supplied <see cref="ICustomCodec"/> as the runtime's memory path does: the codec
    ///     sees every remaining byte, the cursor moves past what it consumed (past the whole input on a short read),
    ///     those bytes are charged to the budget, and a rejected or short read fails with the runtime's texts.
    /// </summary>
    /// <param name="codec">The codec, one of the instances the layout class provides.</param>
    /// <param name="member">The field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The decoded value.</returns>
    /// <exception cref="CStructReadException">The codec needs more bytes, rejected the input, threw, or reported an impossible length.</exception>
    /// <exception cref="CStructReadLimitException">The consumed bytes exceed the read budget.</exception>
    public object TakeCustom(ICustomCodec codec, string member, string? memberType)
    {
        ArgumentNullException.ThrowIfNull(codec);
        CStructReadException? failure;
        object? value;
        int consumed;
        try
        {
            failure = CustomCodecAdapter.DecodeFromMemory(codec, this.source.Slice(this.position), out value, out consumed);
        }
        catch (CStructReadException exception)
        {
            // The codec threw: the adapter wrapped it; nothing was consumed.
            this.Attach(exception, member, memberType);
            throw;
        }

        this.position += consumed;
        this.Charge(consumed, member, memberType);
        if (failure is not null)
        {
            this.Attach(failure, member, memberType);
            throw failure;
        }

        return value!;
    }

    /// <summary>Reads a 16-byte identifier (<c>uuid</c> in network order, <c>guid</c> in Windows order) with the runtime's short-read text.</summary>
    /// <param name="networkOrder">Whether the identifier is stored in network (big-endian) order.</param>
    /// <param name="member">The identifier field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The identifier.</returns>
    public Guid TakeGuid(bool networkOrder, string member, string? memberType)
    {
        if (this.Remaining < 16)
        {
            this.Charge(this.Remaining, member, memberType);
            this.position = this.source.Length;
            throw this.Fail(ReadFailures.IdentifierShortRead, member, memberType);
        }

        return Codec.ReadGuid(this.Take(16, member, memberType), networkOrder);
    }

    /// <summary>Reads one LEB128 integer byte by byte, as the runtime does, so a short read reports the single byte it needed.</summary>
    /// <param name="width">The payload width in bits (32 or 64).</param>
    /// <param name="signed">Whether the encoding is SLEB128.</param>
    /// <param name="member">The field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The decoded bits (sign-extended for a signed encoding).</returns>
    public ulong TakeLeb128(int width, bool signed, string member, string? memberType)
    {
        var decoder = new Codecs.Leb128Decoder(width, signed);
        while (true)
        {
            byte octet = this.Take(1, member, memberType)[0];
            try
            {
                if (decoder.Push(octet, out ulong value))
                {
                    return value;
                }
            }
            catch (CStructReadException exception)
            {
                throw this.Fail(exception.Message, member, memberType);
            }
        }
    }
}
