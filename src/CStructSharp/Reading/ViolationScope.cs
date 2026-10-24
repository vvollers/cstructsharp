namespace CStructSharp.Reading;

using System.Collections;
using System.Collections.Generic;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>
///     A view of the caller's <see cref="ReadOptions.ConstraintViolations"/> collection for one attempt to read: it forwards every
///     violation to the caller's collection at once and remembers what it forwarded, so that an attempt that is abandoned (a
///     buffered read that needs more of its input and runs again from the start) can take its violations back and the rerun does
///     not add them a second time. A scope that a record sequence points at each record in turn also puts the record's index and
///     input position on each violation, as a thrown failure of that record would carry them.
/// </summary>
internal sealed class ViolationScope : ICollection<CStructConstraintException>
{
    private readonly ICollection<CStructConstraintException> target;
    private int recordIndex;
    private long recordPosition;
    private List<CStructConstraintException>? forwarded;
    private ReadOptions? source;
    private ReadOptions? scoped;

    /// <summary>Creates a scope that forwards to the caller's collection unchanged, until it is pointed at a record (<see cref="BeginRecord"/>).</summary>
    /// <param name="target">The caller's collection.</param>
    public ViolationScope(ICollection<CStructConstraintException> target)
    {
        this.target = target;
        this.recordIndex = -1;
    }

    /// <inheritdoc />
    public int Count => this.target.Count;

    /// <inheritdoc />
    public bool IsReadOnly => this.target.IsReadOnly;

    /// <summary>
    ///     Points a scope that is reused for the records of one enumeration at the next record, so that a sequence allocates the scope
    ///     once rather than once per record. The violations forwarded for the previous record are kept by the caller's collection.
    /// </summary>
    /// <param name="index">The record's index.</param>
    /// <param name="position">Where the record starts, in the input's coordinates.</param>
    public void BeginRecord(int index, long position)
    {
        this.recordIndex = index;
        this.recordPosition = position;
        this.forwarded?.Clear();
    }

    /// <summary>The options a record is read with: <paramref name="options"/> with this scope as its violation collection, built again only when <paramref name="options"/> is another instance than last time.</summary>
    /// <param name="options">The options the sequence reads the record with.</param>
    /// <returns>The scoped options.</returns>
    public ReadOptions ScopedFor(ReadOptions options)
    {
        if (!ReferenceEquals(options, this.source))
        {
            this.source = options;
            this.scoped = options with { ConstraintViolations = this, };
        }

        return this.scoped!;
    }

    /// <summary>Takes back the violations this scope forwarded, so that a rerun of the attempt adds them once.</summary>
    public void Rollback()
    {
        if (this.forwarded is null)
        {
            return;
        }

        foreach (CStructConstraintException violation in this.forwarded)
        {
            _ = this.target.Remove(violation);
        }

        this.forwarded.Clear();
    }

    /// <inheritdoc />
    public void Add(CStructConstraintException item)
    {
        if (this.recordIndex >= 0)
        {
            RecordSequence.Complete(item, this.recordIndex, this.recordPosition);
        }

        (this.forwarded ??= []).Add(item);
        this.target.Add(item);
    }

    /// <inheritdoc />
    public bool Remove(CStructConstraintException item)
    {
        _ = this.forwarded?.Remove(item);
        return this.target.Remove(item);
    }

    /// <inheritdoc />
    public void Clear()
    {
        this.forwarded?.Clear();
        this.target.Clear();
    }

    /// <inheritdoc />
    public bool Contains(CStructConstraintException item) => this.target.Contains(item);

    /// <inheritdoc />
    public void CopyTo(CStructConstraintException[] array, int arrayIndex) => this.target.CopyTo(array, arrayIndex);

    /// <inheritdoc />
    public IEnumerator<CStructConstraintException> GetEnumerator() => this.target.GetEnumerator();

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
