namespace CStructSharp.Fuzzing;

using CStructSharp;

/// <summary>The pointer-union target's layout as a generated class, for the generated-vs-runtime differential.</summary>
[CStructLayout("union payload { uint32 number; byte raw[4]; }; struct node { byte tag; payload data; node *next; };", Root = "node", PointerSize = 2)]
internal static partial class PointerUnionLayout
{
}
