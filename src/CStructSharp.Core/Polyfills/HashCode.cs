#if NETSTANDARD2_0
namespace System;

/// <summary>
///     A minimal <c>System.HashCode</c> for the netstandard2.0 build of the Core sources (the source generator):
///     the combine/add/to-hash-code shape the syntax records use. Hash values need not match the runtime's.
/// </summary>
internal struct HashCode
{
    private const uint Prime1 = 2654435761U;
    private const uint Prime2 = 2246822519U;
    private uint value;
    private int count;

    /// <summary>Combines the hash codes of up to eight values.</summary>
    public static int Combine<T1>(T1 value1)
    {
        var hash = default(HashCode);
        hash.Add(value1);
        return hash.ToHashCode();
    }

    /// <inheritdoc cref="Combine{T1}(T1)"/>
    public static int Combine<T1, T2>(T1 value1, T2 value2)
    {
        var hash = default(HashCode);
        hash.Add(value1);
        hash.Add(value2);
        return hash.ToHashCode();
    }

    /// <inheritdoc cref="Combine{T1}(T1)"/>
    public static int Combine<T1, T2, T3>(T1 value1, T2 value2, T3 value3)
    {
        var hash = default(HashCode);
        hash.Add(value1);
        hash.Add(value2);
        hash.Add(value3);
        return hash.ToHashCode();
    }

    /// <inheritdoc cref="Combine{T1}(T1)"/>
    public static int Combine<T1, T2, T3, T4>(T1 value1, T2 value2, T3 value3, T4 value4)
    {
        var hash = default(HashCode);
        hash.Add(value1);
        hash.Add(value2);
        hash.Add(value3);
        hash.Add(value4);
        return hash.ToHashCode();
    }

    /// <inheritdoc cref="Combine{T1}(T1)"/>
    public static int Combine<T1, T2, T3, T4, T5>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5)
    {
        var hash = default(HashCode);
        hash.Add(value1);
        hash.Add(value2);
        hash.Add(value3);
        hash.Add(value4);
        hash.Add(value5);
        return hash.ToHashCode();
    }

    /// <inheritdoc cref="Combine{T1}(T1)"/>
    public static int Combine<T1, T2, T3, T4, T5, T6>(T1 value1, T2 value2, T3 value3, T4 value4, T5 value5, T6 value6)
    {
        var hash = default(HashCode);
        hash.Add(value1);
        hash.Add(value2);
        hash.Add(value3);
        hash.Add(value4);
        hash.Add(value5);
        hash.Add(value6);
        return hash.ToHashCode();
    }

    /// <summary>Adds one value's hash code.</summary>
    public void Add<T>(T value)
    {
        this.Add(value is null ? 0 : value.GetHashCode());
    }

    /// <summary>Adds one value's hash code computed by <paramref name="comparer"/>.</summary>
    public void Add<T>(T value, Collections.Generic.IEqualityComparer<T>? comparer)
    {
        this.Add(value is null ? 0 : comparer is null ? value.GetHashCode() : comparer.GetHashCode(value));
    }

    /// <summary>The combined hash code.</summary>
    public readonly int ToHashCode()
    {
        uint mixed = this.value ^ (uint)this.count;
        mixed ^= mixed >> 15;
        mixed *= Prime2;
        mixed ^= mixed >> 13;
        return unchecked((int)mixed);
    }

    private void Add(int hash)
    {
        unchecked
        {
            this.value = (this.value + ((uint)hash * Prime1)) * Prime2;
            this.count++;
        }
    }
}
#endif
