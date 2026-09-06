using CStructSharp;

var layout = new CStruct("struct header { uint16 kind; uint32 length; };");

// Create six new bytes from an ordinary C# object.
byte[] bytes = layout.Serialize("header", new Header { Kind = 2, Length = 6 });
Console.WriteLine($"Created: {Convert.ToHexString(bytes)}");

// Change a fixed field in those bytes. MemoryStream lets the library seek to it.
using var stream = new MemoryStream(bytes);
layout.UpdateStream(stream, "header.kind", 3);
Console.WriteLine($"Updated: {Convert.ToHexString(stream.ToArray())}");

// Read into a class with checked property conversion.
Header header = layout.ReadValue<Header>(stream.ToArray().AsSpan(), "header");
Console.WriteLine($"Kind = {header.Kind}; Length = {header.Length}");

// Too few bytes are an expected input failure, so use TryReadValue.
bool success = layout.TryReadValue<Header>(new byte[] { 2 }.AsSpan(), out _, "header");
Console.WriteLine($"Truncated read succeeds = {success}");

public sealed class Header
{
    public ushort Kind { get; set; }

    public uint Length { get; set; }
}
