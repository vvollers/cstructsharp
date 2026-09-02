using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Numerics;
using System.Reflection;
using System.Runtime.CompilerServices;
using CStructSharp;

const string Definition = "struct root { uint8 marker; uint16 value; uint8 *target; };";
byte[] input = [0xA5, 0x34, 0x12, 0x04, 0x7E,];
var cstruct = new CStruct(Definition, pointerSize: 1);
AssertInitOnlyOptions();

using (var stream = new MemoryStream(input))
{
    IDictionary<string, object?> parsed = cstruct.ParseStream(stream, "root");
    AssertEqual((byte)0xA5, parsed["marker"], "parsed marker");
    AssertEqual((ushort)0x1234, parsed["value"], "parsed value");

    var pointer = parsed["target"] as CStructSharp.Pointer ??
                  throw new InvalidOperationException("Parsed target is not a package Pointer value.");
    AssertEqual(4L, pointer.Address, "parsed pointer address");
    AssertEqual(false, pointer.IsNull, "parsed pointer null state");
    AssertEqual(true, pointer.IsDereferenced, "parsed pointer follow state");
    AssertEqual((byte)0x7E, pointer.Value, "parsed pointer target");

    stream.Position = 0;
    (List<DebugData> debugData, object debugResult) = cstruct.ParseStreamWithDebug(stream, "root");
    var debugRoot = ((IDictionary<string, object?>)debugResult)["root"] as IDictionary<string, object?> ??
                     throw new InvalidOperationException("Debug parsing did not return the named root object.");
    AssertEqual((byte)0xA5, debugRoot["marker"], "debug parse marker");
    foreach (string expectedPath in new[] { "root.marker", "root.value", "root.target", })
    {
        if (!debugData.Any(item => item.DebugStackString == expectedPath))
        {
            throw new InvalidOperationException($"Debug parsing did not report '{expectedPath}'.");
        }
    }

    AssertAddress(cstruct, stream, "root.value", 1);
    AssertAddress(cstruct, stream, "root.target.address", 3);
    AssertAddress(cstruct, stream, "root.target.value", 4);

    stream.Position = 0;
    AssertEqual(
        (ushort)0x1234,
        cstruct.ReadValue<ushort>(stream, "root.value"),
        "typed scalar read");

    stream.Position = 0;
    PackageRoot typed = cstruct.ReadValue<PackageRoot>(stream, "root");
    AssertEqual((byte)0xA5, typed.Marker, "typed POCO marker");
    AssertEqual((ushort)0x1234, typed.Value, "typed POCO value");
    AssertEqual(4L, typed.Target.Address, "typed POCO pointer");

    stream.Position = 0;
    if (cstruct.TryReadValue<DateTime>(stream, "root.value", out _))
    {
        throw new InvalidOperationException("Unsupported package conversion unexpectedly succeeded.");
    }

    AssertEqual(0L, stream.Position, "failed typed-read stream position");
}

var addressOnlyPointer = new CStructSharp.Pointer(4, null, 1);
AssertEqual(false, addressOnlyPointer.IsNull, "address-only pointer null state");
AssertEqual(false, addressOnlyPointer.IsDereferenced, "address-only pointer follow state");
AssertNull(addressOnlyPointer.Value, "address-only pointer target");

var nullPointerLayout = new CStruct("struct null_root { uint8 *target; };", pointerSize: 1);
IDictionary<string, object?> nullPointerResult = nullPointerLayout.Parse([0], "null_root");
var nullPointer = nullPointerResult["target"] as CStructSharp.Pointer ??
                  throw new InvalidOperationException("Null pointer parsing did not return a package Pointer value.");
AssertEqual(true, nullPointer.IsNull, "null pointer state");
AssertEqual(false, nullPointer.IsDereferenced, "null pointer follow state");
AssertNull(nullPointer.Value, "null pointer target");

IDictionary<string, object?> output = new ExpandoObject();
output["marker"] = (byte)0xA5;
output["value"] = (ushort)0x1234;
output["target"] = 4L;
byte[] expectedSerialized = [0xA5, 0x34, 0x12, 0x04,];
AssertBytes(expectedSerialized, cstruct.Serialize("root", output), "serialization");

IDictionary<string, object?> memoryParsed = cstruct.Parse(input.AsSpan(), "root");
AssertEqual((ushort)0x1234, memoryParsed["value"], "span parse");
PackageRoot memoryTyped = cstruct.ReadValue<PackageRoot>((ReadOnlyMemory<byte>)input, "root");
AssertEqual((byte)0x7E, memoryTyped.Target.Value, "memory typed pointer target");
if (!cstruct.TryReadValue((ReadOnlyMemory<byte>)input, out ushort memoryScalar, "root.value"))
{
    throw new InvalidOperationException("Memory typed scalar read unexpectedly failed.");
}

AssertEqual((ushort)0x1234, memoryScalar, "memory typed scalar");
Span<byte> spanOutput = stackalloc byte[expectedSerialized.Length];
AssertEqual(
    expectedSerialized.Length,
    cstruct.Serialize(spanOutput, "root", output),
    "span serialization length");
AssertBytes(expectedSerialized, spanOutput.ToArray(), "span serialization");
var bufferOutput = new ArrayBufferWriter<byte>();
AssertEqual(
    (long)expectedSerialized.Length,
    cstruct.Serialize(bufferOutput, "root", output),
    "buffer-writer serialization length");
AssertBytes(expectedSerialized, bufferOutput.WrittenSpan.ToArray(), "buffer-writer serialization");

using (var stream = new MemoryStream())
{
    cstruct.WriteStream(stream, "root", output);
    AssertBytes(expectedSerialized, stream.ToArray(), "stream write");
}

using (var stream = new MemoryStream((byte[])input.Clone()))
{
    stream.Position = 0;
    cstruct.UpdateStream(stream, "root.value", (ushort)0xABCD);
    AssertEqual(0L, stream.Position, "value-update stream position");
    AssertBytes([0xA5, 0xCD, 0xAB, 0x04, 0x7E,], stream.ToArray(), "value update");
}

using (var stream = new MemoryStream((byte[])input.Clone()))
{
    stream.Position = 0;
    cstruct.UpdateStream(stream, "root.target.value", (byte)0x55);
    AssertEqual(0L, stream.Position, "pointer-update stream position");
    AssertBytes([0xA5, 0x34, 0x12, 0x04, 0x55,], stream.ToArray(), "pointer target update");
}

using (var stream = new MemoryStream((byte[])input.Clone()))
{
    try
    {
        cstruct.UpdateStream(stream, "root", new { marker = (byte)0x11, });
        throw new InvalidOperationException("A late package update binding failure did not fail.");
    }
    catch (CStructWriteException)
    {
        AssertEqual(0L, stream.Position, "failed-update stream position");
        AssertBytes(input, stream.ToArray(), "validation-before-mutation update");
    }
}

using (var stream = new MemoryStream((byte[])input.Clone()) { Position = input.Length, })
{
    try
    {
        cstruct.UpdateStream(stream, "root.marker", (byte)0x11);
        throw new InvalidOperationException("A package update unexpectedly extended the destination.");
    }
    catch (CStructException)
    {
        AssertEqual((long)input.Length, stream.Position, "non-extending update stream position");
        AssertBytes(input, stream.ToArray(), "non-extending update");
    }
}

const string VariableDefinition = "#define COUNT 1\nstruct variable_root { uint8 values[COUNT]; };";
var variableLayout = new CStruct(VariableDefinition);
var variableSource = new Dictionary<string, int> { ["COUNT"] = 2, };
IReadOnlyDictionary<string, int> variables =
    new ReadOnlyDictionary<string, int>(variableSource);
using (var stream = new MemoryStream([0x11, 0x22,]))
{
    IDictionary<string, object?> parsed = variableLayout.ParseStream(stream, "variable_root", variables);
    var values = parsed["values"] as IList<object> ??
                 throw new InvalidOperationException("Read-only variables did not produce an array result.");
    AssertEqual(2, values.Count, "read-only variable array count");

    stream.Position = 0;
    byte[] typedValues = variableLayout.ReadValue<byte[]>(stream, "variable_root.values", variables);
    AssertBytes([0x11, 0x22,], typedValues, "read-only variable typed read");
}

var variableOutput = new Dictionary<string, object>
{
    ["values"] = new object[] { (byte)0x11, (byte)0x22, },
};
AssertBytes(
    [0x11, 0x22,],
    variableLayout.Serialize("variable_root", variableOutput, variables),
    "read-only variable serialization");
AssertEqual(1, variableSource.Count, "caller-owned variable count");
AssertEqual(2, variableSource["COUNT"], "caller-owned variable value");

const string UnionDefinition = "union choice { uint8 small; uint16 large; };";
var unionLayout = new CStruct(UnionDefinition, pointerSize: 1);
using (var stream = new MemoryStream([0x34, 0x12,]))
{
    object parsedValue = unionLayout.ParseStream(stream, "choice");
    var parsedUnion = parsedValue as UnionValue ??
                      throw new InvalidOperationException("Union parsing did not return a package UnionValue.");
    AssertBytes([0x34, 0x12,], parsedUnion.RawStorage!.Value.ToArray(), "union raw storage");
    AssertEqual((ushort)0x1234, parsedUnion["large"], "union member view");
    AssertBytes([0x34, 0x12,], unionLayout.Serialize("choice", parsedUnion), "raw union round-trip");
}

AssertBytes(
    [0xA5, 0x00,],
    unionLayout.Serialize("choice", UnionValue.FromMember("choice", "small", (byte)0xA5)),
    "selected union serialization");

const string EnumDefinition =
    "enum state : uint64 { Maximum = 18446744073709551615 }; struct enum_root { state value; };";
var enumLayout = new CStruct(EnumDefinition);
using (var stream = new MemoryStream([0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,]))
{
    IDictionary<string, object?> parsed = enumLayout.ParseStream(stream, "enum_root");
    var parsedEnum = parsed["value"] as EnumValueResult ??
                     throw new InvalidOperationException("Enum parsing did not return EnumValueResult.");
    AssertEqual(new BigInteger(ulong.MaxValue), parsedEnum.Value, "enum exact value");
    AssertEqual(ulong.MaxValue, parsedEnum.RawBits, "enum raw bits");
    AssertEqual(64, parsedEnum.BitWidth, "enum bit width");
    AssertEqual(false, parsedEnum.IsSigned, "enum signedness");
    AssertEqual("uint64", parsedEnum.StorageType, "enum storage type");
    AssertEqual("Maximum", parsedEnum.Name, "enum symbolic name");
    AssertBytes(
        [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF,],
        enumLayout.Serialize("enum_root", parsed),
        "enum exact round-trip");
}

using (var stream = new MemoryStream(input))
{
    try
    {
        _ = cstruct.ResolveAddress(stream, "root.missing");
        throw new InvalidOperationException("Missing package path did not fail.");
    }
    catch (CStructPathException exception)
    {
        CStructException domainException = exception;
        AssertEqual(CStructErrorCode.InvalidPath, domainException.Code, "stable exception code");
        AssertEqual("root.missing", domainException.Path, "safe exception path");
    }
}

Console.WriteLine($"CStructSharp package consumer smoke passed on {AppContext.TargetFrameworkName}.");

static void AssertAddress(CStruct cstruct, Stream stream, string path, long expected)
{
    stream.Position = 0;
    long actual = cstruct.ResolveAddress(stream, path);
    AssertEqual(expected, actual, $"address for {path}");
    AssertEqual(0L, stream.Position, $"address-resolution stream position for {path}");
}

static void AssertInitOnlyOptions()
{
    foreach (Type optionType in new[]
             {
                 typeof(CStructCompilationOptions),
                 typeof(ReadOptions),
                 typeof(WriteOptions),
                 typeof(UpdateOptions),
             })
    {
        foreach (PropertyInfo property in optionType.GetProperties(BindingFlags.Instance | BindingFlags.Public))
        {
            MethodInfo? setter = property.SetMethod;
            if (setter is null ||
                !setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(IsExternalInit)))
            {
                throw new InvalidOperationException(
                    $"{optionType.Name}.{property.Name} is not init-only in the installed package.");
            }
        }
    }
}

static void AssertBytes(byte[] expected, byte[] actual, string operation)
{
    if (!expected.AsSpan().SequenceEqual(actual))
    {
        throw new InvalidOperationException(
            $"{operation} produced {Convert.ToHexString(actual)} instead of {Convert.ToHexString(expected)}.");
    }
}

static void AssertNull(object? actual, string operation)
{
    if (actual is not null)
    {
        throw new InvalidOperationException($"{operation} produced '{actual}' instead of null.");
    }
}

static void AssertEqual<T>(T expected, object? actual, string operation)
{
    if (actual is not T typedActual || !EqualityComparer<T>.Default.Equals(expected, typedActual))
    {
        throw new InvalidOperationException($"{operation} produced '{actual}' instead of '{expected}'.");
    }
}

public sealed class PackageRoot
{
    public byte Marker { get; set; }

    public ushort Value { get; set; }

    public CStructSharp.Pointer Target { get; set; } = null!;
}
