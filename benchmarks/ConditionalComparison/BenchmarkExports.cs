namespace CStructSharpWeb.Wasm;
using System.IO;
using System.Runtime.InteropServices.JavaScript;
using CStructSharp;
public partial class CStructExports
{
    private static CStruct? benchmarkLayout;
    [JSExport]
    public static void BenchmarkCompile(string definition) => benchmarkLayout = new CStruct(definition, aligned: false);
    [JSExport]
    public static string BenchmarkParse(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return SerializeParsedValue(benchmarkLayout!.ParseStream(stream, "root"));
    }
    [JSExport]
    public static int BenchmarkParseCore(byte[] bytes)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        object result = benchmarkLayout!.ParseStream(stream, "root");
        System.GC.KeepAlive(result);
        return checked((int)stream.Position);
    }
}
