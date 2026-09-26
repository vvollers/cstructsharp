namespace CStructSharp.Comparison.Model;

using System.Runtime.CompilerServices;

/// <summary>
///     Eight <see cref="int" /> values stored inline (32 bytes), the C# equivalent of <c>int32 samples[8]</c> inside a
///     struct. <see cref="InlineArrayAttribute" /> makes the runtime repeat the single field eight times.
/// </summary>
[InlineArray(8)]
public struct SampleBuffer
{
    // The first element; the runtime lays out the other seven directly after it.
    private int element;
}
