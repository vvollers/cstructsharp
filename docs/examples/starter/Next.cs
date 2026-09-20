using System.Runtime.CompilerServices;
using CStructSharp;
using CStructSharp.Values;

var layout = new CStruct("struct header { uint16 kind; uint32 length; };");

// Create six new bytes from your own class; its WriteTo below names the layout members it fills.
byte[] bytes = layout.Serialize("header", new Header { Kind = 2, Length = 6 });
Console.WriteLine($"Created: {Convert.ToHexString(bytes)}");

// Change a fixed field in those bytes. MemoryStream lets the library seek to it.
using var stream = new MemoryStream(bytes);
layout.Update(stream, "header.kind", 3);
Console.WriteLine($"Updated: {Convert.ToHexString(stream.ToArray())}");

// Read into the class; ReadFrom converts each member with range checks.
Header header = layout.ReadValue<Header>(stream.ToArray(), "header");
Console.WriteLine($"Kind = {header.Kind}; Length = {header.Length}");

// Too few bytes are an expected input failure, so use TryReadValue.
bool success = layout.TryReadValue<Header>(new byte[] { 2 }, "header", out _);
Console.WriteLine($"Truncated read succeeds = {success}");

// A mapped class: two static members map it to and from the layout, and a module initializer registers it.
// The [CStructMapped] source generator writes all three for a partial class; here they are written by hand.
public sealed class Header : ICStructMapped<Header>
{
    public ushort Kind { get; set; }

    public uint Length { get; set; }

    public static Header ReadFrom(StructValue source)
    {
        return new Header { Kind = source.Get<ushort>("kind"), Length = source.Get<uint>("length") };
    }

    public static void WriteTo(Header value, StructValue target)
    {
        target["kind"] = value.Kind;
        target["length"] = value.Length;
    }

    [ModuleInitializer]
    internal static void Register()
    {
        MappedTypes.Register<Header>();
    }
}
