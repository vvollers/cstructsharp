namespace CStructSharp.Generators;

using System;
using System.Collections;
using System.Collections.Generic;

/// <summary>
///     An immutable array with element-wise equality, so a record in the incremental pipeline that holds a list
///     still compares by value and the generator's cache keeps working.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly T[]? items;

    /// <summary>Wraps <paramref name="items"/> without copying; the caller must not mutate it afterwards.</summary>
    /// <param name="items">The elements, or <see langword="null"/> for an empty array.</param>
    public EquatableArray(T[]? items)
    {
        this.items = items;
    }

    /// <summary>Gets the empty array, which equals any other empty or default instance.</summary>
    public static EquatableArray<T> Empty => default;

    /// <summary>Gets the number of elements; 0 for a default instance.</summary>
    public int Count => this.items?.Length ?? 0;

    /// <summary>Gets the element at a zero-based position.</summary>
    /// <param name="index">The zero-based element position.</param>
    /// <exception cref="IndexOutOfRangeException">The position is outside the array.</exception>
    public T this[int index] => (this.items ?? throw new IndexOutOfRangeException())[index];

    /// <summary>Compares two arrays element by element.</summary>
    /// <param name="left">The first array.</param>
    /// <param name="right">The second array.</param>
    /// <returns><see langword="true"/> when both hold equal elements in the same order.</returns>
    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    /// <summary>Compares two arrays element by element.</summary>
    /// <param name="left">The first array.</param>
    /// <param name="right">The second array.</param>
    /// <returns><see langword="true"/> when the lengths or any pair of elements differ.</returns>
    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    /// <summary>Compares element by element; a default instance equals an empty one.</summary>
    /// <param name="other">The array to compare with.</param>
    /// <returns><see langword="true"/> when both hold equal elements in the same order.</returns>
    public bool Equals(EquatableArray<T> other)
    {
        T[]? left = this.items;
        T[]? right = other.items;
        if (ReferenceEquals(left, right))
        {
            return true;
        }

        if (left is null || right is null || left.Length != right.Length)
        {
            return (left?.Length ?? 0) == (right?.Length ?? 0);
        }

        for (int index = 0; index < left.Length; index++)
        {
            if (!left[index].Equals(right[index]))
            {
                return false;
            }
        }

        return true;
    }

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is EquatableArray<T> other && this.Equals(other);

    /// <summary>Combines the element hash codes in order, consistent with element-wise equality.</summary>
    /// <returns>0 for a default instance; otherwise a hash of every element.</returns>
    public override int GetHashCode()
    {
        if (this.items is null)
        {
            return 0;
        }

        int hash = 17;
        foreach (T item in this.items)
        {
            hash = unchecked((hash * 31) + item.GetHashCode());
        }

        return hash;
    }

    /// <inheritdoc/>
    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(this.items ?? Array.Empty<T>())).GetEnumerator();

    /// <inheritdoc/>
    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
