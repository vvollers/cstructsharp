namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     The operations beside Parse and Serialize: the root class's <c>ICStructGenerated</c> implementation, the
///     <c>Sizes</c>/<c>Offsets</c> constants against the runtime's <c>sizeof</c>/<c>offsetof</c>, the typed
///     <c>Update</c> setters against the runtime's path update, and the path operations that run on the runtime.
/// </summary>
[TestClass]
public class OperationTests
{
    private const string Source = """"
        using CStructSharp;

        namespace Demo;

        [CStructLayout("""
            enum kind : uint8 { A = 1, B = 2 };
            struct hdr { uint16 length; kind tag; uint8 flags:3; uint8 level:5; };
            struct root { uint8 magic; hdr header; uint32 values[3]; uint8 n; uint8 data[n]; int24 wide; uint16 *link; };
            """, Root = "root", LittleEndian = false, Aligned = true, PointerSize = 2)]
        public static partial class Packet { }
        """";

    private delegate void Setter<in T>(Span<byte> target, T value);

    private delegate void IndexedSetter<in T>(Span<byte> target, int index, T value);

    private delegate TResult PathCall<out TResult>(ReadOnlySpan<byte> source, string path, IReadOnlyDictionary<string, int>? variables, ReadOptions? options);

    private delegate TResult DebugCall<out TResult>(ReadOnlySpan<byte> source, IReadOnlyDictionary<string, int>? variables, ReadOptions? options);

    [TestMethod]
    public void ConstantsSettersAndPathOperations_MatchTheRuntime()
    {
        GeneratorResult result = GeneratorRunner.Run(Source).AssertClean();
        Snapshot.Match("Operations.Packet", result.Source);
        Assembly assembly = result.Load();
        Type packet = assembly.GetType("Demo.Packet")!;
        var runtime = (CStruct)packet.GetProperty("Layout")!.GetValue(null)!;

        // Sizes and Offsets are the runtime's sizeof/offsetof.
        Assert.AreEqual(runtime.GetStructSizeInBytes("hdr"), (int)assembly.GetType("Demo.Packet+Sizes")!.GetField("Hdr")!.GetValue(null)!);
        Assert.IsNull(assembly.GetType("Demo.Packet+Sizes")!.GetField("Root"), "a runtime-sized root has no size constant");
        Type offsets = assembly.GetType("Demo.Packet+Offsets")!;
        Assert.AreEqual(0, (int)offsets.GetField("Magic")!.GetValue(null)!);
        Assert.AreEqual(2, (int)offsets.GetNestedType("Header")!.GetField("Length")!.GetValue(null)!);
        Assert.AreEqual(4, (int)offsets.GetNestedType("Header")!.GetField("Tag")!.GetValue(null)!);
        Assert.AreEqual(5, (int)offsets.GetNestedType("Header")!.GetField("Level")!.GetValue(null)!);
        Assert.AreEqual(8, (int)offsets.GetField("Values")!.GetValue(null)!);
        Assert.IsNull(offsets.GetField("Wide"), "a member after a runtime-sized array has no static offset");

        // Typed setters change exactly the bytes the runtime's Update changes.
        byte[] bytes = [7, 0, 0x01, 0x02, 1, 0b0001_0101, 0, 0, 0, 0, 0, 1, 0, 0, 0, 2, 0, 0, 0, 3, 2, 9, 8, 0, 0x10, 0x20, 0, 0x02];
        byte[] expected = (byte[])bytes.Clone();
        byte[] actual = (byte[])bytes.Clone();
        Type update = assembly.GetType("Demo.Packet+Update")!;
        Type header = update.GetNestedType("Header")!;
        Apply(runtime, expected, actual, "root.magic", (byte)9, update.GetMethod("Magic")!, null);
        Apply(runtime, expected, actual, "root.header.length", (ushort)0x0304, header.GetMethod("Length")!, null);
        Apply(runtime, expected, actual, "root.header.tag", "B", header.GetMethod("Tag")!, Enum.ToObject(assembly.GetType("Demo.Packet+Kind")!, 2));
        Apply(runtime, expected, actual, "root.header.flags", 6, header.GetMethod("Flags")!, (byte)6);
        Apply(runtime, expected, actual, "root.header.level", 17, header.GetMethod("Level")!, (byte)17);
        Apply(runtime, expected, actual, "root.values[1]", 0x11223344u, update.GetMethod("Values")!, null, 1, 0x11223344u);
        CollectionAssert.AreEqual(expected, actual);
        Assert.Throws<ArgumentOutOfRangeException>(() => Invoke(update.GetMethod("Values")!, actual, 3, 1u));

        // The path operations answer as the runtime does; ParseWithDebug pairs the generated value with the runtime's ranges.
        MethodInfo resolve = packet.GetMethod("ResolveAddress")!;
        Assert.AreEqual(runtime.ResolveAddress(bytes, "root.data[1]"), (long)InvokeSpan(resolve, bytes, "root.data[1]"));
        MethodInfo length = packet.GetMethod("GetArrayLength")!;
        Assert.AreEqual(2, (int)InvokeSpan(length, bytes, "root.data"));
        MethodInfo debug = packet.GetMethod("ParseWithDebug")!;
        object pair = InvokeSpan(debug, bytes, null);
        var debugRecords = (IReadOnlyList<DebugData>)pair.GetType().GetField("Item2")!.GetValue(pair)!;
        Assert.AreEqual(runtime.ParseWithDebug(bytes, "root").Debug.Count, debugRecords.Count);
        ParityComparer.AssertSame(runtime.Parse(bytes, "root"), pair.GetType().GetField("Item1")!.GetValue(pair), "root");

        // The root class implements ICStructGenerated<Root> with the class's own operations.
        Type root = assembly.GetType("Demo.Packet+Root")!;
        Type contract = typeof(ICStructGenerated<>).MakeGenericType(root);
        Assert.IsTrue(contract.IsAssignableFrom(root));
        MethodInfo generic = typeof(OperationTests).GetMethod(nameof(RoundTrip), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(root);
        CollectionAssert.AreEqual(runtime.Serialize("root", runtime.Parse(bytes, "root")), (byte[])generic.Invoke(null, [bytes])!);
    }

    private static byte[] RoundTrip<T>(byte[] bytes)
        where T : ICStructGenerated<T>
    {
        T value = T.Parse(bytes);
        Assert.AreEqual("root", T.RootName);
        Assert.IsNotNull(T.Layout);
        var destination = new byte[bytes.Length];
        int written = T.Serialize(value, destination);
        return destination[..written];
    }

    private static void Apply(CStruct runtime, byte[] expected, byte[] actual, string path, object runtimeValue, MethodInfo setter, object? generatedValue, params object[] extra)
    {
        runtime.Update(expected.AsSpan(), path, runtimeValue);
        object[] arguments = extra.Length > 0 ? extra : [generatedValue ?? runtimeValue];
        Invoke(setter, actual, arguments);
    }

    private static void Invoke(MethodInfo setter, byte[] target, params object[] arguments)
    {
        // A Span<byte> parameter cannot be boxed: bind through a delegate closed over the value type.
        MethodInfo helper = typeof(OperationTests).GetMethod(arguments.Length == 2 ? nameof(CallIndexed) : nameof(Call), BindingFlags.NonPublic | BindingFlags.Static)!
            .MakeGenericMethod(setter.GetParameters()[^1].ParameterType);
        try
        {
            helper.Invoke(null, arguments.Length == 2 ? [setter, target, arguments[0], arguments[1]] : [setter, target, arguments[0]]);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is ArgumentException { } bind && bind is not ArgumentOutOfRangeException)
        {
            throw new AssertFailedException(setter.Name + "(" + string.Join(", ", setter.GetParameters().Select(parameter => parameter.ParameterType.Name)) + "): " + bind.Message);
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
        }
    }

    private static void Call<T>(MethodInfo setter, byte[] target, T value) => ((Setter<T>)Delegate.CreateDelegate(typeof(Setter<T>), setter))(target, value);

    private static void CallIndexed<T>(MethodInfo setter, byte[] target, int index, T value) => ((IndexedSetter<T>)Delegate.CreateDelegate(typeof(IndexedSetter<T>), setter))(target, index, value);

    private static object InvokeSpan(MethodInfo method, byte[] bytes, string? path)
    {
        // A ReadOnlySpan<byte> parameter cannot be boxed: bind through a delegate closed over the return type.
        MethodInfo helper = typeof(OperationTests).GetMethod(path is null ? nameof(CallDebug) : nameof(CallPath), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(method.ReturnType);
        return helper.Invoke(null, path is null ? [method, bytes] : [method, bytes, path])!;
    }

    private static TResult CallPath<TResult>(MethodInfo method, byte[] bytes, string path)
        => ((PathCall<TResult>)Delegate.CreateDelegate(typeof(PathCall<TResult>), method))(bytes, path, null, null);

    private static TResult CallDebug<TResult>(MethodInfo method, byte[] bytes)
        => ((DebugCall<TResult>)Delegate.CreateDelegate(typeof(DebugCall<TResult>), method))(bytes, null, null);
}
