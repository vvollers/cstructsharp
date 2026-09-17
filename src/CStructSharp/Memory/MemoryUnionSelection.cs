namespace CStructSharp.Memory;

/// <summary>Names which member of a union a write should encode; the rest of the union's bytes become zero.</summary>
/// <remarks>
/// A union stores several interpretations of the same bytes, so reading one yields a dictionary with every member
/// decoded. That dictionary cannot be written back: encoding all members would make the result depend on which one
/// was encoded last. This value removes the ambiguity by naming one member. Encoding a whole union with it clears
/// the storage first, so bytes outside a smaller chosen member are zero. To keep unknown bytes exactly, pass a
/// <c>byte[]</c> of the union's size instead, or update a specific member path so neighboring bytes are preserved.
/// </remarks>
/// <param name="Member">Name of the union member to encode.</param>
/// <param name="Value">Value in the declared shape of that member's type.</param>
public sealed record MemoryUnionSelection(string Member, object? Value);
