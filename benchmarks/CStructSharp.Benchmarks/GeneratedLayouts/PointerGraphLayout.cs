namespace CStructSharp.Benchmarks.GeneratedLayouts;

/// <summary>The <c>ReadBenchmarks</c> pointer graph as a generated layout.</summary>
[CStructLayout("struct node { node *next; uint8 value; }; struct root { node *head; };", Root = "root", PointerSize = 1, Aligned = false, LittleEndian = true)]
public static partial class PointerGraphLayout
{
}
