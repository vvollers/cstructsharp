namespace CStructSharp.Generated.Parity;

/// <summary>The generated layouts whose smallest read budgets <see cref="ReadBudgetParityTests"/> compares with the runtime's.</summary>
public static partial class ReadBudgetLayouts
{
    /// <summary>A terminated array of bytes, the smallest data-sized array a budget scan passes over.</summary>
    [CStructLayout("struct one { uint8 a[]; };", Root = "one")]
    public static partial class ByteArray
    {
    }

    /// <summary>A terminated array of two-byte elements, whose terminator is a two-byte zero.</summary>
    [CStructLayout("struct wide { uint16 a[]; };", Root = "wide")]
    public static partial class WideArray
    {
    }

    /// <summary>A terminated array of structs, read element by element.</summary>
    [CStructLayout("struct e { uint8 x; uint8 y; }; struct list { e items[]; };", Root = "list")]
    public static partial class StructArray
    {
    }

    /// <summary>A terminated array inside a nested struct, followed by a member of the outer struct.</summary>
    [CStructLayout("struct inner { uint8 a[]; }; struct outer { inner i; uint8 t; };", Root = "outer")]
    public static partial class NestedArray
    {
    }

    /// <summary>A NUL-terminated one-byte string followed by a byte.</summary>
    [CStructLayout("struct text { cstring name; uint8 x; };", Root = "text")]
    public static partial class NarrowText
    {
    }

    /// <summary>A NUL-terminated UTF-16 string followed by a byte.</summary>
    [CStructLayout("struct wtext { unicode_string_zero name; uint8 x; };", Root = "wtext", LittleEndian = true)]
    public static partial class WideText
    {
    }

    /// <summary>An unsized character array, which is terminated text, followed by a byte.</summary>
    [CStructLayout("struct ctext { char name[]; uint8 x; };", Root = "ctext")]
    public static partial class CharText
    {
    }

    /// <summary>An aligned terminated array after a byte: the padding before it moves the position without being charged.</summary>
    [CStructLayout("struct rec { uint8 tag; uint16 values[]; uint8 tail; };", Root = "rec", Aligned = true)]
    public static partial class AlignedArray
    {
    }

    /// <summary>An aligned fixed struct whose padding and tail padding are skipped, not charged.</summary>
    [CStructLayout("struct padded { uint8 a; uint32 b; uint8 c; };", Root = "padded", Aligned = true)]
    public static partial class PaddedFixed
    {
    }

    /// <summary>An aligned read-to-end array after a byte: its count depends on where the whole input ends.</summary>
    [CStructLayout("struct tail { uint8 a; uint32 v[EOF]; };", Root = "tail", Aligned = true)]
    public static partial class AlignedToEnd
    {
    }

    /// <summary>A runtime-sized aligned record whose padding and tail padding are not charged, read as a sequence of records.</summary>
    [CStructLayout("struct rec { uint8 tag; uint32 values[]; uint8 tail; };", Root = "rec", Aligned = true)]
    public static partial class AlignedRecord
    {
    }

    /// <summary>A pointer whose target lies far past the bytes the read is charged for.</summary>
    [CStructLayout("struct far { uint8 a; uint8* p; };", Root = "far")]
    public static partial class FarPointer
    {
    }
}
