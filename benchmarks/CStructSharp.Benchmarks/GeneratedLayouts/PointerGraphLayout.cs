namespace CStructSharp.Benchmarks.GeneratedLayouts;

/// <summary>A two-node linked list behind one-byte pointers, as a generated layout; the runtime side compiles this attribute's definition.</summary>
[CStructLayout("struct node { node *next; uint8 value; }; struct root { node *head; };", Root = "root", PointerSize = 1, Aligned = false, LittleEndian = true)]
public static partial class PointerGraphLayout
{
}
