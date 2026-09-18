namespace CStructSharpWeb.Wasm;

using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using CStructSharp;

/// <summary>
///     Benchmark-only exports (never part of a normal build). They isolate the boundary costs the public API pays:
///     marshaling bytes/strings in and out, page reads through the JS-backed source stream, schema compilation,
///     the core parse without projection, and the JSON projection on its own. Managed allocation is exposed via
///     GC.GetTotalAllocatedBytes so the harness can report bytes allocated per operation.
/// </summary>
[SupportedOSPlatform("browser")]
public partial class CStructExports
{
    private static CStruct? benchLayout;
    private static object? benchRetainedResult;
    private static byte[] benchBytesOut = [];
    private static string benchStringOut = string.Empty;
    private static readonly byte[] BenchScratch = new byte[64 * 1024];

    [JSExport]
    public static int BenchNoop() => 0;

    [JSExport]
    public static int BenchBytesIn(byte[] bytes) => bytes.Length;

    [JSExport]
    public static void BenchPrepareBytesOut(int size)
    {
        benchBytesOut = new byte[size];
        for (int i = 0; i < size; i++)
        {
            benchBytesOut[i] = (byte)i;
        }
    }

    [JSExport]
    public static byte[] BenchBytesOut() => benchBytesOut;

    [JSExport]
    public static int BenchStringIn(string text) => text.Length;

    [JSExport]
    public static void BenchPrepareStringOut(int size) => benchStringOut = new string('x', size);

    [JSExport]
    public static string BenchStringOut() => benchStringOut;

    /// <summary>Reads the whole JS-backed source through the same page mechanism the public parse path uses.</summary>
    [JSExport]
    public static double BenchPageReadAll(JSObject source)
    {
        using var stream = new JavaScriptSourceStream(source);
        long total = 0;
        int read;
        while ((read = stream.Read(BenchScratch, 0, BenchScratch.Length)) > 0)
        {
            total += read;
        }

        return total;
    }

    /// <summary>Compiles through the bridge's own path (a cache hit after the first call since E3.2).</summary>
    [JSExport]
    public static void BenchCompile(string definition, string optionsJson)
    {
        benchLayout = CreateCStruct(definition, ParseOptions(optionsJson));
    }

    /// <summary>Compiles bypassing the layout cache: the cost of an actual compilation under the interpreter.</summary>
    [JSExport]
    public static void BenchCompileFresh(string definition, string optionsJson)
    {
        InteropOptionsDto options = ParseOptions(optionsJson);
        benchLayout = new CStruct(
            definition,
            (byte)(options.PointerSize ?? 8),
            options.Aligned ?? false,
            options.LittleEndian ?? true);
    }

    /// <summary>Core parse from a managed byte[] with no projection; returns the consumed position.</summary>
    [JSExport]
    public static int BenchParseCore(byte[] bytes, string root, string optionsJson)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        object result = benchLayout!.Parse(stream, root, options: CreateReadOptions(ParseOptions(optionsJson)));
        GC.KeepAlive(result);
        return checked((int)stream.Position);
    }

    /// <summary>Core parse retaining the result so the projection can be measured separately.</summary>
    [JSExport]
    public static int BenchParseRetain(byte[] bytes, string root, string optionsJson)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        benchRetainedResult = benchLayout!.Parse(stream, root, options: CreateReadOptions(ParseOptions(optionsJson)));
        return checked((int)stream.Position);
    }

    /// <summary>The production JSON projection of the retained result (Expando → dictionary → UTF-8 JSON → string).</summary>
    [JSExport]
    public static string BenchProjectRetained() => SerializeParsedValue(benchRetainedResult!);

    /// <summary>Parse + projection through the same code the public ParseWithDebug/ParseSource exports use.</summary>
    [JSExport]
    public static string BenchParseJson(byte[] bytes, string root, string optionsJson)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return SerializeParsedValue((object)benchLayout!.Parse(stream, root, options: CreateReadOptions(ParseOptions(optionsJson))));
    }

    [JSExport]
    public static double BenchAllocatedBytes() => GC.GetTotalAllocatedBytes(precise: false);

    /// <summary>Options JSON deserialization alone (public-path fixed cost breakdown, E3.5/E3.8 scoping).</summary>
    [JSExport]
    public static int BenchOptionsParse(string optionsJson) => ParseOptions(optionsJson).PointerSize ?? 0;

    /// <summary>Envelope serialization alone for a given Data payload.</summary>
    [JSExport]
    public static int BenchEnvelope(string data) => SerializeParseEnvelope("root", data, []).Length;

    /// <summary>Layout cache lookup alone (definition hashing + options → compilation options).</summary>
    [JSExport]
    public static int BenchLayoutLookup(string definition, string optionsJson) => CreateCStruct(definition, ParseOptions(optionsJson)).PointerSize;

    /// <summary>
    ///     Marker for the AOT profiler (E3.1b): a profiler-enabled bundle is started with
    ///     <c>aotProfilerOptions.writeAt</c> naming this method, so the profile is written when the recorder calls it
    ///     after the workload. Empty on purpose.
    /// </summary>
    [JSExport]
    public static void BenchStopAotProfile()
    {
    }

    /// <summary>The profiler's send-to target (signature dictated by the Mono AOT profiler): keeps the bytes for JS.</summary>
    public static unsafe void BenchReceiveAotProfile(ref byte buffer, int length, string extraArgument)
    {
        aotProfile = new ReadOnlySpan<byte>(Unsafe.AsPointer(ref buffer), length).ToArray();
    }

    /// <summary>Returns the recorded profile (empty when nothing was recorded).</summary>
    [JSExport]
    [DynamicDependency(nameof(BenchReceiveAotProfile), typeof(CStructExports))]
    public static byte[] BenchTakeAotProfile() => aotProfile ?? [];

    private static byte[]? aotProfile;

    [JSExport]
    public static void BenchCollect() => GC.Collect();
}
