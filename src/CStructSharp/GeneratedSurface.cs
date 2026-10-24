namespace CStructSharp;

/// <summary>
///     Chooses how much code <see cref="CStructLayoutAttribute"/> generates for a layout. A smaller surface compiles
///     faster and adds less to the assembly; every surface reads the same bytes with the same results and failures.
/// </summary>
/// <remarks>
///     Every struct and union costs the same generated code however small it is, so the surface matters most for a
///     project with hundreds of layouts of which it reads only a few members each.
/// </remarks>
public enum GeneratedSurface
{
    /// <summary>
    ///     Everything: typed classes, the span, memory, sequence, stream, and asynchronous readers, <c>TryParse</c>,
    ///     <c>Records</c>, <c>Serialize</c> and <c>Write</c>, in-place <c>Update</c> setters, path operations, the
    ///     runtime <c>Layout</c>, size and offset constants, and views.
    /// </summary>
    Full = 0,

    /// <summary>
    ///     Typed classes and the readers over bytes already in memory (<c>Parse</c> and <c>TryParse</c> for a span, an
    ///     array, and a <see cref="System.ReadOnlyMemory{T}"/>), size and offset constants, and views. There is no
    ///     writer, <c>Update</c>, stream or sequence reader, <c>Records</c>, path operation, or runtime <c>Layout</c>.
    /// </summary>
    Reader = 1,

    /// <summary>
    ///     Views, size and offset constants, and the layout's enums only: no typed class and no reader, so a view
    ///     has no <c>ToObject</c> method. A member a view cannot expose is not available at all with this surface.
    /// </summary>
    Views = 2,

    /// <summary>
    ///     The smallest reader: typed classes, one <c>Parse&lt;Name&gt;</c> over a <see cref="System.ReadOnlySpan{T}"/>
    ///     for every struct and union, the unnamed root <c>Parse</c>, size and offset constants, and views (unless
    ///     <see cref="CStructLayoutAttribute.Views"/> is off). There is no <c>TryParse</c>, no array or
    ///     <see cref="System.ReadOnlyMemory{T}"/> overload (a <c>byte[]</c> converts to the span), and no
    ///     <c>Definition</c> or <c>RootName</c> constant, besides everything the <see cref="Reader"/> surface leaves out.
    /// </summary>
    Parser = 3,
}
