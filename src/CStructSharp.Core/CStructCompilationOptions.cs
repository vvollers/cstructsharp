namespace CStructSharp;

using System.Collections.Generic;
using CStructSharp.Codecs;

/// <summary>Controls the resource limits applied before a C-like layout definition is parsed and compiled.</summary>
/// <remarks>
///     Values are read and validated when <see cref="CStruct"/> is constructed. Later changes to the options object
///     do not alter an already compiled layout. A record: <c>options with { CLongWidth = 32 }</c> copies every other
///     member, and two instances with the same members are equal (<see cref="Codecs"/> and <see cref="Defined"/>
///     compare by reference).
/// </remarks>
public sealed record CStructCompilationOptions
{
    /// <summary>Creates the default bounded compilation policy.</summary>
    public CStructCompilationOptions()
    {
    }

    /// <summary>Gets the greatest accepted layout-source length in characters.</summary>
    public int MaxDefinitionLength { get; init; } = 128 * 1024;

    /// <summary>Gets the greatest brace-nesting depth accepted in a layout definition.</summary>
    public int MaxLayoutNestingDepth { get; init; } = 256;

    /// <summary>
    ///     Gets the greatest syntax-tree or identifier-dependency depth accepted for one expression.
    /// </summary>
    public int MaxExpressionNestingDepth { get; init; } = 256;

    /// <summary>
    ///     Gets the greatest number of expression nodes one evaluation session may compile or execute.
    /// </summary>
    public int MaxExpressionTokens { get; init; } = 100_000;

    /// <summary>
    ///     Gets the width in bits of the C <c>long</c> family (<c>long</c>, <c>unsigned long</c>, <c>ulong</c>,
    ///     <c>long int</c>, ...). Portable's default is 64, the LP64 reading of a kernel header; 32 is the ILP32/LLP64
    ///     reading and the width dissect.cstruct assumes. Windows <c>LONG</c>/<c>ULONG</c> are always 32 bits and are
    ///     not affected. Accepted values are 32 and 64.
    /// </summary>
    public int CLongWidth { get; init; } = 64;

    /// <summary>
    ///     Gets the storage type of an <c>enum</c> or <c>flag</c> declared without a backing type (<c>enum E { A };</c>).
    ///     <see langword="null"/>, the default, applies the rule C compilers use: 32 bits (<c>int</c>-sized),
    ///     <c>uint32</c> unless a member is written as a negative number, which selects <c>int32</c>. That also
    ///     matches dissect.cstruct's <c>uint32</c> default for every header it ships. Any accepted integer spelling
    ///     fixes the storage instead and is resolved when the layout compiles.
    /// </summary>
    public string? DefaultEnumStorage { get; init; }

    /// <summary>
    ///     Gets the names that <c>#ifdef</c>/<c>#ifndef</c> treat as defined before the layout's own <c>#define</c>
    ///     lines are seen, the way a compiler's <c>-D</c> flags do. <see langword="null"/> means none.
    /// </summary>
    public IReadOnlySet<string>? Defined { get; init; }

#if !NETSTANDARD2_0
    /// <summary>
    ///     Gets the caller-supplied primitive types available to the layout by name. The list is compared by
    ///     reference for caching, so keep one instance per codec set. <see langword="null"/> means none.
    /// </summary>
    public IReadOnlyList<ICustomCodec>? Codecs { get; init; }
#endif

    /// <summary>
    ///     Gets layout text compiled ahead of the layout itself - shared typedefs, defines, and declarations that
    ///     several layouts have in common. The prelude's declarations are ordinary declarations of the layout
    ///     (they appear in <see cref="CStruct.Layout"/> and <see cref="CStruct.ToDefinition"/>) and count toward
    ///     <see cref="MaxDefinitionLength"/>. <see langword="null"/> means none.
    /// </summary>
    public string? Prelude { get; init; }

    /// <summary>
    ///     Gets which end of a storage unit the first bitfield takes: <see cref="BitfieldAllocation.LowBitFirst"/>
    ///     (the default) or <see cref="BitfieldAllocation.HighBitFirst"/> for formats specified by big-endian ABIs
    ///     or RFC diagrams. Storage-unit grouping and byte order are unaffected.
    /// </summary>
    public BitfieldAllocation BitfieldAllocation { get; init; } = BitfieldAllocation.LowBitFirst;

    /// <summary>
    ///     Gets how adjacent bitfields of different declared types share storage: <see cref="BitfieldPacking.SysV"/>
    ///     (the default; GCC and Clang on every System V platform, and the Itanium C++ ABI) allocates bits
    ///     contiguously and lets a field join the previous one as long as it does not cross a boundary of its own
    ///     type's size, so <c>uint8 a:4; uint16 b:4;</c> is two bytes; <see cref="BitfieldPacking.Msvc"/> starts a
    ///     new unit of the declared size whenever the size changes, so the same fields take four. Both are part of
    ///     the compiled-layout cache key; <see cref="BitfieldAllocation"/> chooses bit numbering inside a unit
    ///     independently.
    /// </summary>
    public BitfieldPacking BitfieldPacking { get; init; } = BitfieldPacking.SysV;
}
