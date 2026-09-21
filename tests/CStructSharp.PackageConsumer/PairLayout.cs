namespace CStructSharp.PackageConsumer;

/// <summary>A two-byte record without pointers, for the record-sequence forms (a sequence advances by the record's extent, so a pointer target would have to live inside it).</summary>
[CStructLayout("struct pair { uint8 first; uint8 second; };")]
public static partial class PairLayout
{
}
