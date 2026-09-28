namespace CStructSharp.Compilation;

/// <summary>Identifies whether a field is scalar, statically counted, runtime counted, or sized by the data itself.</summary>
internal enum CompiledArrayKind
{
    /// <summary>A single value, not an array.</summary>
    Scalar,

    /// <summary>An array whose element count is a constant known when the layout is compiled.</summary>
    Fixed,

    /// <summary>An array whose element count is an expression evaluated from earlier fields or variables.</summary>
    Runtime,

    /// <summary>An unsized character array (<c>char name[]</c>): a terminated string.</summary>
    Flexible,

    /// <summary><c>T values[EOF]</c>: every whole element until the end of the input.</summary>
    ToEnd,

    /// <summary><c>T values[]</c> on a non-character type: elements until an all-zero element, which is consumed.</summary>
    Terminated,
}
