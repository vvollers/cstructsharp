namespace CStructSharp.Comparison.Model;

/// <summary>
///     The comparison record as a CStructSharp layout: 79 packed little-endian bytes. The runtime variants compile
///     <see cref="Definition" /> through <see cref="Layout" />; the generated variants use the members the source
///     generator adds to this class (<c>Parse</c>, <c>Serialize</c>, <c>ReadingView</c>).
/// </summary>
[CStructLayout(
    """
    struct vec3 { float32 x; float32 y; float32 z; };
    struct reading {
        uint32 id;
        int64 timestamp;
        vec3 position;
        vec3 velocity;
        uint16 flags;
        uint8 kind;
        float64 value;
        int32 samples[8];
    };
    """,
    Root = "reading",
    Aligned = false,
    LittleEndian = true)]
public static partial class ReadingLayout
{
}
