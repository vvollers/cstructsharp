namespace CStructSharp.Compilation;

/// <summary>Identifies the executor category attached to one compiled type symbol.</summary>
internal enum CompiledTypeKind
{
    /// <summary>A built-in or custom codec that reads one value directly, such as <c>uint32</c>.</summary>
    Primitive,

    /// <summary>An enum or flag type, read through its underlying integer primitive.</summary>
    Enum,

    /// <summary>A struct whose members follow one another in declaration order.</summary>
    Struct,

    /// <summary>A union whose members all start at the same offset.</summary>
    Union,
}
