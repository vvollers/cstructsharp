namespace CStructSharp.Compilation;

/// <summary>
///     One comparison of a field's bytes with a byte string that a constraint makes (<c>oem == "NTFS    "</c>). A reader evaluates it
///     right after reading the field and publishes 1 (equal) or 0 (different) under <see cref="SlotName"/>, the name the
///     constraint's compiled condition reads; the condition itself never sees the array.
/// </summary>
/// <param name="SlotName">The hidden name the 1-or-0 result is published under.</param>
/// <param name="Bytes">The bytes the field's array must equal; as many as the array has elements.</param>
internal sealed record ByteMatch(string SlotName, byte[] Bytes);
