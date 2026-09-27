namespace CStructSharp.Addressing;

/// <summary>The array facts of a resolved path target.</summary>
/// <param name="IsArray">Whether the declared field is an array, whether or not the path indexed it.</param>
/// <param name="Length">The element count of the dimension left unindexed, or <see langword="null"/> when every dimension was indexed.</param>
/// <param name="Index">The index that selected one element, or <see langword="null"/> when the target is not one element.</param>
internal readonly record struct ArraySelection(bool IsArray, int? Length, int? Index);
