namespace CStructSharp.Expressions;

/// <summary>One entry of a program's validation prelude: an identifier outside short-circuit and <c>?:</c> arms.</summary>
/// <param name="Key">The key the identifier reads: an index into the program's names, or a slot.</param>
/// <param name="Depth">The identifier's syntax level inside its program (the root is level 1).</param>
internal readonly record struct SessionReference(int Key, int Depth);
