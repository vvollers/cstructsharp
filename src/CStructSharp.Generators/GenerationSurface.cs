namespace CStructSharp.Generators;

/// <summary>How much code one layout generates: the generator's mirror of the public <c>GeneratedSurface</c> values (the generator does not reference the runtime assembly).</summary>
internal enum GenerationSurface
{
    /// <summary>Everything the generator can emit.</summary>
    Full = 0,

    /// <summary>Typed classes and the readers over bytes in memory, constants, and views.</summary>
    Reader = 1,

    /// <summary>Views, constants, and enums only.</summary>
    Views = 2,
}
