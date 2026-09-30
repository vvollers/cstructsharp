namespace CStructSharp.Generated.Parity;

/// <summary>Generates the same fixed struct, with a constructor of its own on the outer class.</summary>
[CStructLayout("struct inner { uint16 a; uint16 b; }; struct outer { uint8 tag; inner first; inner second; };", Root = "outer")]
public static partial class NestedConstructorLayout
{
    /// <summary>The outer class, whose constructor gives both nested members one shared value.</summary>
    public sealed partial class Outer
    {
        /// <summary>Creates a value whose nested members are both <see cref="Shared"/>.</summary>
        public Outer()
        {
            this.First = Shared;
            this.Second = Shared;
        }

        /// <summary>Gets the nested value every new <see cref="Outer"/> starts with.</summary>
        public static Inner Shared { get; } = new();
    }
}
