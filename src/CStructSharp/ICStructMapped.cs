namespace CStructSharp;

using CStructSharp.Values;

/// <summary>
///     A class that maps to and from a layout composite without reflection: <see cref="ReadFrom"/> builds an
///     instance from a parsed <see cref="StructValue"/> and <see cref="WriteTo"/> fills a <see cref="StructValue"/>
///     of the layout's shape from an instance. The <c>[CStructMapped]</c> source generator writes both members for a
///     <c>partial</c> class; a class may also implement them by hand. <see cref="MappedTypes.Register{T}"/> makes
///     the type known to <see cref="CStruct.ReadValue{T}(System.IO.Stream, string?, System.Collections.Generic.IReadOnlyDictionary{string, int}?, ReadOptions?)"/>,
///     <see cref="StructValue.Get{T}(string)"/>, and the write operations. Register from a module initializer
///     (<c>[ModuleInitializer] internal static void Register() =&gt; MappedTypes.Register&lt;Point&gt;();</c>), as
///     generated code does: a module initializer runs before any other code in the assembly on every runtime,
///     Native AOT included, whereas a static constructor that nothing else triggers is removed by the AOT compiler.
/// </summary>
/// <typeparam name="TSelf">The implementing class.</typeparam>
public interface ICStructMapped<TSelf>
    where TSelf : ICStructMapped<TSelf>
{
    /// <summary>Creates an instance from the members of <paramref name="source"/> (converted with the <see cref="StructValue.Get{T}(string)"/> rules).</summary>
    /// <param name="source">The parsed composite.</param>
    /// <returns>The mapped instance.</returns>
    /// <exception cref="Diagnostics.CStructReadException">A member is missing or cannot be converted.</exception>
    static abstract TSelf ReadFrom(StructValue source);

    /// <summary>Stores the instance's members into <paramref name="target"/>, which the writer created with the layout composite's shape.</summary>
    /// <param name="value">The instance to write.</param>
    /// <param name="target">The struct value to fill; members are set by their layout names.</param>
    static abstract void WriteTo(TSelf value, StructValue target);
}
