namespace CStructSharp.Benchmarks.GeneratedLayouts;

/// <summary>Owned records with short names in both UTF-16 byte orders and a byte-counted UTF-8 label.</summary>
[CStructLayout("struct item { uint32< id; wchar< name[24]; wchar> tag[8]; utf8 label[32]; }; struct root { uint32< count; item records[count]; };", Root = "root", PointerSize = 8, Aligned = false, LittleEndian = true)]
public static partial class TextRecordLayout
{
}
