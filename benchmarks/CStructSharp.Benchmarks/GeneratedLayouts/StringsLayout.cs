namespace CStructSharp.Benchmarks.GeneratedLayouts;

/// <summary>The <c>strings-1024</c> fixture as a generated layout.</summary>
[CStructLayout("struct root { char name[32]; char cstr[]; utf8_string_zero utf8_text; wchar< wide[]; unicode_string_zero< utf16_text; };", Root = "root", PointerSize = 8, Aligned = false, LittleEndian = true)]
public static partial class StringsLayout
{
}
