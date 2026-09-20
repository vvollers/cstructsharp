namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CStructSharp.Compilation;
using CStructSharp.Introspection;
using CStructSharp.Syntax;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>
///     The read-only description of a compiled layout (<see cref="Layout"/>), its rendering back to Portable text
///     (<see cref="ToDefinition"/>), and sibling layouts compiled from the same source with one option changed.
/// </summary>
public partial class CStruct
{
    /// <summary>
    ///     Gets the declarations, fields, offsets, sizes, and members of this layout. Built from the compiled model on
    ///     first use and cached; a layout that is never inspected pays only the deferred slot.
    /// </summary>
    public LayoutInfo Layout => this.compilation.Layout;

    /// <summary>Gets the compilation options this layout was built with (the defaults when none were supplied).</summary>
    public CStructCompilationOptions CompilationOptions => this.compilation.CompilationOptions;

    /// <summary>
    ///     Returns a layout compiled from the same source and options with the requested byte order - for a format
    ///     whose header says which order the rest of the file uses. Repeated calls return the cached instance.
    /// </summary>
    /// <param name="isLittleEndian"><see langword="true"/> for little-endian neutral values; <see langword="false"/> for big-endian.</param>
    /// <returns>This layout when the byte order already matches; otherwise the sibling layout.</returns>
    public CStruct WithEndianness(bool isLittleEndian)
    {
        return isLittleEndian == this.IsLittleEndian
                   ? this
                   : GetOrCompile(this.Source, this.PointerSize, this.Aligned, isLittleEndian, this.CompilationOptions);
    }

    /// <summary>Returns a layout compiled from the same source and options with the requested pointer width.</summary>
    /// <param name="pointerSize">The pointer width in bytes: 1, 2, 4, or 8.</param>
    /// <returns>This layout when the width already matches; otherwise the sibling layout.</returns>
    public CStruct WithPointerSize(byte pointerSize)
    {
        return pointerSize == this.PointerSize
                   ? this
                   : GetOrCompile(this.Source, pointerSize, this.Aligned, this.IsLittleEndian, this.CompilationOptions);
    }

    /// <summary>Returns a layout compiled from the same source and options with the requested placement rule.</summary>
    /// <param name="aligned"><see langword="true"/> for the portable composite-alignment rules; <see langword="false"/> for packed placement.</param>
    /// <returns>This layout when the rule already matches; otherwise the sibling layout.</returns>
    public CStruct WithAlignment(bool aligned)
    {
        return aligned == this.Aligned
                   ? this
                   : GetOrCompile(this.Source, this.PointerSize, aligned, this.IsLittleEndian, this.CompilationOptions);
    }

    /// <summary>Renders the compiled layout back to Portable text (declarations in their compiled order).</summary>
    /// <returns>The layout definition that compiles to this layout's model.</returns>
    public string ToDefinition()
    {
        return this.compilation.ToDefinition();
    }
}
