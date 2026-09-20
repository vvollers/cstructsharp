using CStructSharp;

// The same six-byte header, generated: the attribute holds the layout text and the compiler produces the classes and
// the operations. Wire.Header is a class with Kind and Length; Wire.Parse and Wire.Serialize are plain methods.
byte[] bytes = [0x02, 0x00, 0x06, 0x00, 0x00, 0x00];
Wire.Header header = Wire.Parse(bytes);
Console.WriteLine($"kind = {header.Kind}; length = {header.Length}");

// Change a property and write the bytes back; the generated writer applies the same range checks as the runtime.
header.Kind = 3;
Console.WriteLine($"Serialized: {Convert.ToHexString(Wire.Serialize(header))}");

// A setter edits one field in place, at the offset the generator computed at build time.
Wire.Update.Length(bytes, 7);
Console.WriteLine($"Updated: {Convert.ToHexString(bytes)} (offset {Wire.Offsets.Length}, size {Wire.Sizes.Header})");

// A view reads straight from the bytes without creating an object.
var view = new Wire.HeaderView(bytes);
Console.WriteLine($"view length = {view.Length}");

// A mapped class: the generator writes ReadFrom, WriteTo, and the registration for a partial class.
Record record = Wire.Layout.ReadValue<Record>(bytes, "header");
Console.WriteLine($"mapped kind = {record.Kind}");

[CStructLayout("struct header { uint16 kind; uint32 length; };")]
public static partial class Wire
{
}

[CStructMapped(Layout = "header")]
public sealed partial class Record
{
    public ushort Kind { get; set; }

    public uint Length { get; set; }
}
