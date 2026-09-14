namespace CStructSharp;

/// <summary>Which of the typed read plan's execution modes a bound member takes (E2.7).</summary>
internal enum TypedMemberMode : byte
{
    Scalar,
    TypedArrayCopy,
    TypedNested,
    TypedNestedArray,
    Materialize,
}
