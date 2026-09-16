namespace CStructSharp;

using System.Collections.Generic;
using System.Numerics;

/// <summary>How a field's elements are counted, as reported by <see cref="LayoutFieldInfo.ArrayKind"/>.</summary>
public enum LayoutArrayKind
{
    /// <summary>One value.</summary>
    Scalar,

    /// <summary>A compile-time fixed count (every dimension in <see cref="LayoutFieldInfo.Dimensions"/> is known).</summary>
    Fixed,

    /// <summary>A count evaluated from an expression per operation.</summary>
    Runtime,

    /// <summary>A terminated character string (<c>char name[]</c>).</summary>
    String,

    /// <summary>Every whole element to the end of the input (<c>T values[EOF]</c>).</summary>
    ToEnd,

    /// <summary>Elements until an all-zero element (<c>T values[]</c> on a non-character type).</summary>
    Terminated,
}
