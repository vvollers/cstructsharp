namespace CStructSharp.Benchmarks;

using BenchmarkDotNet.Attributes;

/// <summary>Serializes a four-kilobyte terminated text field in each supported encoding: ASCII, UTF-8 and UTF-16 in both byte orders.</summary>
[BenchmarkCategory("Write")]
public class TextWriteBenchmarks
{
    private CStruct asciiLayout = null!;
    private CStruct utf8Layout = null!;
    private CStruct utf16LittleEndianLayout = null!;
    private CStruct utf16BigEndianLayout = null!;
    private Dictionary<string, object> asciiData = null!;
    private Dictionary<string, object> utf8Data = null!;
    private Dictionary<string, object> utf16Data = null!;

    /// <summary>Compiles one terminated-text layout per encoding and builds text of about 4 KiB for each.</summary>
    [GlobalSetup]
    public void Setup()
    {
        this.asciiLayout = new CStruct("struct root { char text[]; };");
        this.utf8Layout = new CStruct("struct root { utf8_string_zero text; };");
        this.utf16LittleEndianLayout = new CStruct("struct root { wchar< text[]; };");
        this.utf16BigEndianLayout = new CStruct("struct root { wchar> text[]; };");
        this.asciiData = new Dictionary<string, object> { ["text"] = new string('A', 4096), };
        this.utf8Data = new Dictionary<string, object> { ["text"] = string.Concat(Enumerable.Repeat("Grüße世界", 512)), };
        this.utf16Data = new Dictionary<string, object> { ["text"] = string.Concat(Enumerable.Repeat("Hello世界", 512)), };
    }

    /// <summary>Serializes 4096 ASCII characters and their terminator.</summary>
    /// <returns>The bytes.</returns>
    [Benchmark]
    public byte[] SerializeTerminatedAscii()
    {
        return this.asciiLayout.Serialize("root", this.asciiData);
    }

    /// <summary>Serializes mixed Latin and CJK text as UTF-8 with its terminator.</summary>
    /// <returns>The bytes.</returns>
    [Benchmark]
    [BenchmarkCategory("Impact")]
    public byte[] SerializeTerminatedUtf8()
    {
        return this.utf8Layout.Serialize("root", this.utf8Data);
    }

    /// <summary>Serializes mixed Latin and CJK text as little-endian UTF-16 with its terminator.</summary>
    /// <returns>The bytes.</returns>
    [Benchmark]
    public byte[] SerializeTerminatedUtf16LittleEndian()
    {
        return this.utf16LittleEndianLayout.Serialize("root", this.utf16Data);
    }

    /// <summary>Serializes mixed Latin and CJK text as big-endian UTF-16 with its terminator.</summary>
    /// <returns>The bytes.</returns>
    [Benchmark]
    public byte[] SerializeTerminatedUtf16BigEndian()
    {
        return this.utf16BigEndianLayout.Serialize("root", this.utf16Data);
    }
}
