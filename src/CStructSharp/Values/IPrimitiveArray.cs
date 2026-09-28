namespace CStructSharp.Values;

using System;

/// <summary>Non-generic view of a <see cref="PrimitiveArray{T}"/> for callers that know the element type only at runtime.</summary>
internal interface IPrimitiveArray
{
    /// <summary>Gets the CLR type of every element, the array's type argument.</summary>
    Type ElementType { get; }

    /// <summary>Copies the elements into a new typed array.</summary>
    /// <returns>
    ///     A fresh array whose element type is <see cref="ElementType"/>; changing it does not change this array.
    /// </returns>
    Array ToArray();
}
