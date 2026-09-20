namespace CStructSharp.Generated;

using System;
using CStructSharp.Values;

/// <summary>
///     A pointer member of a generated layout class: the stored address, the pointer depth (<c>uint8 **p</c> is
///     depth 2), and - when the read dereferenced it - the typed target. Address 0 is the null pointer and never
///     has a target. Converts to and from the runtime's untyped <see cref="Values.Pointer"/>.
/// </summary>
/// <typeparam name="T">The target type: a generated class, a primitive, or a nested <see cref="Pointer{T}"/>.</typeparam>
public readonly struct Pointer<T> : IEquatable<Pointer<T>>
{
    /// <summary>Creates a pointer with the runtime's invariants.</summary>
    /// <param name="address">The stored address; 0 is null.</param>
    /// <param name="depth">The pointer depth, at least 1.</param>
    /// <param name="value">The dereferenced target, or the default when <paramref name="isDereferenced"/> is false.</param>
    /// <param name="isDereferenced">Whether the read followed the pointer.</param>
    /// <exception cref="ArgumentOutOfRangeException">The address is negative or the depth is not positive.</exception>
    /// <exception cref="ArgumentException">A null pointer is marked dereferenced.</exception>
    public Pointer(long address, int depth, T? value = default, bool isDereferenced = false)
    {
        if (address < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "Pointer addresses cannot be negative.");
        }

        if (depth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "Pointer depth must be greater than zero.");
        }

        if (address == 0 && isDereferenced)
        {
            throw new ArgumentException("A null pointer cannot be marked as dereferenced.", nameof(isDereferenced));
        }

        this.Address = address;
        this.Depth = depth;
        this.Value = isDereferenced ? value : default;
        this.IsDereferenced = isDereferenced;
    }

    /// <summary>Gets the stored address; 0 for the null pointer.</summary>
    public long Address { get; }

    /// <summary>Gets the pointer depth: 1 for <c>T *p</c>, 2 for <c>T **p</c>.</summary>
    public int Depth { get; }

    /// <summary>Gets whether the read followed the pointer and <see cref="Value"/> holds the target.</summary>
    public bool IsDereferenced { get; }

    /// <summary>Gets whether the address is 0.</summary>
    public bool IsNull => this.Address == 0;

    /// <summary>Gets the target when <see cref="IsDereferenced"/>; otherwise the default.</summary>
    public T? Value { get; }

    /// <summary>Compares two pointers.</summary>
    /// <param name="left">The first pointer.</param>
    /// <param name="right">The second pointer.</param>
    /// <returns>Whether they are equal.</returns>
    public static bool operator ==(Pointer<T> left, Pointer<T> right) => left.Equals(right);

    /// <summary>Compares two pointers.</summary>
    /// <param name="left">The first pointer.</param>
    /// <param name="right">The second pointer.</param>
    /// <returns>Whether they differ.</returns>
    public static bool operator !=(Pointer<T> left, Pointer<T> right) => !left.Equals(right);

    /// <summary>Creates a typed pointer from the runtime's value, converting the target with <paramref name="convert"/>.</summary>
    /// <param name="pointer">The runtime pointer.</param>
    /// <param name="convert">Turns the untyped target into <typeparamref name="T"/>; called only for a dereferenced pointer.</param>
    /// <returns>The typed pointer.</returns>
    public static Pointer<T> FromPointer(Values.Pointer pointer, Func<object, T> convert)
    {
        ArgumentNullException.ThrowIfNull(pointer);
        ArgumentNullException.ThrowIfNull(convert);
        return pointer.IsDereferenced
                   ? new Pointer<T>(pointer.Address, pointer.Depth, convert(pointer.Value!), true)
                   : new Pointer<T>(pointer.Address, pointer.Depth);
    }

    /// <summary>Creates the runtime's untyped pointer, converting the target with <paramref name="convert"/>.</summary>
    /// <param name="convert">Turns the typed target into the value the runtime stores; called only for a dereferenced pointer.</param>
    /// <returns>The runtime pointer.</returns>
    public Values.Pointer ToPointer(Func<T, object> convert)
    {
        ArgumentNullException.ThrowIfNull(convert);
        return this.IsDereferenced
                   ? new Values.Pointer(this.Address, convert(this.Value!), this.Depth, true)
                   : new Values.Pointer(this.Address, null, this.Depth);
    }

    /// <summary>Compares the address, depth, dereferenced state, and target.</summary>
    /// <param name="other">The pointer to compare with.</param>
    /// <returns>Whether the pointers are equal.</returns>
    public bool Equals(Pointer<T> other)
        => this.Address == other.Address && this.Depth == other.Depth && this.IsDereferenced == other.IsDereferenced &&
           System.Collections.Generic.EqualityComparer<T?>.Default.Equals(this.Value, other.Value);

    /// <summary>Compares with another <see cref="Pointer{T}"/>.</summary>
    /// <param name="obj">The object to compare with.</param>
    /// <returns>Whether <paramref name="obj"/> is an equal pointer.</returns>
    public override bool Equals(object? obj) => obj is Pointer<T> other && this.Equals(other);

    /// <summary>Hashes the address, depth, dereferenced state, and target.</summary>
    /// <returns>The hash code.</returns>
    public override int GetHashCode() => HashCode.Combine(this.Address, this.Depth, this.IsDereferenced, this.Value);

    /// <summary>The target's text when dereferenced; otherwise the address in hexadecimal.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => this.IsDereferenced ? this.Value?.ToString() ?? string.Empty : "0x" + this.Address.ToString("X", System.Globalization.CultureInfo.InvariantCulture);
}
