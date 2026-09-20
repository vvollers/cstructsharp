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
///     <see cref="CStructCompilationOptions"/>.
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

    /// <summary>Gets or sets whether generated names keep the layout's spelling instead of becoming PascalCase.</summary>
    public bool KeepNames { get; set; }

    /// <summary>Gets or sets whether a zero-allocation <c>ref struct</c> view is generated per composite.</summary>
    public bool Views { get; set; } = true;
}
