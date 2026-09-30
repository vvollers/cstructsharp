namespace CStructSharp.Engine;

using CStructSharp.Diagnostics;

/// <summary>One value's place in a <see cref="CapturedLayout"/>: its path and its byte range.</summary>
/// <param name="Path">The value's path, as its debug record formats it.</param>
/// <param name="Start">The value's first byte, as its debug record holds it (<see cref="DebugData.Start"/>).</param>
/// <param name="End">The byte after the value's last byte, as its debug record holds it (<see cref="DebugData.End"/>).</param>
internal readonly record struct LayoutRange(string Path, long Start, long End);
