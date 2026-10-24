namespace CStructSharp.Generators;

/// <summary>The C# type the generator makes of each struct of a layout: the generator's mirror of the public <c>GeneratedShape</c> values (the generator does not reference the runtime assembly).</summary>
internal enum GenerationShape
{
    /// <summary>A class, which every surface can generate.</summary>
    Class = 0,

    /// <summary>A value type; a layout or surface that cannot have one is reported as CSG014.</summary>
    Struct = 1,

    /// <summary>A value type when the layout and surface qualify, otherwise a class, without a report.</summary>
    Auto = 2,

    /// <summary>A static <c>Parse</c> that returns the one value of a one-member layout, and no type.</summary>
    Scalar = 3,
}
