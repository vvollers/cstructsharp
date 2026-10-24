namespace CStructSharp.Reading;

using System.Collections;
using System.Collections.Generic;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;

/// <summary>
///     A view of the caller's <see cref="ReadOptions.ConstraintViolations"/> collection for one attempt to read: it forwards every
///     violation to the caller's collection at once and remembers what it forwarded, so that an attempt that is abandoned (a
///     buffered read that needs more of its input and runs again from the start) can take its violations back and the rerun does
///     not add them a second time. A scope made for one record of a sequence also puts the record's index and input position on
///     each violation, as a thrown failure of that record would carry them.
/// </summary>
internal sealed class ViolationScope : ICollection<CStructConstraintException>
{
    private readonly ICollection<CStructConstraintException> target;
    private readonly int recordIndex;
    private readonly long recordPosition;
    private readonly List<CStructConstraintException> forwarded = [];

    /// <summary>Creates a scope that forwards to the caller's collection unchanged.</summary>
    /// <param name="target">The caller's collection.</param>
    public ViolationScope(ICollection<CStructConstraintException> target)
        : this(target, -1, 0)
    {
    }

    /// <summary>Creates a scope for one record of a sequence.</summary>
    /// <param name="target">The caller's collection.</param>
    /// <param name="recordIndex">The record's index, or -1 for a scope that adds no record context.</param>
    /// <param name="recordPosition">Where the record starts, in the input's coordinates; added to each violation's offset.</param>
    public ViolationScope(ICollection<CStructConstraintException> target, int recordIndex, long recordPosition)
    {
        this.target = target;
        this.recordIndex = recordIndex;
        this.recordPosition = recordPosition;
    }

    /// <inheritdoc />
    public int Count => this.target.Count;

    /// <inheritdoc />
    public bool IsReadOnly => this.target.IsReadOnly;

    /// <summary>Takes back the violations this scope forwarded, so that a rerun of the attempt adds them once.</summary>
    public void Rollback()
    {
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

        this.forwarded.Add(item);
        this.target.Add(item);
    }

    /// <inheritdoc />
    public bool Remove(CStructConstraintException item)
    {
        _ = this.forwarded.Remove(item);
        return this.target.Remove(item);
    }

    /// <inheritdoc />
    public void Clear()
    {
        this.forwarded.Clear();
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
