namespace CStructSharp.Addressing;

using System.Collections.Generic;

/// <summary>Tracks semantic context while traversal descends through fields, arrays, and pointers.</summary>
internal sealed class TargetResolutionContext
{
    /// <summary>Creates a traversal context from already snapshotted path metadata.</summary>
    /// <param name="debugPrefix">The path names from the root so far.</param>
    /// <param name="selectedIndexes">Every array index the path supplied so far.</param>
    /// <param name="pointerTargetAddress">The address the last followed pointer stored, if any.</param>
    /// <param name="pointerAccessorsConsumed">How many <c>.value</c> accessors the path followed so far.</param>
    public TargetResolutionContext(
        IReadOnlyList<string> debugPrefix,
        IReadOnlyList<int> selectedIndexes,
        long? pointerTargetAddress = null,
        int pointerAccessorsConsumed = 0)
    {
        this.DebugPrefix = debugPrefix;
        this.SelectedIndexes = selectedIndexes;
        this.PointerTargetAddress = pointerTargetAddress;
        this.PointerAccessorsConsumed = pointerAccessorsConsumed;
    }

    /// <summary>Gets the path names from the root so far.</summary>
    public IReadOnlyList<string> DebugPrefix { get; }

    /// <summary>Gets how many <c>.value</c> accessors the path followed so far.</summary>
    public int PointerAccessorsConsumed { get; }

    /// <summary>Gets the address the last followed pointer stored, or <see langword="null"/> before any pointer.</summary>
    public long? PointerTargetAddress { get; }

    /// <summary>Gets every array index the path supplied so far, in path order.</summary>
    public IReadOnlyList<int> SelectedIndexes { get; }

    /// <summary>
    ///     Returns a context with one declared field and every index supplied for it (zero or more, one
    ///     per dimension actually indexed) appended.
    /// </summary>
    /// <param name="fieldName">The declared field name the path enters.</param>
    /// <param name="selectedIndexes">The indexes the path supplied for this field, in dimension order.</param>
    /// <returns>A new context; this context is not changed.</returns>
    public TargetResolutionContext EnterField(string fieldName, IReadOnlyList<int> selectedIndexes)
    {
        string[] debugPrefix = Append(this.DebugPrefix, fieldName);
        IReadOnlyList<int> combinedIndexes = selectedIndexes.Count == 0
                                                 ? this.SelectedIndexes
                                                 : AppendRange(this.SelectedIndexes, selectedIndexes);
        return new TargetResolutionContext(
            debugPrefix,
            combinedIndexes,
            this.PointerTargetAddress,
            this.PointerAccessorsConsumed);
    }

    /// <summary>Returns a context after following one explicit pointer <c>.value</c> accessor to <paramref name="targetAddress"/>.</summary>
    /// <param name="targetAddress">The stream address, in bytes, that the followed pointer resolved to.</param>
    /// <returns>A new context with the address recorded and one more accessor counted.</returns>
    /// <exception cref="System.OverflowException">The accessor count exceeds <see cref="int.MaxValue"/>.</exception>
    public TargetResolutionContext FollowPointer(long targetAddress)
    {
        return new TargetResolutionContext(
            this.DebugPrefix,
            this.SelectedIndexes,
            targetAddress,
            checked(this.PointerAccessorsConsumed + 1));
    }

    /// <summary>Appends one immutable semantic value to an existing read-only list.</summary>
    private static T[] Append<T>(IReadOnlyList<T> values, T value)
    {
        var result = new T[values.Count + 1];
        for (int index = 0; index < values.Count; index++)
        {
            result[index] = values[index];
        }

        result[^1] = value;
        return result;
    }

    /// <summary>Appends every value from one read-only list to another.</summary>
    private static T[] AppendRange<T>(IReadOnlyList<T> values, IReadOnlyList<T> newValues)
    {
        var result = new T[values.Count + newValues.Count];
        for (int index = 0; index < values.Count; index++)
        {
            result[index] = values[index];
        }

        for (int index = 0; index < newValues.Count; index++)
        {
            result[values.Count + index] = newValues[index];
        }

        return result;
    }
}
