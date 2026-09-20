namespace CStructSharp.Benchmarks.GeneratedLayouts;

/// <summary>The <c>real-png</c> fixture as a generated layout.</summary>
[CStructLayout("enum png_color_type : uint8 {\n    Grayscale = 0,\n    Rgb = 2,\n    Palette = 3,\n    GrayscaleAlpha = 4,\n    Rgba = 6\n};\n\nstruct ihdr_chunk {\n    uint32 length;\n    char chunk_type[4];\n    uint32 width;\n    uint32 height;\n    uint8 bit_depth;\n    png_color_type color_type;\n    uint8 compression_method;\n    uint8 filter_method;\n    uint8 interlace_method;\n    uint32 crc;\n};\n\nstruct root {\n    uint8 signature[8];\n    ihdr_chunk ihdr;\n};", Root = "root", PointerSize = 8, Aligned = false, LittleEndian = false)]
public static partial class PngLayout
{
}
