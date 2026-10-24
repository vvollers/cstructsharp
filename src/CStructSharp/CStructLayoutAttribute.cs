namespace CStructSharp;

using System;

/// <summary>
///     Asks the CStructSharp source generator to turn a layout into C# at compile time: one class per struct and
///     union, an enum per layout enum, and straight-line <c>Parse</c>, <c>Serialize</c>, <c>Update</c>, and
///     <c>ResolveAddress</c> methods on the attributed <c>static partial</c> class, with the same results and the
///     same diagnostics as <see cref="CStruct"/> produces for the same definition and options.
/// </summary>
/// <remarks>
///     The layout is the constructor argument or, with <see cref="File"/>, a <c>.cstruct</c> file the project lists
///     as an <c>AdditionalFiles</c> item. The other properties mirror the <see cref="CStruct"/> constructor and
///     <see cref="CStructCompilationOptions"/>. An option this attribute does not name takes the project's default from the
///     matching <c>CStructSharp*</c> build property (<c>CStructSharpSurface</c> for <see cref="Surface"/>, and so on), then
///     the library's.
/// </remarks>
[AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
public sealed class CStructLayoutAttribute : Attribute
{
    /// <summary>Generates from layout text.</summary>
    /// <param name="definition">The Portable v1 layout source.</param>
    public CStructLayoutAttribute(string definition)
    {
        this.Definition = definition;
    }

    /// <summary>Generates from the file named by <see cref="File"/>.</summary>
    public CStructLayoutAttribute()
    {
    }

    /// <summary>Gets the layout text, when it was given inline.</summary>
    public string? Definition { get; }

    /// <summary>Gets or sets the path of a <c>.cstruct</c> additional file, relative to the project, holding the layout text.</summary>
    public string? File { get; set; }

    /// <summary>Gets or sets the declaration the plain <c>Parse</c>/<c>Serialize</c> methods use; the default is the first struct.</summary>
    public string? Root { get; set; }

    /// <summary>Gets or sets whether the portable composite-alignment rules apply.</summary>
    public bool Aligned { get; set; }

    /// <summary>Gets or sets whether neutral values are little-endian.</summary>
    public bool LittleEndian { get; set; } = true;

    /// <summary>Gets or sets the pointer width in bytes (1, 2, 4, or 8).</summary>
    public int PointerSize { get; set; } = 8;

    /// <summary>Gets or sets how bitfields pack into storage units.</summary>
    public BitfieldPacking BitfieldPacking { get; set; } = BitfieldPacking.SysV;

    /// <summary>Gets or sets which end of a storage unit bitfields are allocated from.</summary>
    public BitfieldAllocation BitfieldAllocation { get; set; } = BitfieldAllocation.LowBitFirst;

    /// <summary>Gets or sets the width of C <c>long</c> in bits; 0 means the library default (64).</summary>
    public int CLongWidth { get; set; }

    /// <summary>Gets or sets the preprocessor symbols defined for the layout.</summary>
    public string[]? Defined { get; set; }

    /// <summary>Gets or sets the storage type of an enum declared without one (<c>enum kind { ... }</c>); <see langword="null"/> means the library default.</summary>
    public string? DefaultEnumStorage { get; set; }

    /// <summary>
    ///     Gets or sets the custom codecs the layout uses, one declaration per codec: <c>"name"</c> for a
    ///     variable-length codec, <c>"name:size"</c> for a fixed size in bytes, <c>"name:size:alignment"</c> to also
    ///     give the alignment (<c>"name:*:alignment"</c> for a variable-length one). The generator places fields with
    ///     these facts; the class supplies the matching <see cref="Codecs.ICustomCodec"/> instances, in the same order,
    ///     from its <c>static partial IReadOnlyList&lt;ICustomCodec&gt; CreateCodecs()</c> method.
    /// </summary>
    public string[]? Codecs { get; set; }

    /// <summary>Gets or sets whether generated names keep the layout's spelling instead of becoming PascalCase.</summary>
    public bool KeepNames { get; set; }

    /// <summary>Gets or sets whether a zero-allocation <c>ref struct</c> view is generated per composite.</summary>
    /// <remarks>It must stay <see langword="true"/> with <see cref="GeneratedSurface.Views"/>, whose only output is the views.</remarks>
    public bool Views { get; set; } = true;

    /// <summary>Gets or sets whether each view of a fixed-size struct gets a <c>foreach</c> enumerator over consecutive records.</summary>
    /// <remarks>
    ///     The full surface always has the enumerator. The <see cref="GeneratedSurface.Reader"/>, <see cref="GeneratedSurface.Parser"/>
    ///     and <see cref="GeneratedSurface.Views"/> surfaces leave it out unless this is <see langword="true"/>, because it adds
    ///     code to every fixed-size struct. It needs <see cref="Views"/>.
    /// </remarks>
    public bool Enumerators { get; set; }

    /// <summary>Gets or sets whether the byte order of neutral values (those spelled without a <c>&lt;</c> or <c>&gt;</c> suffix) is chosen when a value is read instead of when the layout is compiled.</summary>
    /// <remarks>
    ///     <para>
    ///         One layout then reads both byte orders: every <c>Parse</c> and <c>TryParse</c> takes a <c>bool littleEndian</c> after the source, a
    ///         view's constructor and <c>Enumerate</c> take it after the source too, and <see cref="LittleEndian"/> is not used. It replaces a pair of
    ///         layouts that differ only in <see cref="LittleEndian"/>. A field with an explicit suffix keeps its order.
    ///     </para>
    ///     <para>
    ///         It needs a surface that only reads (<see cref="GeneratedSurface.Reader"/>, <see cref="GeneratedSurface.Parser"/> or
    ///         <see cref="GeneratedSurface.Views"/>), and a layout whose bitfields sit in the same place in both byte orders;
    ///         anything else is reported as CSG013.
    ///     </para>
    /// </remarks>
    public bool RuntimeByteOrder { get; set; }

    /// <summary>Gets or sets whether each struct and union of the layout becomes a class (the default), a value type, or a value type only where the layout qualifies; see <see cref="GeneratedShape"/>.</summary>
    public GeneratedShape Shape { get; set; } = GeneratedShape.Class;

    /// <summary>
    ///     Gets or sets the largest struct, in bytes, that a value-type shape (<see cref="GeneratedShape.Struct"/>, or
    ///     <see cref="GeneratedShape.Auto"/> where it applies) may generate; a bigger struct is copied into every return value and
    ///     variable, so the default of 1,024 keeps it a class. The value must be between 1 and 65,536; <see cref="GeneratedShape.Struct"/>
    ///     with another value is reported as CSG014, and <see cref="GeneratedShape.Auto"/> keeps the class. The project default is the
    ///     build property <c>CStructSharpMaxValueBytes</c>.
    /// </summary>
    public int MaxValueBytes { get; set; } = 1024;

    /// <summary>Gets or sets how much code is generated; the default, <see cref="GeneratedSurface.Full"/>, generates everything.</summary>
    public GeneratedSurface Surface { get; set; } = GeneratedSurface.Full;
}
