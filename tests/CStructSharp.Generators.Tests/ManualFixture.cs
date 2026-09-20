namespace CStructSharp.Generators.Tests;

using System.Collections.Generic;

/// <summary>One valid fixture of the language contract and the options it was recorded with.</summary>
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
