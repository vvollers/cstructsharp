namespace CStructSharp;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;

/// <summary>
///     A parsed one-dimensional array of a fixed-width numeric primitive, stored as a <typeparamref name="T"/>[]
///     (E2.3). It is the <see cref="IList{T}"/> of <see cref="object"/> the documented value table promises for
///     arrays - enumeration, indexing and passing it back to <c>Serialize</c>/<c>UpdateStream</c> all work - while
///     <see cref="Span"/> and <see cref="ToArray"/> expose the values without boxing. Like a .NET array it is fixed
///     size: elements can be replaced through the indexer, never added or removed.
/// </summary>
/// <typeparam name="T">The element type: <see cref="byte"/>, <see cref="sbyte"/>, <see cref="bool"/>, the 16/32/64-bit integers, <see cref="float"/> or <see cref="double"/>.</typeparam>
public sealed class PrimitiveArray<T> : IList<object?>, IReadOnlyList<object?>, IList
    where T : unmanaged
{
    private readonly T[] values;

    /// <summary>Wraps <paramref name="values"/> without copying.</summary>
    /// <param name="values">The element storage; the array shares it.</param>
    public PrimitiveArray(T[] values)
    {
        ArgumentNullException.ThrowIfNull(values);
        this.values = values;
    }

    /// <summary>Gets the number of elements.</summary>
    public int Count => this.values.Length;

    /// <summary>Gets the elements as a typed read-only span (no boxing).</summary>
    public ReadOnlySpan<T> Span => this.values;

    /// <summary>Gets the elements as typed read-only memory (no boxing).</summary>
    public ReadOnlyMemory<T> Memory => this.values;

    bool ICollection<object?>.IsReadOnly => true;

    bool IList.IsReadOnly => true;

    bool IList.IsFixedSize => true;

    bool ICollection.IsSynchronized => false;

    object ICollection.SyncRoot => this.values;

    object? IList.this[int index]
    {
        get => this.values[index];
        set => this.values[index] = ConvertElement(value);
    }

    /// <summary>Gets or replaces one element; the boxed element is a <typeparamref name="T"/>, and a replacement is converted to <typeparamref name="T"/>.</summary>
    /// <param name="index">The zero-based element index.</param>
    public object? this[int index]
    {
        get => this.values[index];
        set => this.values[index] = ConvertElement(value);
    }

    /// <summary>Copies the elements into a new typed array.</summary>
    /// <returns>A fresh <typeparamref name="T"/>[] with the same values.</returns>
    public T[] ToArray()
    {
        return (T[])this.values.Clone();
    }

    /// <summary>Enumerates the elements as boxed values.</summary>
    /// <returns>An enumerator over the boxed elements.</returns>
    public IEnumerator<object?> GetEnumerator()
    {
        for (int index = 0; index < this.values.Length; index++)
        {
            yield return this.values[index];
        }
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return this.GetEnumerator();
    }

    /// <summary>Finds the first element equal to <paramref name="item"/>.</summary>
    /// <param name="item">The value to find; compared after conversion to <typeparamref name="T"/> when possible.</param>
    /// <returns>The index, or -1.</returns>
    public int IndexOf(object? item)
    {
        if (!TryConvertElement(item, out T needle))
        {
            return -1;
        }

        return Array.IndexOf(this.values, needle);
    }

    /// <summary>Returns whether any element equals <paramref name="item"/>.</summary>
    /// <param name="item">The value to find.</param>
    /// <returns><see langword="true"/> when found.</returns>
    public bool Contains(object? item)
    {
        return this.IndexOf(item) >= 0;
    }

    /// <summary>Copies the boxed elements into <paramref name="array"/>.</summary>
    /// <param name="array">The destination.</param>
    /// <param name="arrayIndex">The destination start index.</param>
    public void CopyTo(object?[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        for (int index = 0; index < this.values.Length; index++)
        {
            array[arrayIndex + index] = this.values[index];
        }
    }

    void IList<object?>.Insert(int index, object? item) => throw FixedSize();

    void IList<object?>.RemoveAt(int index) => throw FixedSize();

    void ICollection<object?>.Add(object? item) => throw FixedSize();

    void ICollection<object?>.Clear() => throw FixedSize();

    bool ICollection<object?>.Remove(object? item) => throw FixedSize();

    int IList.Add(object? value) => throw FixedSize();

    void IList.Clear() => throw FixedSize();

    bool IList.Contains(object? value) => this.Contains(value);

    int IList.IndexOf(object? value) => this.IndexOf(value);

    void IList.Insert(int index, object? value) => throw FixedSize();

    void IList.Remove(object? value) => throw FixedSize();

    void IList.RemoveAt(int index) => throw FixedSize();

    void ICollection.CopyTo(Array array, int index)
    {
        ArgumentNullException.ThrowIfNull(array);
        for (int offset = 0; offset < this.values.Length; offset++)
        {
            array.SetValue(this.values[offset], index + offset);
        }
    }

    /// <summary>Describes the array for debugging.</summary>
    /// <returns>The element type and count.</returns>
    public override string ToString()
    {
        return typeof(T).Name + "[" + this.values.Length.ToString(CultureInfo.InvariantCulture) + "]";
    }

    private static NotSupportedException FixedSize()
    {
        return new NotSupportedException("A parsed primitive array has a fixed size; replace elements through the indexer or copy it to a List.");
    }

    private static T ConvertElement(object? value)
    {
        if (value is T typed)
        {
            return typed;
        }

        if (value is null)
        {
            throw new ArgumentNullException(nameof(value), "A primitive array element cannot be null.");
        }

        return (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
    }

    private static bool TryConvertElement(object? value, out T converted)
    {
        if (value is T typed)
        {
            converted = typed;
            return true;
        }

        if (value is IConvertible)
        {
            try
            {
                converted = (T)Convert.ChangeType(value, typeof(T), CultureInfo.InvariantCulture);
                return true;
            }
            catch (Exception exception) when (exception is InvalidCastException or FormatException or OverflowException)
            {
            }
        }

        converted = default;
        return false;
    }
}
