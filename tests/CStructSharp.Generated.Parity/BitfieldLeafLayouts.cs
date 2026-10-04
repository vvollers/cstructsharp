namespace CStructSharp.Generated.Parity;

using System;

/// <summary>Bitfield leaves covering compiled storage windows and observable parent or leaf constructors.</summary>
public static partial class BitfieldLeafLayouts
{
    /// <summary>The canonical packed mixed-width run, including overlapping windows and anonymous padding.</summary>
    [CStructLayout("struct rec { uint8 a:3; uint8 b:5; uint16 c:4; uint16 :4; uint16 d:8; uint32 e:12; uint32 f:20; }; struct root { rec items[2]; };", Root = "root")]
    public static partial class Packed
    {
    }

    /// <summary>Aligned big-endian high-bit-first fields with signed casts, enums and a zero-width separator.</summary>
    [CStructLayout("enum kind : uint16 { One = 1, Last = 31 }; struct rec { int8 a:3; uint8 b:5; kind c:5; uint16 :3; uint16 d:8; uint32 :0; uint32 e:7; uint32 f:25; }; struct root { uint8 prefix; rec first; rec second; uint8 tail; };", Root = "root", Aligned = true, LittleEndian = false, BitfieldAllocation = BitfieldAllocation.HighBitFirst)]
    public static partial class AlignedLeaf
    {
    }

    /// <summary>MSVC units with low-bit allocation over big-endian bytes.</summary>
    [CStructLayout("struct rec { int8 a:3; uint8 b:5; uint16 c:5; uint16 :3; uint16 d:8; uint32 :0; uint32 e:7; uint32 f:25; }; struct root { rec items[2]; };", Root = "root", Aligned = true, LittleEndian = false, BitfieldPacking = BitfieldPacking.Msvc)]
    public static partial class Msvc
    {
    }

    /// <summary>A union reads the same leaf twice while charging each read and retaining its owned raw bytes.</summary>
    [CStructLayout("struct rec { uint16 a:5; uint16 b:11; }; union root { rec first; rec second; };", Root = "root")]
    public static partial class Overlap
    {
    }

    /// <summary>A parent constructor can mutate input or cancel before the bitfield child is entered.</summary>
    [CStructLayout("struct rec { uint8 a:3; uint8 b:5; }; struct root { rec child; uint8 tail; };", Root = "root")]
    public static partial class Parent
    {
        /// <summary>The consumer's parent declaration, with its original generated child initializer.</summary>
        public sealed partial class Root
        {
            /// <summary>Invokes the test's caller action before any child input is consumed.</summary>
            public Root() => Constructing?.Invoke();

            /// <summary>Gets or sets the action observed when the parent is constructed.</summary>
            public static Action? Constructing { get; set; }
        }
    }

    /// <summary>A consumer constructor on the leaf preserves member-by-member execution and callback order.</summary>
    [CStructLayout("struct rec { uint8 a:3; uint8 b:5; };", Root = "rec")]
    public static partial class Constructor
    {
        /// <summary>A leaf whose constructor is user code and must remain on its original path.</summary>
        public sealed partial class Rec
        {
            /// <summary>Runs caller code before reading either bitfield.</summary>
            public Rec() => Constructing?.Invoke();

            /// <summary>Gets or sets the action observed when the leaf is constructed.</summary>
            public static Action? Constructing { get; set; }
        }
    }

    /// <summary>A consumer's field initializer runs user code even though its leaf constructor is implicit.</summary>
    [CStructLayout("struct rec { uint8 a:3; uint8 b:5; };", Root = "rec")]
    public static partial class Initializer
    {
        /// <summary>Gets or sets the callback invoked from the consumer's instance initializer.</summary>
        public static Func<int>? Initialize { get; set; }

        /// <summary>A leaf whose implicit constructor still runs a caller-defined initializer.</summary>
        public sealed partial class Rec
        {
            /// <summary>Gets the caller's initialization result without changing parsed field values.</summary>
            public int Initialized { get; } = Initialize?.Invoke() ?? 0;
        }
    }
}
