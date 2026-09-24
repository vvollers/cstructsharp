namespace CStructSharp.Memory.Metadata;

/// <summary>The BTF type kinds this library understands, matching the numeric kind field packed into a BTF type record's info word.</summary>
/// <remarks>Kinds 8 through 11, 17, and 18 are modifiers that point at another type and carry no storage of their own; 0, 7, 12, and 13 have no value representation.</remarks>
public enum BtfKind
{
    /// <summary>The implicit type of ID 0; carries no storage.</summary>
    Void = 0,

    /// <summary>A fixed- or variable-width integer, decoded through a core codec.</summary>
    Int = 1,

    /// <summary>A pointer to another type, or an opaque pointer when its target is <see cref="Void"/>.</summary>
    Ptr = 2,

    /// <summary>A fixed number of equally sized elements.</summary>
    Array = 3,

    /// <summary>Members at explicit offsets that must not overlap.</summary>
    Struct = 4,

    /// <summary>Members at explicit offsets that deliberately overlap.</summary>
    Union = 5,

    /// <summary>A 32-bit enumeration.</summary>
    Enum = 6,

    /// <summary>A forward declaration; carries no storage.</summary>
    Fwd = 7,

    /// <summary>A type alias; transparent when resolving storage.</summary>
    Typedef = 8,

    /// <summary>A <c>volatile</c> qualifier; transparent when resolving storage.</summary>
    Volatile = 9,

    /// <summary>A <c>const</c> qualifier; transparent when resolving storage.</summary>
    Const = 10,

    /// <summary>A <c>restrict</c> qualifier; transparent when resolving storage.</summary>
    Restrict = 11,

    /// <summary>A function; carries no storage.</summary>
    Func = 12,

    /// <summary>A function's parameter and return types; carries no storage.</summary>
    FuncProto = 13,

    /// <summary>A variable declaration, used inside a <see cref="Datasec"/>.</summary>
    Var = 14,

    /// <summary>An ELF section's variable layout.</summary>
    Datasec = 15,

    /// <summary>A floating-point value, decoded through a core codec.</summary>
    Float = 16,

    /// <summary>A declaration-site annotation; transparent when resolving storage.</summary>
    DeclTag = 17,

    /// <summary>A type-site annotation; transparent when resolving storage.</summary>
    TypeTag = 18,

    /// <summary>A 64-bit enumeration.</summary>
    Enum64 = 19,
}
