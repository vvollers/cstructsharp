namespace CStructSharp.Generators.Tests;

using System.Collections.Generic;
using CStructSharp;

/// <summary>One benchmark fixture case with its bytes and options.</summary>
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
