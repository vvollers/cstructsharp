namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     The generated writer's validation against the runtime writer's: a value both readers produced is changed the
///     same way on both sides, and the two writers must then agree - the bytes, or the failure's type and message
///     (the offset stripped where the runtime's static write plan reports the block's start).
/// </summary>
[TestClass]
public class WriterParityTests
{
    private const string Arguments = "Root = \"root\", PointerSize = 2, Aligned = false, LittleEndian = true";

    [TestMethod]
    public void ValueRules_MatchTheRuntime()
    {
        const string Numbers = "struct root { int24 a; uint24 b; int48 c; uint48 d; char e; fixed16_16 f; uint8 g:3; uint8 h:5; int8 i; };";
        byte[] numbers = [1, 0, 0, 2, 0, 0, 3, 0, 0, 0, 0, 0, 4, 0, 0, 0, 0, 0, (byte)'x', 0, 0, 1, 0, 0x2B, 0xFF];
        Compare("int24-range", Numbers, numbers, "a", 8_388_608);
        Compare("uint24-range", Numbers, numbers, "b", 16_777_216u);
        Compare("int48-range", Numbers, numbers, "c", 1L << 47);
        Compare("uint48-range", Numbers, numbers, "d", 1UL << 48);
        Compare("char-range", Numbers, numbers, "e", 'Ā');
        Compare("fixed-grid", Numbers, numbers, "f", 1.0000001);
        Compare("fixed-range", Numbers, numbers, "f", 40000.0);
        Compare("bitfield-range", Numbers, numbers, "g", (byte)8);
        Compare("bitfield-ok", Numbers, numbers, "h", (byte)31);
        Compare("negative-ok", Numbers, numbers, "i", (sbyte)-5);

        const string Texts = "struct root { char name[4]; wchar wide[2]; utf8 label[3]; utf16le pair[4]; cstring tail; char open[]; };";
        byte[] texts = [(byte)'a', (byte)'b', 0, 0, (byte)'w', 0, 0, 0, (byte)'l', 0, 0, (byte)'p', 0, 0, 0, (byte)'t', 0, (byte)'o', 0];
        Compare("fixed-text-long", Texts, texts, "name", "abcde");
        Compare("fixed-text-narrow", Texts, texts, "name", "aĀ");
        Compare("wide-ok", Texts, texts, "wide", "éè");
        Compare("bounded-long", Texts, texts, "label", "éé");
        Compare("bounded-ok", Texts, texts, "label", "é");
        Compare("utf16-ok", Texts, texts, "pair", "éx");
        Compare("terminator-in-value", Texts, texts, "tail", "a\0b");
        Compare("terminated-ok", Texts, texts, "tail", "hello");
        Compare("flexible-ok", Texts, texts, "open", "world");

        const string Arrays = "struct pair { uint8 x; uint8 y; }; struct root { uint8 n; uint16 v[n]; uint8 grid[2][2]; pair items[2]; uint8 rest[EOF]; };";
        byte[] arrays = [2, 1, 0, 2, 0, 1, 2, 3, 4, 9, 8, 7, 6, 5, 4];
        Compare("count-mismatch", Arrays, arrays, "v", new ushort[] { 1, 2, 3 });
        Compare("count-fewer", Arrays, arrays, "v", Array.Empty<ushort>());
        Compare("grid-rows", Arrays, arrays, "grid", new byte[][] { [1, 2] });
        Compare("grid-columns", Arrays, arrays, "grid", new byte[][] { [1, 2], [3] });
        Compare("nested-count", Arrays, arrays, "items", Array.Empty<object>());
        Compare("rest-any", Arrays, arrays, "rest", new byte[] { 1, 2, 3, 4, 5, 6 });
    }

    [TestMethod]
    public void UnionsPointersAndConditionals_MatchTheRuntime()
    {
        const string Unions = "union choice { uint8 small; uint32 big; }; struct root { uint8 tag; choice value; };";
        byte[] unions = [1, 0x11, 0x22, 0x33, 0x44];
        CompareUnion("union-select-small", Unions, unions, "small", (byte)7, [1, 7, 0, 0, 0]);
        CompareUnion("union-select-big", Unions, unions, "big", 0x01020304u, [1, 4, 3, 2, 1]);
        CompareUnion("union-raw-short", Unions, unions, null, new byte[] { 1, 2 }, null);
        CompareUnion("union-unknown-member", Unions, unions, "missing", (byte)1, null);

        const string Pointers = "struct root { uint8 head; uint16 *ptr; uint8 tail; };";
        byte[] pointers = [1, 4, 0, 9, 0x2A, 0];

        // A negative address cannot be expressed: Pointer<T> rejects it in its constructor, so only the writer's rules remain.
        Compare("pointer-wide", Pointers, pointers, "ptr", 70000L);
        Compare("pointer-null", Pointers, pointers, "ptr", 0L);
        Compare("pointer-relative", Pointers, pointers, "ptr", 6L, new WriteOptions { AddressingMode = PointerAddressingMode.Relative, Origin = 2 });
        Compare("pointer-relative-negative", Pointers, pointers, "ptr", 1L, new WriteOptions { AddressingMode = PointerAddressingMode.Relative, Origin = 2 });

        const string Conditionals = "struct root { uint8 tag; if (tag == 1) { uint8 one; } else { uint16 two; } uint8 tail; };";
        byte[] one = [1, 5, 9];
        Compare("conditional-inactive-supplied", Conditionals, one, "two", (ushort)3);
        Compare("conditional-retag", Conditionals, one, "tag", (byte)0);
        Compare("conditional-ok", Conditionals, one, "one", (byte)6);

        const string Limits = "struct inner { uint8 v; }; struct root { inner a; uint8 n; uint8 data[n]; cstring text; };";
        byte[] limits = [1, 2, 3, 4, (byte)'h', (byte)'i', 0];
        Compare("limit-array", Limits, limits, "n", (byte)3, new WriteOptions { MaxArrayElements = 2 });
        Compare("limit-string", Limits, limits, "text", "hello", new WriteOptions { MaxStringBytes = 4 });
        Compare("limit-total", Limits, limits, "text", "hello", new WriteOptions { MaxTotalBytesWritten = 6 });
        Compare("limit-nesting", Limits, limits, "n", (byte)2, new WriteOptions { MaxNestingDepth = 1 });
    }

    private static void Compare(string id, string definition, byte[] bytes, string member, object newValue, WriteOptions? options = null)
    {
        (CStruct runtime, Type generatedClass, StructValue runtimeValue, object generatedValue) = Load(id, definition, bytes);
        runtimeValue[member] = newValue is object[] { Length: 0 } ? Array.Empty<object>() : newValue;
        SetProperty(generatedValue, member, newValue);
        AssertSameOutcome(id, runtime, generatedClass, runtimeValue, generatedValue, options);
    }

    private static void CompareUnion(string id, string definition, byte[] bytes, string? selectedMember, object payload, byte[]? expectedBytes)
    {
        (CStruct runtime, Type generatedClass, StructValue runtimeValue, object generatedValue) = Load(id, definition, bytes);
        object union = generatedValue.GetType().GetProperty("Value")!.GetValue(generatedValue)!;
        if (selectedMember is null)
        {
            runtimeValue["value"] = UnionValue.FromRaw("choice", (byte[])payload);
            union.GetType().GetProperty("RawStorage")!.SetValue(union, payload);
        }
        else
        {
            runtimeValue["value"] = UnionValue.FromMember("choice", selectedMember, payload);
            union.GetType().GetProperty("SelectedMember")!.SetValue(union, selectedMember);
            if (union.GetType().GetProperty(Pascal(selectedMember)) is { } property)
            {
                property.SetValue(union, payload);
            }
        }

        AssertSameOutcome(id, runtime, generatedClass, runtimeValue, generatedValue, null);
        if (expectedBytes is not null)
        {
            CollectionAssert.AreEqual(expectedBytes, runtime.Serialize("root", runtimeValue), id);
        }
    }

    private static void AssertSameOutcome(string id, CStruct runtime, Type generatedClass, StructValue runtimeValue, object generatedValue, WriteOptions? options)
    {
        MethodInfo serialize = generatedClass.GetMethods().Single(method => method.Name == "SerializeRoot" && method.GetParameters().Length == 3);
        Exception? runtimeError = Catch(() => runtime.Serialize("root", runtimeValue, options: options));
        Exception? generatedError = Catch(() => serialize.Invoke(null, [generatedValue, null, options]));
        Assert.AreEqual(runtimeError?.GetType(), generatedError?.GetType(), $"{id}: exception type ({runtimeError?.Message} vs {generatedError?.Message})");
        Assert.AreEqual(WriteParity.WithoutOffset(runtimeError?.Message), WriteParity.WithoutOffset(generatedError?.Message), id);
        if (runtimeError is null)
        {
            CollectionAssert.AreEqual(runtime.Serialize("root", runtimeValue, options: options), (byte[])serialize.Invoke(null, [generatedValue, null, options])!, id + ": bytes");
        }
    }

    private static (CStruct Runtime, Type GeneratedClass, StructValue RuntimeValue, object GeneratedValue) Load(string id, string definition, byte[] bytes)
    {
        string className = "Writer" + string.Concat(id.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
        string source = "using CStructSharp;\n\nnamespace Parity;\n\n[CStructLayout(" + ReaderParityTests.Literal(definition) + ", " + Arguments + ")]\npublic static partial class " + className + " { }\n";
        Type generated = GeneratorRunner.Run(source).AssertClean().Load().GetType("Parity." + className)!;
        var runtime = (CStruct)generated.GetProperty("Layout")!.GetValue(null)!;
        StructValue runtimeValue = runtime.Parse(bytes, "root");
        MethodInfo parse = generated.GetMethods().Single(method => method.Name == "ParseRoot" && method.GetParameters()[0].ParameterType == typeof(byte[]));
        object generatedValue = parse.Invoke(null, [bytes, null, null])!;
        ParityComparer.AssertSame(runtimeValue, generatedValue, "root");
        return (runtime, generated, runtimeValue, generatedValue);
    }

    private static void SetProperty(object target, string member, object newValue)
    {
        PropertyInfo property = target.GetType().GetProperty(Pascal(member)) ?? throw new AssertFailedException("No property " + member);
        Type type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        object converted = newValue switch
        {
            long address when type.IsGenericType && type.GetGenericTypeDefinition() == typeof(Generated.Pointer<>) => Activator.CreateInstance(type, address, 1, null, false)!,
            object[] { Length: 0 } when type.IsArray => Array.CreateInstance(type.GetElementType()!, 0),
            _ when type.IsInstanceOfType(newValue) => newValue,
            IConvertible convertible when type.IsPrimitive => Convert.ChangeType(convertible, type, System.Globalization.CultureInfo.InvariantCulture),
            _ => newValue,
        };
        property.SetValue(target, converted);
        if (target.GetType().GetProperty("Has" + Pascal(member)) is { } flag)
        {
            flag.SetValue(target, true);
        }
    }

    private static string Pascal(string name) => string.Concat(name.Split('_').Select(part => part.Length == 0 ? string.Empty : char.ToUpperInvariant(part[0]) + part.Substring(1)));

    private static Exception? Catch(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is CStructException inner)
        {
            return inner;
        }
        catch (CStructException exception)
        {
            return exception;
        }
    }
}
