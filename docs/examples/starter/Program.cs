using CStructSharp;
using CStructSharp.Values;

var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
byte[] bytes = { 0x02, 0x00, 0x06, 0x00, 0x00, 0x00 };
StructValue header = layout.Parse(bytes, "header");

Console.WriteLine($"kind = {header.Get<ushort>("kind")}");
Console.WriteLine($"length = {header.Get<uint>("length")}");
