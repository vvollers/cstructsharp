using System.Collections.Generic;
using CStructSharp;

/// <summary>A generated mapper over the same layout: an <c>IList&lt;T&gt;</c> member, matched by name, registered by the generator.</summary>
[CStructMapped(Layout = "record")]
public sealed partial class MappedRecord
{
    public byte Tag { get; set; }

    public Records.Point Origin { get; set; } = new();

    public IList<Records.Point> Corners { get; set; } = [];

    public byte[] Flags { get; set; } = [];
}
