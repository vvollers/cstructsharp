namespace CStructSharp.Values;

using System.Diagnostics.CodeAnalysis;

/// <summary>
///     Typed element access for an array value, so <c>Get&lt;int&gt;("samples[3]")</c> can read an element of a
///     <see cref="PrimitiveArray{T}"/> without boxing it. The type parameter is unconstrained so that callers with an
///     unconstrained <typeparamref name="T"/> can test for it.
/// </summary>
/// <typeparam name="T">The element type.</typeparam>
internal interface ITypedElements<T>
{
    /// <summary>Reads one element when <paramref name="index"/> is inside the array.</summary>
    /// <param name="index">The zero-based element index.</param>
    /// <param name="value">The element when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> when the index is outside the array.</returns>
    bool TryGetElement(int index, [MaybeNullWhen(false)] out T value);
}
