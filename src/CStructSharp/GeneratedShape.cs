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
    ///     A <see langword="struct"/> of at most 1,024 bytes whose members are public fields. A fixed array of numbers is a generated inline array type
    ///     (<c>InlineArrays.ByteArray4</c>, with <c>AsSpan()</c>, <c>ToArray()</c> and an indexer) instead of a <c>T[]</c>.
    ///     It needs the <see cref="GeneratedSurface.Reader"/> or <see cref="GeneratedSurface.Parser"/> surface and a layout
    ///     that is fixed in size, with no union, conditional member, pointer, runtime-sized array or terminated string;
    ///     anything else is reported as CSG014.
    /// </summary>
    Struct = 1,

    /// <summary>
    ///     A <see cref="Struct"/> when the layout and surface qualify for one, otherwise a <see cref="Class"/>, decided for the
    ///     whole layout class: nothing is reported when it does not qualify. It lets a project set one shape for every layout
    ///     (<c>CStructSharpShape</c>) and take the value type where there is one. The members of both shapes that consumer code can
    ///     share are the scalars, text, nested values and, for a fixed array of numbers, the index, <c>Length</c> and
    ///     <c>AsSpan()</c>; code that needs more (the array type itself, the <c>default</c> of a missing value) names its shape.
    /// </summary>
    Auto = 2,

    /// <summary>
    ///     No type at all: a layout whose root struct holds exactly one number, enum or identifier at offset 0 generates a static
    ///     <c>Parse</c> that returns that value (a <c>uint</c>, an enum, a <see cref="System.Guid"/>) and, on the
    ///     <see cref="GeneratedSurface.Reader"/> surface, <c>TryParse</c>; failures, limits and constraints are the struct's. It
    ///     needs the <see cref="GeneratedSurface.Reader"/> or <see cref="GeneratedSurface.Parser"/> surface and generates no view.
    ///     Any other layout is reported as CSG015. It is for the hundreds of one-field layouts a project reads (a stored word, a
    ///     magic number), each of which would otherwise cost a class and three readers.
    /// </summary>
    Scalar = 3,
}
