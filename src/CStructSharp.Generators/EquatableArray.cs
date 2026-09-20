namespace CStructSharp.Generators;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;

/// <summary>
///     An immutable array with element-wise equality, so a record in the incremental pipeline that holds a list
///     still compares by value and the generator's cache keeps working.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IReadOnlyList<T>
    where T : IEquatable<T>
{
    private readonly T[]? items;

    public EquatableArray(T[]? items)
    {
        this.items = items;
    }

    public static EquatableArray<T> Empty => default;

    public int Count => this.items?.Length ?? 0;

    public T this[int index] => (this.items ?? throw new IndexOutOfRangeException())[index];

    public static bool operator ==(EquatableArray<T> left, EquatableArray<T> right) => left.Equals(right);

    public static bool operator !=(EquatableArray<T> left, EquatableArray<T> right) => !left.Equals(right);

    public ImmutableArray<T> ToImmutableArray() => this.items is null ? ImmutableArray<T>.Empty : ImmutableArray.Create(this.items);

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

    public override bool Equals(object? obj) => obj is EquatableArray<T> other && this.Equals(other);

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

    public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(this.items ?? Array.Empty<T>())).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => this.GetEnumerator();
}
