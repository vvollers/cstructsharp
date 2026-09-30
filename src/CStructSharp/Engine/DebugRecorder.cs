namespace CStructSharp.Engine;

using System.Collections.Generic;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     The debug records of one compiled-engine debug parse: one record per value read, in read order, holding the
///     value's byte range, path, type spelling and decoded value, and - when an update asks for it - the
///     conditional-layout trace.
/// </summary>
/// <remarks>
///     <para>
///         <b>Paths.</b> A record's path is a linked <see cref="DebugPath"/>, so a member's segment is shared by every record
///         beneath it and <see cref="DebugData.Path"/> formats the path once, on first use. The recorder holds the path of
///         the composite being read (<see cref="Path"/>), the path of its member being read (<see cref="Member"/>), and the
///         path a pointer's target is recorded under (<see cref="Target"/>); a nested composite's frame saves and restores
///         <see cref="Path"/> around itself. A failed operation returns no records, so nothing is restored after a failure.
///     </para>
///     <para>
///         <b>Ranges, not bytes.</b> A record holds its range relative to the operation's origin, and the caller selects the
///         bytes from its own input; only a union's record carries its raw storage, whose views overlap.
///     </para>
///     <para>Owned by one operation on one thread; the engine's debug programs are the only code that uses it.</para>
/// </remarks>
internal sealed class DebugRecorder
{
    // The index of the trace entry of the conditional member being selected, which becomes active once its arm is chosen.
    private int condition = -1;

    /// <summary>Creates the recorder of one debug parse.</summary>
    /// <param name="trace">Whether to keep the conditional-layout trace an update compares (<see cref="Trace"/>).</param>
    public DebugRecorder(bool trace)
    {
        this.Trace = trace ? [] : null;
    }

    /// <summary>Gets the records, in the order the values were read; the list the debug parse returns.</summary>
    public List<DebugData> Records { get; } = [];

    /// <summary>
    ///     Gets the conditional-layout trace, or <see langword="null"/> when the recorder keeps none: one entry per
    ///     conditional member in the order the members were decided.
    /// </summary>
    public List<ConditionalSelection>? Trace { get; }

    /// <summary>Gets or sets the path of the composite whose members are being read; <see langword="null"/> outside every composite (a root field).</summary>
    public DebugPath? Path { get; set; }

    /// <summary>Gets or sets the path of the member being read, which the member's records carry.</summary>
    public DebugPath? Member { get; set; }

    /// <summary>
    ///     Gets or sets the path a pointer's target is recorded under: the pointer's own path (or its element path in a
    ///     pointer array), set before the pointer is followed, in place or after its struct's last member.
    /// </summary>
    public DebugPath? Target { get; set; }

    /// <summary>
    ///     Gets or sets the path the next member entered is recorded under instead of its own segment, used once: a selected
    ///     read (<c>ReadValueWithDebug</c> of a nested path) names its one member by the path the caller selected, whose
    ///     last segment can carry indexes (<c>grid[1]</c>) the member's name does not.
    /// </summary>
    public DebugPath? Selected { get; set; }

    /// <summary>Gets or sets the position the next record starts at, remembered before its value is read.</summary>
    public long Start { get; set; }

    /// <summary>
    ///     The path segment a debug record gives a member: its name, <c>_</c> for unnamed padding (the name it was
    ///     declared with), and an empty segment for an anonymous bitfield or an inline composite without a name.
    /// </summary>
    /// <param name="field">The member.</param>
    /// <returns>The segment.</returns>
    public static string Segment(CompiledField field)
        => field.Name.Length == 0 && field.BitSize == 0 && !field.IsInlineComposite ? "_" : field.Name;

    /// <summary>
    ///     The path of one element of a composite array (or of a pointer array whose targets are composites): the last
    ///     segment of the member's path with the element's coordinates, beside the member's own path under the same parent,
    ///     such as <c>items[2]</c> or <c>grid[1][0]</c>. A selected row keeps the index that selected it, so element 0 of
    ///     <c>grid[1]</c> is <c>grid[1][0]</c>.
    /// </summary>
    /// <param name="field">The array member (or the row a path selected), whose dimensions give the coordinates.</param>
    /// <param name="member">The member's path.</param>
    /// <param name="index">The element's flat row-major index.</param>
    /// <param name="count">The member's element count, the size of a dimension without a fixed count.</param>
    /// <returns>The element's path.</returns>
    public static DebugPath ElementPath(CompiledField field, DebugPath? member, int index, int count)
    {
        string indices = string.Empty;
        int remaining = index;
        for (int dimension = field.Array.Dimensions.Length - 1; dimension >= 0; dimension--)
        {
            int size = field.Array.Dimensions[dimension].FixedCount ?? count;
            indices = "[" + (remaining % size) + "]" + indices;
            remaining /= size;
        }

        return new DebugPath(member!.Parent, member.Name + indices);
    }

    /// <summary>
    ///     The path of one element of a counted pointer target whose elements are composites: the pointer's path with the
    ///     element's index appended to its last segment (<c>nodes[3]</c>).
    /// </summary>
    /// <param name="pointer">The pointer's path, or <see langword="null"/>.</param>
    /// <param name="index">The element's index.</param>
    /// <returns>The element's path, or <see langword="null"/> when the pointer has none.</returns>
    public static DebugPath? CountedElementPath(DebugPath? pointer, int index)
        => pointer is null ? null : new DebugPath(pointer.Parent, pointer.Name + "[" + index + "]");

    /// <summary>
    ///     Names the member about to be read: its path is the composite's path extended by its segment, or the
    ///     <see cref="Selected"/> path, which is then cleared.
    /// </summary>
    /// <param name="field">The member.</param>
    public void EnterMember(CompiledField field)
    {
        if (this.Selected is { } selected)
        {
            this.Member = selected;
            this.Selected = null;
            return;
        }

        this.Member = new DebugPath(this.Path, Segment(field));
    }

    /// <summary>Adds one record.</summary>
    /// <param name="start">The value's first byte.</param>
    /// <param name="end">The position just past the value.</param>
    /// <param name="path">The value's path.</param>
    /// <param name="value">The decoded value.</param>
    /// <param name="typeName">The value's type spelling.</param>
    public void Record(long start, long end, DebugPath? path, object value, string typeName)
        => this.Records.Add(new DebugData { Start = start, End = end, DebugStack = path, Value = value, TypeName = typeName, });

    /// <summary>
    ///     Adds a union's record after its member views' records: its whole storage range, the union value, and its raw
    ///     storage, which the record carries because the views overlap.
    /// </summary>
    /// <param name="start">The union's first byte.</param>
    /// <param name="end">The union's end.</param>
    /// <param name="path">The union's path.</param>
    /// <param name="union">The union value.</param>
    /// <param name="typeName">The union's name.</param>
    public void RecordUnion(long start, long end, DebugPath? path, UnionValue union, string typeName)
        => this.Records.Add(new DebugData { Start = start, End = end, DebugStack = path, Value = union, Bytes = union.GetRawStorageArray(), TypeName = typeName, });

    /// <summary>Adds an inactive trace entry for a conditional member about to be selected, when the recorder keeps a trace.</summary>
    /// <param name="field">The conditional member.</param>
    /// <param name="position">The position at which the member is decided, before it is placed.</param>
    public void BeginCondition(CompiledField field, long position)
    {
        if (this.Trace is { } trace)
        {
            this.condition = trace.Count;
            trace.Add(new ConditionalSelection(field.Declaration.Name.Name, position, Active: false));
        }
    }

    /// <summary>Marks the trace entry of the member whose selection just passed active.</summary>
    public void ActivateCondition()
    {
        if (this.Trace is { } trace)
        {
            trace[this.condition] = trace[this.condition] with { Active = true };
        }
    }

    /// <summary>
    ///     Returns what an update compares before and after its change: each record's path and range, and the trace, which
    ///     the capture takes over.
    /// </summary>
    /// <returns>The records' paths and ranges and the trace (empty when the recorder keeps none).</returns>
    public CapturedLayout Layout()
    {
        var ranges = new LayoutRange[this.Records.Count];
        for (int index = 0; index < this.Records.Count; index++)
        {
            DebugData record = this.Records[index];
            ranges[index] = new LayoutRange(record.Path, record.Start, record.End);
        }

        return new CapturedLayout(ranges, this.Trace ?? []);
    }
}
