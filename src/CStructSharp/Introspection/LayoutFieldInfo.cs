namespace CStructSharp.Introspection;

using System.Collections.Generic;

/// <summary>One field of a struct or union.</summary>
public sealed class LayoutFieldInfo
{
    internal LayoutFieldInfo(
        string name,
        string typeName,
        int pointerDepth,
        LayoutArrayKind arrayKind,
        IReadOnlyList<int?> dimensions,
        int? offset,
        int? size,
        int? bitWidth,
        int? bitOffset,
        bool isAnonymous,
        bool isConditional,
        IReadOnlyList<LayoutFieldInfo> promotedFields)
    {
        this.Name = name;
        this.TypeName = typeName;
        this.PointerDepth = pointerDepth;
        this.ArrayKind = arrayKind;
        this.Dimensions = dimensions;
        this.Offset = offset;
        this.Size = size;
        this.BitWidth = bitWidth;
        this.BitOffset = bitOffset;
        this.IsAnonymous = isAnonymous;
        this.IsConditional = isConditional;
        this.PromotedFields = promotedFields;
    }

    /// <summary>Gets the field name; empty for an anonymous bitfield or an anonymous promoted member.</summary>
    public string Name { get; }

    /// <summary>Gets the resolved type name: a canonical primitive codec, a declaration name, or the anonymous composite's own name.</summary>
    public string TypeName { get; }

    /// <summary>Gets the pointer depth (0 for a value field).</summary>
    public int PointerDepth { get; }

    /// <summary>Gets how elements are counted.</summary>
    public LayoutArrayKind ArrayKind { get; }

    /// <summary>Gets each dimension's fixed count, outermost first; <see langword="null"/> when a dimension is decided at runtime.</summary>
    public IReadOnlyList<int?> Dimensions { get; }

    /// <summary>Gets the byte offset from the containing composite's start when it is static; <see langword="null"/> when a preceding field is runtime-sized.</summary>
    public int? Offset { get; }

    /// <summary>Gets the field's total storage size when it is static; <see langword="null"/> otherwise.</summary>
    public int? Size { get; }

    /// <summary>Gets the bit width of a bitfield; <see langword="null"/> otherwise.</summary>
    public int? BitWidth { get; }

    /// <summary>Gets the bit offset of a bitfield inside its storage unit; <see langword="null"/> otherwise.</summary>
    public int? BitOffset { get; }

    /// <summary>Gets whether the field is an anonymous promoted member whose own fields are spliced into the parent (see <see cref="PromotedFields"/>).</summary>
    public bool IsAnonymous { get; }

    /// <summary>Gets whether the field exists only when a conditional selects it.</summary>
    public bool IsConditional { get; }

    /// <summary>Gets the fields of an anonymous promoted member; empty otherwise.</summary>
    public IReadOnlyList<LayoutFieldInfo> PromotedFields { get; }
}
