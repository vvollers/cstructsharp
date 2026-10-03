namespace CStructSharp.Generated.Parity;

/// <summary>Numeric jagged layouts covering direct row decoding, zero-total shapes and union accounting.</summary>
public static partial class MultidimensionalArrayLayouts
{
    /// <summary>A little-endian matrix and a big-endian odd-width cube surrounded by scalar fields.</summary>
    [CStructLayout("struct root { uint8 tag; uint16< matrix[2][3]; uint24> cube[2][2][3]; uint8 tail; };", Root = "root")]
    public static partial class Mixed
    {
    }

    /// <summary>Zero-sized first, middle and last dimensions, followed by a scalar that must remain readable.</summary>
    [CStructLayout("struct root { uint8 first[0][3]; uint16 middle[2][0][3]; uint32 last[2][3][0]; uint8 tail; };", Root = "root")]
    public static partial class Empty
    {
    }

    /// <summary>A multidimensional union member whose repeated storage reads are charged per element.</summary>
    [CStructLayout("union value { uint16> matrix[2][3]; uint8 octets[12]; }; struct root { uint8 tag; value content; uint8 tail; };", Root = "root")]
    public static partial class Union
    {
    }

    /// <summary>An odd-width matrix whose final element crosses the 64 KiB block-reader boundary.</summary>
    [CStructLayout("struct root { uint8 tag; uint24> matrix[2][10923]; uint8 tail; };", Root = "root")]
    public static partial class Block
    {
    }
}
