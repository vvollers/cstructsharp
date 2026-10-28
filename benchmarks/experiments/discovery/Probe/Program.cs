// Measures synthetic buffer recognition and selective field operations. Usage: DiscoveryLibraryProbe MODE success|reject|short [iterations].
// Build immutable reference/candidate JIT and Native AOT executables first; retain serial alternating process samples.
using System.Buffers.Binary;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using CStructSharp;
using CStructSharp.Memory;
using DiscoveryLibraryProbe;

string mode = args.Length == 0 ? "runtime" : args[0];
string scenario = args.Length < 2 ? "success" : args[1];
int iterations = args.Length < 3 ? 100_000 : int.Parse(args[2], CultureInfo.InvariantCulture);
#if PREPARED
if (mode == "first-bytes")
{
    MemorySchema schema = PortableMemorySchema.Create(new CStruct("struct Text { int8 name[16]; };"), "Text");
    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
    long started = Stopwatch.GetTimestamp();
    MemoryByteReader reader = schema.BindBytes("Text", "name");
    double nanoseconds = Stopwatch.GetElapsedTime(started).TotalNanoseconds;
    long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
    Console.WriteLine(FormattableString.Invariant($"first-bytes: {nanoseconds:F1} ns/op, {allocatedBytes} B/op, length={reader.Length}, iterations=1"));
    return;
}
#endif

bool firstUse = mode is "cold" or "first-runtime";
#if PREPARED
firstUse |= mode is "preparation" or "first-prepared";
#endif
if (firstUse)
{
    const string declaration = "struct Header { uint64 tag @magic(tag == 0xFEDCBA9876543210); uint64 unused; };";
    byte[] input = new byte[16];
    BinaryPrimitives.WriteUInt64LittleEndian(input, 0xFEDCBA9876543210);
    CStruct? firstLayout = mode == "cold" ? null : new CStruct(declaration);
#if PREPARED
    PreparedMatcher? firstMatcher = mode == "first-prepared" ? firstLayout!.PrepareMatcher("Header") : null;
#endif
    long firstAllocated = GC.GetAllocatedBytesForCurrentThread();
    long firstStart = Stopwatch.GetTimestamp();
    ulong firstResult;
#if PREPARED
    if (mode == "preparation")
    {
        firstResult = (ulong)firstLayout!.PrepareMatcher("Header").Rules.Count;
    }
    else if (firstMatcher is not null)
    {
        firstResult = firstMatcher.Matches(input) ? 1UL : 0;
    }
    else
#endif
    {
        firstResult = (firstLayout ?? new CStruct(declaration)).Matches("Header", input) ? 1UL : 0;
    }

    double firstElapsed = Stopwatch.GetElapsedTime(firstStart).TotalNanoseconds;
    firstAllocated = GC.GetAllocatedBytesForCurrentThread() - firstAllocated;
    Console.WriteLine(FormattableString.Invariant($"{mode}/success: {firstElapsed:F1} ns/op, {firstAllocated} B/op, checksum={firstResult}, iterations=1"));
    return;
}

var workload = new Workload(mode, scenario);
if (mode != "cold")
{
    // Preparation and first use have their own named cases; repeated cases warm independently.
    for (int warmup = 0; warmup < 50_000; warmup++)
    {
        workload.Run();
    }
}

long sourceRequests = workload.Source.Requests;
long sourceBytes = workload.Source.Bytes;
long before = GC.GetAllocatedBytesForCurrentThread();
long start = Stopwatch.GetTimestamp();
ulong checksum = 0;
for (int iteration = 0; iteration < iterations; iteration++)
{
    checksum += workload.Run();
}

double elapsed = Stopwatch.GetElapsedTime(start).TotalNanoseconds / iterations;
long allocated = (GC.GetAllocatedBytesForCurrentThread() - before) / iterations;
Console.WriteLine(FormattableString.Invariant($"{mode}/{scenario}: {elapsed:F1} ns/op, {allocated} B/op, checksum={checksum}, sourceRequests={(workload.Source.Requests - sourceRequests) / iterations}, sourceBytes={(workload.Source.Bytes - sourceBytes) / iterations}, iterations={iterations}"));
