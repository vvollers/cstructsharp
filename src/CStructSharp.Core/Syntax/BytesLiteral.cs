namespace CStructSharp.Syntax;

using System;

/// <summary>
///     A quoted byte string written in a constraint condition (<c>"NTFS    "</c> or <c>b"\x7fELF"</c>). It is not a number: the
///     parser accepts it only as the operand of <c>==</c> or <c>!=</c> next to a field name and turns the pair into a
///     <see cref="ByteEquals"/>, so no other part of the compiler ever sees one.
/// </summary>
internal sealed class BytesLiteral : Expr
{
    /// <summary>The most bytes a byte string may hold; the comparison's slot name spells them in hexadecimal.</summary>
    public const int MaximumLength = 256;

    /// <summary>Creates a byte string.</summary>
    /// <param name="bytes">The bytes, one per character of the quoted text after escapes were applied.</param>
    public BytesLiteral(byte[] bytes)
    {
        this.Bytes = bytes;
    }

    /// <summary>Gets the bytes; the array is shared and must not be modified.</summary>
    public byte[] Bytes { get; }

    /// <summary>The bytes as uppercase hexadecimal digits, two per byte.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The digits.</returns>
    public static string ToHex(byte[] bytes)
    {
        const string Digits = "0123456789ABCDEF";
        var text = new char[bytes.Length * 2];
        for (int index = 0; index < bytes.Length; index++)
        {
            text[index * 2] = Digits[bytes[index] >> 4];
            text[(index * 2) + 1] = Digits[bytes[index] & 0xF];
        }

        return new string(text);
    }

    /// <summary>A hash of the bytes' values, equal for equal contents.</summary>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The hash.</returns>
    public static int ContentHash(byte[] bytes)
    {
        int hash = bytes.Length;
        foreach (byte value in bytes)
        {
            hash = unchecked((hash * 31) + value);
        }

        return hash;
    }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    /// <param name="other">The expression to compare with this one.</param>
    /// <returns>True when <paramref name="other"/> is a byte string with the same bytes.</returns>
    public override bool Equals(Expr? other) => other is BytesLiteral literal && this.Bytes.AsSpan().SequenceEqual(literal.Bytes);

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    /// <returns>A hash of the bytes.</returns>
    public override int GetHashCode()
    {
        return ContentHash(this.Bytes);
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    /// <returns>The bytes in hexadecimal.</returns>
    public override string ToString() => "Bytes: " + ToHex(this.Bytes);
}
