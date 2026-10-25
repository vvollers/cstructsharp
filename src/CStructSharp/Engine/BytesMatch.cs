namespace CStructSharp.Engine;

using System;
using System.Collections;

/// <summary>
///     The comparison of an array member the engine has stored with a byte string, for a constraint such as
///     <c>oem == "NTFS    "</c>. The member is a <c>uint8</c> array (a list of boxed bytes) or a <c>char</c> array (a string of
///     one character per byte); a text member compares as if it were not trimmed, so the options of the read
///     (<c>TrimFixedText</c>) do not change the answer.
/// </summary>
internal static class BytesMatch
{
    /// <summary>Tells whether a stored array member holds exactly the bytes of a byte string.</summary>
    /// <param name="stored">The member value the read stored: a list of elements, or a string.</param>
    /// <param name="expected">The byte string, as long as the array.</param>
    /// <returns><see langword="true"/> when the member's bytes equal <paramref name="expected"/>.</returns>
    public static bool Equal(object? stored, byte[] expected)
    {
        switch (stored)
        {
        case string text:
            {
                // Trailing NULs are the only characters a trimmed read can drop, so the text equals the string when what is
                // left is a prefix of it and the rest of the string is NULs.
                if (text.Length > expected.Length)
                {
                    return false;
                }

                for (int index = 0; index < expected.Length; index++)
                {
                    if (index < text.Length ? text[index] != expected[index] : expected[index] != 0)
                    {
                        return false;
                    }
                }

                return true;
            }

        case IList list:
            {
                if (list.Count != expected.Length)
                {
                    return false;
                }

                for (int index = 0; index < expected.Length; index++)
                {
                    if (list[index] is not byte value || value != expected[index])
                    {
                        return false;
                    }
                }

                return true;
            }

        default:
            return false;
        }
    }
}
