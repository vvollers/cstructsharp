namespace CStructSharp;

using System;

/// <summary>
///     Represents a pointer read from a binary layout.
///     It always exposes the stored address and, when pointer reading is enabled, also exposes the value found at that address.
/// </summary>
/// <remarks>
///     A null pointer has address zero. A non-null unresolved pointer keeps its address but has no target value;
///     this is distinct from a followed pointer whose target is available through <see cref="Value"/>.
/// </remarks>
public sealed class Pointer
{
    /// <summary>
    ///     Creates a pointer value with its address, optional target, nesting depth, and explicit follow status.
    ///     Address-only values are unresolved by default and can be supplied directly to writers.
    /// </summary>
    /// <param name="address">The non-negative address payload stored in the binary pointer field.</param>
    /// <param name="value">The parsed target when <paramref name="isDereferenced"/> is <see langword="true"/>; otherwise, <see langword="null"/>.</param>
    /// <param name="depth">The one-based pointer level represented by this value.</param>
    /// <param name="isDereferenced"><see langword="true"/> only when <paramref name="value"/> contains the followed target.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="address"/> is negative or <paramref name="depth"/> is not positive.</exception>
    /// <exception cref="ArgumentException">The address, target, and dereference status form an inconsistent pointer state.</exception>
    public Pointer(long address, object? value, int depth, bool isDereferenced = false)
    {
        if (address < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(address), "Pointer addresses cannot be negative.");
        }

        if (depth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(depth), "Pointer depth must be greater than zero.");
        }

        if (address == 0 && value is not null)
        {
            throw new ArgumentException("A null pointer cannot contain a target value.", nameof(value));
        }

        if (address == 0 && isDereferenced)
        {
            throw new ArgumentException("A null pointer cannot be marked as dereferenced.", nameof(isDereferenced));
        }

        if (!isDereferenced && value is not null)
        {
            throw new ArgumentException("An unresolved pointer cannot contain a target value.", nameof(value));
        }

        if (isDereferenced && value is null)
        {
            throw new ArgumentException("A dereferenced pointer must contain its target value.", nameof(value));
        }

        this.Address = address;
        this.Value = value;
        this.Depth = depth;
        this.IsDereferenced = isDereferenced;
    }

    /// <summary>
    ///     Gets the non-negative address payload read from pointer storage. In relative mode this is the encoded offset,
    ///     while dereferencing uses the checked sum of this value and <see cref="ReadOptions.Origin"/>.
    /// </summary>
    public long Address { get; }

    /// <summary>Gets this pointer's one-based level in a parsed pointer chain.</summary>
    public int Depth { get; }

    /// <summary>
    ///     Gets a value indicating whether the parser followed this pointer to obtain <see cref="Value"/>.
    /// </summary>
    public bool IsDereferenced { get; }

    /// <summary>Gets whether pointer storage contains the null address.</summary>
    public bool IsNull => this.Address == 0;

    /// <summary>Gets the parsed target, another <see cref="Pointer"/>, or <see langword="null"/> when not followed.</summary>
    public object? Value { get; }

    /// <summary>Gets the next pointer in a multi-level chain, when the parsed target is another pointer.</summary>
    public Pointer? Next => this.Value as Pointer;

    /// <summary>Returns the parsed target value, or <see langword="null"/> when the pointer was not followed.</summary>
    /// <returns>The parsed target, another <see cref="Pointer"/>, or <see langword="null"/>.</returns>
    public object? Dereference()
    {
        return this.Value;
    }

    /// <summary>Returns the target value as readable text, or an empty string when no target was read.</summary>
    /// <returns>The target's text representation, or <see cref="string.Empty"/> when no target was read.</returns>
    public override string ToString()
    {
        return this.Value?.ToString() ?? string.Empty;
    }
}
