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
    private static readonly byte[] BenchScratch = new byte[64 * 1024];
    private static CStruct? benchLayout;
    private static object? benchRetainedResult;
    private static byte[] benchBytesOut = [];
    private static string benchStringOut = string.Empty;
    private static byte[]? aotProfile;

    /// <summary>Does nothing: the fixed cost of one managed export call.</summary>
    /// <returns>Always 0.</returns>
    [JSExport]
    public static int BenchNoop() => 0;

    /// <summary>Receives a byte array: the cost of marshaling bytes into managed memory.</summary>
    /// <param name="bytes">The bytes marshaled in.</param>
    /// <returns>The length of <paramref name="bytes"/>.</returns>
    [JSExport]
    public static int BenchBytesIn(byte[] bytes) => bytes.Length;

    /// <summary>Prepares a byte array of <paramref name="size"/> bytes for <see cref="BenchBytesOut"/>, outside the measured call.</summary>
    /// <param name="size">The array length in bytes.</param>
    [JSExport]
    public static void BenchPrepareBytesOut(int size)
    {
        benchBytesOut = new byte[size];
        for (int i = 0; i < size; i++)
        {
            benchBytesOut[i] = (byte)i;
        }
    }

    /// <summary>Returns the prepared byte array: the cost of marshaling bytes out to JavaScript.</summary>
    /// <returns>The array prepared by <see cref="BenchPrepareBytesOut"/>.</returns>
    [JSExport]
    public static byte[] BenchBytesOut() => benchBytesOut;

    /// <summary>Receives a string: the cost of marshaling text into managed memory.</summary>
    /// <param name="text">The text marshaled in.</param>
    /// <returns>The length of <paramref name="text"/>.</returns>
    [JSExport]
    public static int BenchStringIn(string text) => text.Length;

    /// <summary>Prepares a string of <paramref name="size"/> characters for <see cref="BenchStringOut"/>, outside the measured call.</summary>
    /// <param name="size">The string length in characters.</param>
    [JSExport]
    public static void BenchPrepareStringOut(int size) => benchStringOut = new string('x', size);

    /// <summary>Returns the prepared string: the cost of marshaling text out to JavaScript.</summary>
    /// <returns>The string prepared by <see cref="BenchPrepareStringOut"/>.</returns>
    [JSExport]
    public static string BenchStringOut() => benchStringOut;

    /// <summary>Reads the whole JS-backed source through the same page mechanism the public parse path uses.</summary>
    /// <param name="source">The JavaScript source object that supplies pages.</param>
    /// <returns>The number of bytes read.</returns>
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

    /// <summary>Compiles through the bridge's own path (a cache hit after the first call).</summary>
    /// <param name="definition">The layout definition.</param>
    /// <param name="optionsJson">The bridge options as JSON.</param>
    [JSExport]
    public static void BenchCompile(string definition, string optionsJson)
    {
        benchLayout = CreateCStruct(definition, ParseOptions(optionsJson));
    }

    /// <summary>Compiles bypassing the layout cache: the cost of an actual compilation under the interpreter.</summary>
    /// <param name="definition">The layout definition.</param>
    /// <param name="optionsJson">The bridge options as JSON.</param>
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
    /// <param name="bytes">The input bytes, marshaled from JavaScript.</param>
    /// <param name="root">The root declaration to parse.</param>
    /// <param name="optionsJson">The bridge options as JSON.</param>
    /// <returns>The position after the parsed root.</returns>
    [JSExport]
    public static int BenchParseCore(byte[] bytes, string root, string optionsJson)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        object result = benchLayout!.Parse(stream, root, options: CreateReadOptions(ParseOptions(optionsJson)));
        GC.KeepAlive(result);
        return checked((int)stream.Position);
    }

    /// <summary>Core parse retaining the result so the projection can be measured separately.</summary>
    /// <param name="bytes">The input bytes, marshaled from JavaScript.</param>
    /// <param name="root">The root declaration to parse.</param>
    /// <param name="optionsJson">The bridge options as JSON.</param>
    /// <returns>The position after the parsed root.</returns>
    [JSExport]
    public static int BenchParseRetain(byte[] bytes, string root, string optionsJson)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        benchRetainedResult = benchLayout!.Parse(stream, root, options: CreateReadOptions(ParseOptions(optionsJson)));
        return checked((int)stream.Position);
    }

    /// <summary>The production JSON projection of the retained result (Expando → dictionary → UTF-8 JSON → string).</summary>
    /// <returns>The JSON projection of the retained result.</returns>
    [JSExport]
    public static string BenchProjectRetained() => SerializeParsedValue(benchRetainedResult!);

    /// <summary>Parse + projection through the same code the public ParseWithDebug/ParseSource exports use.</summary>
    /// <param name="bytes">The input bytes, marshaled from JavaScript.</param>
    /// <param name="root">The root declaration to parse.</param>
    /// <param name="optionsJson">The bridge options as JSON.</param>
    /// <returns>The parse envelope JSON.</returns>
    [JSExport]
    public static string BenchParseJson(byte[] bytes, string root, string optionsJson)
    {
        using var stream = new MemoryStream(bytes, writable: false);
        return SerializeParsedValue((object)benchLayout!.Parse(stream, root, options: CreateReadOptions(ParseOptions(optionsJson))));
    }

    /// <summary>Reports the managed bytes allocated so far, so the harness can compute allocation per operation.</summary>
    /// <returns>The value of <see cref="GC.GetTotalAllocatedBytes(bool)"/>.</returns>
    [JSExport]
    public static double BenchAllocatedBytes() => GC.GetTotalAllocatedBytes(precise: false);

    /// <summary>Options JSON deserialization alone (public-path fixed cost breakdown).</summary>
    /// <param name="optionsJson">The bridge options as JSON.</param>
    /// <returns>The parsed pointer size, so the work is not optimized away.</returns>
    [JSExport]
    public static int BenchOptionsParse(string optionsJson) => ParseOptions(optionsJson).PointerSize ?? 0;

    /// <summary>Envelope serialization alone for a given Data payload.</summary>
    /// <param name="data">The pre-serialized JSON payload placed in the envelope.</param>
    /// <returns>The envelope length in characters.</returns>
    [JSExport]
    public static int BenchEnvelope(string data) => SerializeParseEnvelope("root", data, []).Length;

    /// <summary>Layout cache lookup alone (definition hashing + options → compilation options).</summary>
    /// <param name="definition">The layout definition.</param>
    /// <param name="optionsJson">The bridge options as JSON.</param>
    /// <returns>The layout's pointer size, so the lookup is not optimized away.</returns>
    [JSExport]
    public static int BenchLayoutLookup(string definition, string optionsJson) => CreateCStruct(definition, ParseOptions(optionsJson)).PointerSize;

    /// <summary>
    ///     Marker for the AOT profiler: a profiler-enabled bundle is started with
    ///     <c>aotProfilerOptions.writeAt</c> naming this method, so the profile is written when the recorder calls it
    ///     after the workload. Empty on purpose.
    /// </summary>
    [JSExport]
    public static void BenchStopAotProfile()
    {
    }

    /// <summary>The profiler's send-to target (signature dictated by the Mono AOT profiler): keeps the bytes for JS.</summary>
    /// <param name="buffer">The first byte of the profile data the profiler hands over.</param>
    /// <param name="length">The profile length in bytes.</param>
    /// <param name="extraArgument">The profiler's extra argument, unused.</param>
    public static unsafe void BenchReceiveAotProfile(ref byte buffer, int length, string extraArgument)
    {
        aotProfile = new ReadOnlySpan<byte>(Unsafe.AsPointer(ref buffer), length).ToArray();
    }

    /// <summary>Returns the recorded profile (empty when nothing was recorded).</summary>
    /// <returns>The recorded profile bytes, or an empty array.</returns>
    [JSExport]
    [DynamicDependency(nameof(BenchReceiveAotProfile), typeof(CStructExports))]
    public static byte[] BenchTakeAotProfile() => aotProfile ?? [];

    /// <summary>Forces a full garbage collection between benchmark cases.</summary>
    [JSExport]
    public static void BenchCollect() => GC.Collect();

    /// <summary>Serializes a parsed struct or union alone (benchmark projection cases).</summary>
    private static string SerializeParsedValue(object value)
    {
        InteropJsonWriter writer = projectionWriter ??= new InteropJsonWriter(16 * 1024);
        writer.Reset();
        writer.WriteValue(value);
        return FinishProjection(writer);
    }
}
