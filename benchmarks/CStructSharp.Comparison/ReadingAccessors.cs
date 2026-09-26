namespace CStructSharp.Comparison;

using CStructSharp.Comparison.Model;

/// <summary>
///     Every member of the comparison record as a prepared <see cref="FieldAccessor{T}"/>, resolved once against the
///     runtime layout. The runtime-view and accessor cases read through these instead of path strings.
/// </summary>
public sealed class ReadingAccessors
{
    /// <summary>Initializes a new instance of the <see cref="ReadingAccessors"/> class by resolving every member path.</summary>
    /// <param name="layout">The runtime layout of <see cref="ReadingLayout"/>.</param>
    public ReadingAccessors(CStruct layout)
    {
        this.Id = layout.GetAccessor<uint>("reading.id");
        this.Timestamp = layout.GetAccessor<long>("reading.timestamp");
        this.Position = [layout.GetAccessor<float>("reading.position.x"), layout.GetAccessor<float>("reading.position.y"), layout.GetAccessor<float>("reading.position.z")];
        this.Velocity = [layout.GetAccessor<float>("reading.velocity.x"), layout.GetAccessor<float>("reading.velocity.y"), layout.GetAccessor<float>("reading.velocity.z")];
        this.Flags = layout.GetAccessor<ushort>("reading.flags");
        this.Kind = layout.GetAccessor<byte>("reading.kind");
        this.Value = layout.GetAccessor<double>("reading.value");
        this.Samples = [.. Enumerable.Range(0, 8).Select(index => layout.GetAccessor<int>($"reading.samples[{index}]"))];
    }

    /// <summary>Gets the accessor of <c>id</c>.</summary>
    public FieldAccessor<uint> Id { get; }

    /// <summary>Gets the accessor of <c>timestamp</c>.</summary>
    public FieldAccessor<long> Timestamp { get; }

    /// <summary>Gets the accessors of <c>position.x</c>, <c>.y</c> and <c>.z</c>.</summary>
    public FieldAccessor<float>[] Position { get; }

    /// <summary>Gets the accessors of <c>velocity.x</c>, <c>.y</c> and <c>.z</c>.</summary>
    public FieldAccessor<float>[] Velocity { get; }

    /// <summary>Gets the accessor of <c>flags</c>.</summary>
    public FieldAccessor<ushort> Flags { get; }

    /// <summary>Gets the accessor of <c>kind</c>.</summary>
    public FieldAccessor<byte> Kind { get; }

    /// <summary>Gets the accessor of <c>value</c>.</summary>
    public FieldAccessor<double> Value { get; }

    /// <summary>Gets the accessors of the eight <c>samples</c> elements.</summary>
    public FieldAccessor<int>[] Samples { get; }
}
