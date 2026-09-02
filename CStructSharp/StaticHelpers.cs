namespace CStructSharp;

using System;
using System.Globalization;
using System.Text;

/// <summary>Contains small conversion helpers shared by examples, tests, and callers.</summary>
public static class StaticHelpers
{
    /// <summary>
    ///     Converts readable hexadecimal text into bytes for examples and tests.
    ///     Whitespace is accepted as a separator; all other non-hexadecimal characters and odd digit counts are rejected.
    /// </summary>
    /// <param name="hexData">Hexadecimal byte pairs with optional whitespace separators.</param>
    /// <returns>A new array containing the decoded bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="hexData"/> is <see langword="null"/>.</exception>
    /// <exception cref="FormatException">The input contains a non-hexadecimal character or an incomplete byte.</exception>
    public static byte[] ParseHexDataContent(this string hexData)
    {
        if (hexData is null)
        {
            throw new ArgumentNullException(nameof(hexData));
        }

        // Keep formatted dumps convenient without silently deleting punctuation or accidental prose from fixture data.
        var digits = new StringBuilder(hexData.Length);
        for (int i = 0; i < hexData.Length; i++)
        {
            char character = hexData[i];
            if (char.IsWhiteSpace(character))
            {
                continue;
            }

            if (!Uri.IsHexDigit(character))
            {
                throw new FormatException("Invalid hexadecimal character at position " + i + ".");
            }

            digits.Append(character);
        }

        if (digits.Length % 2 != 0)
        {
            throw new FormatException("Hexadecimal input must contain a whole number of bytes.");
        }

        // Two hexadecimal characters form one byte, so allocate the exact result size before converting pairs.
        byte[] data = new byte[digits.Length / 2];
        for (int i = 0; i < data.Length; i++)
        {
            // Each pair starts at twice the output index and is parsed using hexadecimal rather than decimal rules.
            data[i] = byte.Parse(digits.ToString(i * 2, 2), NumberStyles.HexNumber);
        }

        return data;
    }
}
