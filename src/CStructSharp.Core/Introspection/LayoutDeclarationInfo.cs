namespace CStructSharp.Introspection;

using System.Collections.Generic;

/// <summary>One exported declaration of a compiled layout.</summary>
public sealed class LayoutDeclarationInfo
{
    /// <summary>Creates the description of one declaration from its compiled form.</summary>
    /// <param name="name">The declared name.</param>
    /// <param name="kind">What the declaration is.</param>
    /// <param name="size">
    ///     The encoded size in bytes when fixed; <see langword="null"/> for a runtime-sized composite.
    /// </param>
    /// <param name="alignment">The alignment in bytes that aligned placement uses for the type.</param>
    /// <param name="fields">The fields of a struct or union in declaration order; empty for other kinds.</param>
    /// <param name="members">The members of an enum or flag in declaration order; empty for other kinds.</param>
    /// <param name="underlyingType">The backing type of an enum or flag, or the aliased type of a typedef.</param>
    /// <param name="pointerDepth">The pointer depth a typedef adds to its aliased type.</param>
    /// <param name="arrayShape">The fixed dimensions of an array typedef, outermost first; empty otherwise.</param>
    internal LayoutDeclarationInfo(
        string name,
        LayoutDeclarationKind kind,
        int? size,
        int alignment,
        IReadOnlyList<LayoutFieldInfo> fields,
        IReadOnlyList<LayoutEnumMemberInfo> members,
        string? underlyingType,
        int pointerDepth,
        IReadOnlyList<int> arrayShape)
    {
        this.Name = name;
        this.Kind = kind;
        this.Size = size;
        this.Alignment = alignment;
        this.Fields = fields;
        this.Members = members;
        this.UnderlyingType = underlyingType;
        this.PointerDepth = pointerDepth;
        this.ArrayShape = arrayShape;
    }

    /// <summary>Gets the declared name.</summary>
    public string Name { get; }

    /// <summary>Gets what the declaration is.</summary>
    public LayoutDeclarationKind Kind { get; }

    /// <summary>Gets the encoded size in bytes when it is fixed; <see langword="null"/> for a runtime-sized composite.</summary>
    public int? Size { get; }

    /// <summary>Gets the alignment aligned placement uses for the type.</summary>
    public int Alignment { get; }

    /// <summary>Gets the fields of a struct or union in declaration order (empty otherwise).</summary>
    public IReadOnlyList<LayoutFieldInfo> Fields { get; }

    /// <summary>Gets the members of an enum or flag in declaration order (empty otherwise).</summary>
    public IReadOnlyList<LayoutEnumMemberInfo> Members { get; }

    /// <summary>Gets the backing type of an enum or flag, or the aliased type of a typedef; <see langword="null"/> for composites.</summary>
    public string? UnderlyingType { get; }

    /// <summary>Gets the pointer depth a typedef adds to its aliased type.</summary>
    public int PointerDepth { get; }

    /// <summary>Gets the fixed dimensions of an array typedef (outermost first); empty otherwise.</summary>
    public IReadOnlyList<int> ArrayShape { get; }
}
