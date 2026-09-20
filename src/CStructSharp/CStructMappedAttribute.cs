namespace CStructSharp;

using System;

/// <summary>
///     Asks the CStructSharp source generator to implement <see cref="ICStructMapped{TSelf}"/> for a
///     <c>partial</c> class: <c>ReadFrom</c> and <c>WriteTo</c> assign the class's public members by name, with the
///     conversions <see cref="Values.StructValue.Get{T}(string)"/> applies, and a module initializer registers the
///     type with <see cref="MappedTypes"/>.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false, AllowMultiple = false)]
public sealed class CStructMappedAttribute : Attribute
{
    /// <summary>Marks the class for mapper generation.</summary>
    public CStructMappedAttribute()
    {
    }

    /// <summary>Gets or sets the layout root the class maps, when the generator should check members against it.</summary>
    public string? Layout { get; set; }
}
