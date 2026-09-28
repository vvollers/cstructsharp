using System.Collections.Generic;
using CStructSharp;

/// <summary>A generated mapper over the same layout: an <c>IList&lt;T&gt;</c> member, matched by name, registered by the generator.</summary>
[CStructMapped(Layout = "record")]
public sealed partial class MappedRecord
{
    /// <summary>Gets or sets the <c>uint8 tag</c> field.</summary>
    public byte Tag { get; set; }

    /// <summary>Gets or sets the nested <c>point origin</c>.</summary>
    public Records.Point Origin { get; set; } = new();

    /// <summary>Gets or sets the <c>point corners[2]</c> array, as a list.</summary>
    public IList<Records.Point> Corners { get; set; } = [];

    /// <summary>Gets or sets the <c>uint8 flags[3]</c> array.</summary>
    public byte[] Flags { get; set; } = [];
}
