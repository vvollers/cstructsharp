namespace CStructSharp;

using System;
using System.Collections.Frozen;
using System.Collections.Generic;

/// <summary>
///     The one table of accepted primitive spellings that are aliases of a canonical codec name: C, C99, Windows SDK,
///     Linux kernel, IDA, and dissect spellings. Every other view - the byte-order primitive registries, the bitfield
///     storage table, enum backing resolution, and <see cref="PrimitiveCodec.Resolve"/> - is derived from it, so a
///     spelling added here is accepted everywhere at once.
/// </summary>
/// <remarks>
///     An alias resolves to its canonical codec at compile time exactly like a user <c>typedef</c> does: the compiled
///     field's terminal name is the canonical spelling, so no hot path ever sees an alias. The <c>long</c> family is
///     listed separately because <see cref="CStructCompilationOptions.CLongWidth"/> selects its width per layout
///     (Portable's default is 64 bits; dissect.cstruct and ILP32/LLP64 headers mean 32).
/// </remarks>
internal static class PrimitiveSpellings
{
    /// <summary>Spellings of the C <c>long</c> family whose width depends on <see cref="CStructCompilationOptions.CLongWidth"/>.</summary>
    public static readonly FrozenDictionary<string, bool> LongFamilyIsUnsigned = new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        ["long"] = false,
        ["long int"] = false,
        ["signed long"] = false,
        ["signed long int"] = false,
        ["time_t"] = false,
        ["off_t"] = false,
        ["ulong"] = true,
        ["unsigned long"] = true,
        ["unsigned long int"] = true,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Spellings whose width is the layout's pointer width (<c>size_t</c> and friends), keyed to their signedness.</summary>
    public static readonly FrozenDictionary<string, bool> PointerSizedIsUnsigned = new Dictionary<string, bool>(StringComparer.Ordinal)
    {
        ["size_t"] = true,
        ["uintptr_t"] = true,
        ["SIZE_T"] = true,
        ["ULONG_PTR"] = true,
        ["UINT_PTR"] = true,
        ["DWORD_PTR"] = true,
        ["ssize_t"] = false,
        ["intptr_t"] = false,
        ["ptrdiff_t"] = false,
        ["SSIZE_T"] = false,
        ["LONG_PTR"] = false,
        ["INT_PTR"] = false,
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    ///     Spellings that are a pointer of the layout's pointer width, keyed to what they point at: <c>void</c> for an
    ///     opaque address, or the character type of a Windows string pointer.
    /// </summary>
    public static readonly FrozenDictionary<string, string> PointerSpellings = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["PVOID"] = "void",
        ["LPVOID"] = "void",
        ["LPCVOID"] = "void",
        ["HANDLE"] = "void",
        ["PSTR"] = "char",
        ["LPSTR"] = "char",
        ["PCSTR"] = "char",
        ["LPCSTR"] = "char",
        ["PWSTR"] = "wchar",
        ["LPWSTR"] = "wchar",
        ["PCWSTR"] = "wchar",
        ["LPCWSTR"] = "wchar",
    }.ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>Alias spelling to canonical registry key, excluding the <c>long</c> and pointer-sized families.</summary>
    public static readonly FrozenDictionary<string, string> Aliases = BuildAliases().ToFrozenDictionary(StringComparer.Ordinal);

    /// <summary>
    ///     Returns the canonical registry key for an alias, the <c>long</c> family member for the requested width, the
    ///     pointer-sized family member for the requested pointer width, or the spelling itself.
    /// </summary>
    public static string Canonicalize(string spelling, int cLongWidth, int pointerSize = 8)
    {
        if (Aliases.TryGetValue(spelling, out string? canonical))
        {
            return canonical;
        }

        if (LongFamilyIsUnsigned.TryGetValue(spelling, out bool isUnsigned))
        {
            return LongCanonical(isUnsigned, cLongWidth);
        }

        if (PointerSizedIsUnsigned.TryGetValue(spelling, out bool pointerUnsigned))
        {
            return PointerSizedCanonical(pointerUnsigned, pointerSize);
        }

        return spelling;
    }

    /// <summary>Whether a spelling is an alias (and may therefore be shadowed by a user declaration of the same name).</summary>
    public static bool IsAlias(string spelling)
    {
        return Aliases.ContainsKey(spelling) || LongFamilyIsUnsigned.ContainsKey(spelling) || PointerSizedIsUnsigned.ContainsKey(spelling) ||
               PointerSpellings.ContainsKey(spelling);
    }

    /// <summary>The canonical codec for one pointer-sized family member at the layout's pointer width.</summary>
    public static string PointerSizedCanonical(bool isUnsigned, int pointerSize)
    {
        return (isUnsigned ? "uint" : "int") + (pointerSize * 8).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>The canonical codec for one <c>long</c> family member at the configured width.</summary>
    public static string LongCanonical(bool isUnsigned, int cLongWidth)
    {
        return cLongWidth == 32
                   ? (isUnsigned ? "uint32" : "int32")
                   : (isUnsigned ? "uint64" : "int64");
    }

    private static Dictionary<string, string> BuildAliases()
    {
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal);

        void Add(string canonical, params string[] spellings)
        {
            foreach (string spelling in spellings)
            {
                aliases.Add(spelling, canonical);
            }
        }

        // 8-bit. "char" stays a raw code unit; the numeric spellings alias the numeric codecs (differences-from-c.md).
        Add("uint8", "unsigned char", "uint8_t", "u8", "__u8", "uchar", "u_char", "u_int8_t", "BYTE", "UCHAR", "UINT8", "_BYTE", "unsigned __int8");
        Add("int8", "signed char", "int8_t", "s8", "__s8", "INT8", "__int8");
        Add("char", "CHAR");
        Add("void", "VOID");
        Add("bool", "_Bool");

        // 16-bit.
        Add("int16", "short", "short int", "signed short", "signed short int", "int16_t", "s16", "__s16", "SHORT", "INT16", "__int16");
        Add("uint16", "ushort", "unsigned short", "unsigned short int", "uint16_t", "u16", "__u16", "u_short", "u_int16_t", "WORD", "USHORT", "UINT16", "_WORD", "unsigned __int16");
        Add("wchar", "wchar_t", "WCHAR");

        // 32-bit. Windows LONG/ULONG are always 32 bits, on every Windows target, so they are not part of the long family.
        Add("int32", "int", "signed", "signed int", "int32_t", "s32", "__s32", "INT", "INT32", "LONG", "LONG32", "__int32");
        Add("uint32", "uint", "unsigned", "unsigned int", "uint32_t", "u32", "__u32", "u_int", "u_int32_t", "UINT", "UINT32", "ULONG", "ULONG32", "DWORD", "DWORD32", "_DWORD", "unsigned __int32");

        // 64-bit.
        Add("int64", "long long", "long long int", "signed long long", "signed long long int", "int64_t", "s64", "__s64", "INT64", "LONGLONG", "LONG64", "__int64");
        Add("uint64", "unsigned long long", "unsigned long long int", "uint64_t", "u64", "__u64", "u_int64_t", "UINT64", "ULONGLONG", "ULONG64", "QWORD", "DWORD64", "DWORDLONG", "_QWORD", "unsigned __int64");

        // Wide integers (Windows OWORD, kernel u128, GCC __int128).
        Add("int128", "int128_t", "__int128", "INT128", "s128", "__s128");
        Add("uint128", "uint128_t", "unsigned __int128", "UINT128", "OWORD", "_OWORD", "u128", "__u128");

        // Floating point.
        Add("float32", "float", "FLOAT");
        Add("float64", "double", "DOUBLE");

        // Variable-length integers: dissect's LEB128 types are unbounded, so the widest bounded codec is the alias target.
        Add("uleb128_64", "uleb128");
        Add("sleb128_64", "ileb128", "sleb128");

        // Terminated strings.
        Add("unicode_string_zero", "string");
        Add("unicode_string_zero>", "string>");
        Add("unicode_string_zero<", "string<");
        Add("ascii_string_zero", "cstring");
        return aliases;
    }
}
