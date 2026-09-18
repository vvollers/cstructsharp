namespace CStructSharp.Reading;

/// <summary>Which of the typed read plan's execution modes a bound member takes.</summary>
internal enum TypedMemberMode : byte
{
    Scalar,
    TypedArrayCopy,
    TypedNested,
    TypedNestedArray,
    Materialize,
}
