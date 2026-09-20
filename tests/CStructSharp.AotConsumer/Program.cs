using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

// The starter, exactly as the README shows it.
var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
byte[] bytes = [0x02, 0x00, 0x06, 0x00, 0x00, 0x00];
StructValue header = layout.Parse(bytes, "header");
Check(header.Get<ushort>("kind") == 2, "starter kind");
Check(header.Get<uint>("length") == 6, "starter length");

// A typed read into a mapped class with a nested mapped class, a nested array, and a scalar array. Both classes
// register from module initializers; nothing is annotated for the trimmer.
var records = new CStruct("struct point { int16 x; int16 y; }; struct record { uint8 tag; point origin; point corners[2]; uint8 flags[3]; };");
byte[] recordBytes = [7, 0xFE, 0xFF, 5, 0, 1, 0, 2, 0, 3, 0, 4, 0, 1, 2, 3];
Records.Record record = records.ReadValue<Records.Record>(recordBytes, "record");
Check(record.Tag == 7, "mapped tag");
Check(record.Origin.X == -2 && record.Origin.Y == 5, "mapped nested class");
Check(record.Corners.Length == 2 && record.Corners[1].X == 3 && record.Corners[1].Y == 4, "mapped nested array");
Check(record.Flags.SequenceEqual(new byte[] { 1, 2, 3 }), "mapped scalar array");
Check(records.TryReadValue(new byte[] { 7, 0xFE }, "record", out Records.Record? _) == false, "truncated typed read");

// Get<T> through a path, a union, and a pointer.
StructValue parsed = records.Parse(recordBytes, "record");
Check(parsed.Get<short>("corners[0].y") == 2, "path get");
Check(parsed.Get<Records.Point>("origin").X == -2, "path get mapped");
var unionLayout = new CStruct("union choice { uint8 small; uint16 wide; };");
var choice = (UnionValue)unionLayout.ReadValue(new byte[] { 0x34, 0x12 }, "choice")!;
Check(choice.Get<ushort>("wide") == 0x1234, "union get");
var pointerLayout = new CStruct("struct node { uint8 *next; };", pointerSize: 1);
StructValue node = pointerLayout.Parse(new byte[] { 1, 42 }, "node");
Check(node.Get<byte>("next.value") == 42, "pointer get");

// Writes from a mapped class, a dictionary, and a parsed value; an update in place.
byte[] written = records.Serialize("record", record);
Check(written.AsSpan().SequenceEqual(recordBytes), "mapped serialize round trip");
Check(layout.Serialize("header", new Dictionary<string, object?> { ["kind"] = 3, ["length"] = 6 })[0] == 3, "dictionary serialize");
Check(layout.Serialize("header", header).AsSpan().SequenceEqual(bytes), "struct value serialize");
using (var stream = new MemoryStream(bytes))
{
    layout.Update(stream, "header.kind", 9);
    Check(stream.ToArray()[0] == 9, "update");
}

// Debug reads and the diagnostics carry the same facts under AOT.
(StructValue debugValue, IReadOnlyList<DebugData> debug) = layout.ParseWithDebug(bytes, "header");
Check(debugValue.Get<uint>("length") == 6 && debug.Any(item => item.Path == "header.length" && item.Start == 2), "debug parse");
try
{
    layout.Parse(new byte[] { 2, 0, 6 }, "header");
    Check(false, "short read did not throw");
}
catch (CStructReadException error)
{
    Check(error.Message.Contains("length", StringComparison.Ordinal) && error.Offset is not null, "read diagnostics");
}

// A class that does not implement ICStructMapped<T> is not a mapping target; the errors say what is.
try
{
    records.ReadValue<Records.PlainRecord>(recordBytes, "record");
    Check(false, "plain class mapped");
}
catch (CStructReadException error)
{
    Check(error.Message.Contains("PlainRecord", StringComparison.Ordinal), "plain class read guidance: " + error.Message);
}

try
{
    records.Serialize("record", new Records.PlainRecord { Tag = 1 });
    Check(false, "plain class written");
}
catch (CStructWriteException error)
{
    Check(error.Message.Contains("ICStructMapped<T>", StringComparison.Ordinal), "plain class write guidance: " + error.Message);
}

Console.WriteLine("PASS Native AOT consumer");
return 0;

static void Check(bool condition, string what)
{
    if (!condition)
    {
        Console.Error.WriteLine("FAIL " + what);
        Environment.Exit(1);
    }
}
