namespace CStructSharp;

/// <summary>Identifies whether a field is scalar, statically counted, runtime counted, or flexible.</summary>
internal enum CompiledArrayKind
{
    Scalar,
    Fixed,
    Runtime,
    Flexible,
}
