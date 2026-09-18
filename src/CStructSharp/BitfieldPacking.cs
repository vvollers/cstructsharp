namespace CStructSharp;

/// <summary>Selects which C compiler family's rule places adjacent bitfields of different declared types.</summary>
public enum BitfieldPacking
{
    /// <summary>
    ///     GCC, Clang, and the Itanium C++ ABI: bits are allocated contiguously from the struct start; a field
    ///     starts a new unit only when it would cross a boundary of its own declared type's size (aligned
    ///     placement) - packed placement never splits. A zero-width field (<c>uint16 : 0;</c>) moves the next
    ///     field to the next boundary of that type. This is the default.
    /// </summary>
    SysV,

    /// <summary>
    ///     Microsoft Visual C++: adjacent bitfields share one unit of the declared size while the declared sizes are
    ///     equal and the next field fits; a size change or an overflow starts a new unit of the new size. A
    ///     zero-width field closes the current unit.
    /// </summary>
    Msvc,
}
