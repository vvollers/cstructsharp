namespace CStructSharp;

/// <summary>Chooses the C# type the generator makes of each struct and union of a layout.</summary>
/// <remarks>
///     A class is a heap object: reading one allocates it (and an array for every array member). A struct is a value that
///     lives in the caller's variable, so a read of a layout of numbers, enums, bitfields, nested structs, text and fixed
///     numeric arrays allocates nothing for the value itself, and the value can be kept in a collection and used across an
///     <see langword="await"/>, which a generated view cannot.
/// </remarks>
public enum GeneratedShape
{
    /// <summary>A <see langword="sealed"/> <see langword="class"/> with settable properties, the default, which every surface can generate.</summary>
    Class = 0,

    /// <summary>
    ///     A <see langword="struct"/> with settable properties. A fixed array of numbers is a generated inline array type
    ///     (<c>InlineArrays.ByteArray4</c>, with <c>AsSpan()</c>, <c>ToArray()</c> and an indexer) instead of a <c>T[]</c>.
    ///     It needs the <see cref="GeneratedSurface.Reader"/> or <see cref="GeneratedSurface.Parser"/> surface and a layout
    ///     that is fixed in size, with no union, conditional member, pointer, runtime-sized array or terminated string;
    ///     anything else is reported as CSG014.
    /// </summary>
    Struct = 1,
}
