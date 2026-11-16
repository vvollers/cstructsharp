namespace CStructSharp.Generators.Tests;

using System.Collections.Generic;
using CStructSharp;

/// <summary>One benchmark fixture case with its bytes and options.</summary>
/// <param name="Id">The fixture's identifier.</param>
/// <param name="Definition">The layout text.</param>
/// <param name="Root">The struct the fixture parses.</param>
/// <param name="PointerSize">The pointer width in bytes.</param>
/// <param name="Aligned">Whether the portable composite-alignment rules apply.</param>
/// <param name="LittleEndian">Whether neutral values are little-endian.</param>
/// <param name="Bytes">The input bytes.</param>
/// <param name="Variables">The layout variables passed to the read.</param>
/// <param name="ReadOptions">The read options, or <see langword="null"/> for the defaults.</param>
/// <param name="ExpectedError">The failure message the read must report, or <see langword="null"/> when it succeeds.</param>
internal sealed record BenchmarkFixture(
    string Id,
    string Definition,
    string Root,
    int PointerSize,
    bool Aligned,
    bool LittleEndian,
    byte[] Bytes,
    IReadOnlyDictionary<string, int> Variables,
    ReadOptions? ReadOptions,
    string? ExpectedError);
