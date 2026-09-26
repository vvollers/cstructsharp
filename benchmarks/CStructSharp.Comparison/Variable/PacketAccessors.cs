namespace CStructSharp.Comparison.Variable;

/// <summary>The packet's members as prepared accessors; the conditional members are read with <c>TryGet</c> instead.</summary>
public sealed class PacketAccessors
{
    /// <summary>Initializes a new instance of the <see cref="PacketAccessors"/> class.</summary>
    /// <param name="layout">The runtime layout of <see cref="PacketLayout"/>.</param>
    public PacketAccessors(CStruct layout)
    {
        this.Id = layout.GetAccessor<uint>("packet.id");
        this.Count = layout.GetAccessor<ushort>("packet.count");
        this.Samples = layout.GetAccessor<int[]>("packet.samples");
        this.NameLength = layout.GetAccessor<byte>("packet.name_length");
        this.Name = layout.GetAccessor<string>("packet.name");
        this.Kind = layout.GetAccessor<byte>("packet.kind");
        this.Note = layout.GetAccessor<string>("packet.note");
    }

    /// <summary>Gets the accessor of <c>id</c>.</summary>
    public FieldAccessor<uint> Id { get; }

    /// <summary>Gets the accessor of <c>count</c>.</summary>
    public FieldAccessor<ushort> Count { get; }

    /// <summary>Gets the accessor of <c>samples</c>.</summary>
    public FieldAccessor<int[]> Samples { get; }

    /// <summary>Gets the accessor of <c>name_length</c>.</summary>
    public FieldAccessor<byte> NameLength { get; }

    /// <summary>Gets the accessor of <c>name</c>.</summary>
    public FieldAccessor<string> Name { get; }

    /// <summary>Gets the accessor of <c>kind</c>.</summary>
    public FieldAccessor<byte> Kind { get; }

    /// <summary>Gets the accessor of <c>note</c>.</summary>
    public FieldAccessor<string> Note { get; }
}
