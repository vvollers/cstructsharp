using CStructSharp;

var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
byte[] bytes = { 0x02, 0x00, 0x06, 0x00, 0x00, 0x00 };
dynamic header = layout.Parse(bytes.AsSpan(), "header");

Console.WriteLine($"kind = {header.kind}");
Console.WriteLine($"length = {header.length}");

