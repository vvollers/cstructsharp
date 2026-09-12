using System.Diagnostics;
using System.Text.Json;
using CStructSharp;
var fixtures = JsonSerializer.Deserialize<Fixture[]>(File.ReadAllText(args[0]), new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
var results = new List<object>();
// Give tiered JIT/PGO time to settle before timing the first constructor.
var processWarmup = Stopwatch.StartNew();
while (processWarmup.Elapsed.TotalSeconds < 5) GC.KeepAlive(new CStruct(fixtures[0].Definition, aligned: false));
foreach (var f in fixtures.Where(f => args[1] != "main" || !f.Conditional))
{
    var bytes = f.Bytes?.Select(value => checked((byte)value)).ToArray() ?? Enumerable.Repeat((byte)f.Fill, f.Size).ToArray();
    var layout = new CStruct(f.Definition, aligned: false);
    using var stream = new MemoryStream(bytes, writable: false);
    object expected = layout.ParseStream(stream, "root");
    if (stream.Position != bytes.Length) throw new Exception($"{f.Name}: consumed {stream.Position}/{bytes.Length}");
    if (!layout.Serialize("root", expected).SequenceEqual(bytes)) throw new Exception("Roundtrip failed: " + f.Name);
    foreach (var op in Environment.GetEnvironmentVariable("BENCH_OPERATIONS")?.Split(',') ?? new[] { "compile", "parse", "debug", "span" })
    {
        Func<object> action = op switch {
            "compile" => () => new CStruct(f.Definition, aligned: false),
            "parse" => () => { stream.Position = 0; return layout.ParseStream(stream, "root"); },
            "debug" => () => { stream.Position = 0; return layout.ParseStreamWithDebug(stream, "root"); },
            _ => () => layout.Parse(bytes.AsSpan(), "root")
        };
        object? sink = null;
        var warm = Stopwatch.StartNew();
        int batch = 0;
        while (warm.Elapsed.TotalMilliseconds < 600) { sink = action(); batch++; }
        batch = Math.Clamp((int)(batch * 200.0 / warm.Elapsed.TotalMilliseconds), 1, 1000000);
        var samples = new List<double>(); var allocations = new List<double>();
        for (int i = 0; i < 9; i++) {
            GC.Collect(); GC.WaitForPendingFinalizers(); GC.Collect();
            long before = GC.GetAllocatedBytesForCurrentThread();
            long start = Stopwatch.GetTimestamp();
            for (int j = 0; j < batch; j++) sink = action();
            double ns = Stopwatch.GetElapsedTime(start).TotalNanoseconds / batch;
            allocations.Add((GC.GetAllocatedBytesForCurrentThread() - before) / (double)batch);
            samples.Add(ns);
        }
        GC.KeepAlive(sink);
        results.Add(new { scenario = f.Name, operation = op, batch, ns = samples, allocated = allocations });
        Console.WriteLine($"{f.Name}/{op}: {samples.Order().ElementAt(4):F0} ns");
    }
}
File.WriteAllText(args[2], JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true }));
record Fixture(string Name, string Definition, int Size, bool Conditional, int Fill, int[]? Bytes);
