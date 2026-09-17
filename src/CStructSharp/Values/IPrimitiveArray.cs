namespace CStructSharp.Values;

using System;

/// <summary>Non-generic view of a <see cref="PrimitiveArray{T}"/> for callers that know the element type only at runtime.</summary>
internal interface IPrimitiveArray
{
    Type ElementType { get; }

    Array ToArray();
}
