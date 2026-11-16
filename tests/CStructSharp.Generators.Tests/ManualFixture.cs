namespace CStructSharp.Generators.Tests;

using System.Collections.Generic;

/// <summary>One valid fixture of the language contract and the options it was recorded with.</summary>
/// <param name="Id">The fixture pair's identifier.</param>
/// <param name="Definition">The layout text.</param>
/// <param name="Root">The struct the fixture parses.</param>
/// <param name="PointerSize">The pointer width in bytes.</param>
/// <param name="Aligned">Whether the portable composite-alignment rules apply.</param>
/// <param name="LittleEndian">Whether neutral values are little-endian.</param>
/// <param name="Bytes">The input bytes as hexadecimal text, or <see langword="null"/> when the fixture has none.</param>
/// <param name="Variables">The layout variables passed to the read.</param>
/// <param name="BitfieldAllocation">The bitfield allocation option, or <see langword="null"/> for the default.</param>
/// <param name="CLongWidth">The width of C <c>long</c> in bits; 0 for the library default.</param>
/// <param name="DefaultEnumStorage">The storage type of an enum declared without one, or <see langword="null"/> for the default.</param>
internal sealed record ManualFixture(
    string Id,
    string Definition,
    string Root,
    int PointerSize,
    bool Aligned,
    bool LittleEndian,
    string? Bytes,
    IReadOnlyDictionary<string, int> Variables,
    string? BitfieldAllocation,
    int CLongWidth,
    string? DefaultEnumStorage);
