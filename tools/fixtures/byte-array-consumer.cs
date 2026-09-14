using CStructSharp;

var layout = new CStruct("struct root { uint16 value; };");
byte[] bytes = { 2, 0 };
if ((ushort)layout.Parse(bytes).value != 2 ||
    !Equals(layout.ReadValue(bytes, "root.value"), (ushort)2) ||
    layout.ReadValue<ushort>(bytes, "root.value") != 2 ||
    !layout.TryReadValue(bytes, out ushort value, "root.value") || value != 2)
    throw new Exception("Byte-array overload results differ.");

if (layout.ReadValue<ushort>(bytes.AsSpan(), "root.value") != 2 ||
    layout.ReadValue<ushort>(bytes.AsMemory(), "root.value") != 2 ||
    layout.TryReadValue(Array.Empty<byte>(), out value, "root.value"))
    throw new Exception("Existing memory or failure semantics changed.");

Action[] nullCalls = {
    () => layout.Parse((byte[])null!),
    () => layout.ReadValue((byte[])null!),
    () => layout.ReadValue<ushort>((byte[])null!),
    () => layout.TryReadValue((byte[])null!, out ushort _),
};
foreach (var call in nullCalls)
{
    try { call(); throw new Exception("Null input was accepted."); }
    catch (ArgumentNullException error) when (error.ParamName == "source") { }
}
Func<byte[], string?, IReadOnlyDictionary<string, int>?, ReadOptions?, ushort> read = layout.ReadValue<ushort>;
if (read(bytes, "root.value", null, null) != 2)
    throw new Exception("Byte-array method group failed.");
Console.WriteLine("PASS byte-array consumer");
