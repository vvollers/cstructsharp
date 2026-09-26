namespace CStructSharp.Comparison.Variable;

/// <summary>
///     A record whose layout is not fixed, for measuring the general reader and writer: a count-sized array, a
///     length-prefixed name, a conditional member chosen by <c>kind</c>, and a NUL-terminated note. No member after
///     <c>samples</c> has a build-time offset, so neither the static plan nor the fixed readers apply.
/// </summary>
[CStructLayout(
    """
    struct packet {
        uint32 id;
        uint16 count;
        int32 samples[count];
        uint8 name_length;
        char name[name_length];
        uint8 kind;
        if (kind == 1) { float64 value; } else { uint32 code; }
        cstring note;
    };
    """,
    Root = "packet")]
public static partial class PacketLayout
{
}
