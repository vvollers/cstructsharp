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

    /// <summary>The shape of a generated <c>Update</c> setter for a scalar member.</summary>
    /// <typeparam name="T">The member's value type.</typeparam>
    /// <param name="target">The encoded root struct to change in place.</param>
    /// <param name="value">The new member value.</param>
    private delegate void Setter<in T>(Span<byte> target, T value);

    /// <summary>The shape of a generated <c>Update</c> setter for one element of a fixed array.</summary>
    /// <typeparam name="T">The element's value type.</typeparam>
    /// <param name="target">The encoded root struct to change in place.</param>
    /// <param name="index">The zero-based element index.</param>
    /// <param name="value">The new element value.</param>
    private delegate void IndexedSetter<in T>(Span<byte> target, int index, T value);

    /// <summary>The shape of a generated path operation such as <c>ResolveAddress</c>.</summary>
    /// <typeparam name="TResult">The operation's result type.</typeparam>
    /// <param name="source">The encoded root struct.</param>
    /// <param name="path">The member path, starting at the root name.</param>
    /// <param name="variables">The caller variables.</param>
    /// <param name="options">The read options.</param>
    /// <returns>The operation's answer.</returns>
    private delegate TResult PathCall<out TResult>(ReadOnlySpan<byte> source, string path, IReadOnlyDictionary<string, int>? variables, ReadOptions? options);

    /// <summary>The shape of the generated <c>ParseWithDebug</c>.</summary>
    /// <typeparam name="TResult">The value and debug record pair type.</typeparam>
    /// <param name="source">The encoded root struct.</param>
    /// <param name="variables">The caller variables.</param>
    /// <param name="options">The read options.</param>
    /// <returns>The parsed value with its debug records.</returns>
    private delegate TResult DebugCall<out TResult>(ReadOnlySpan<byte> source, IReadOnlyDictionary<string, int>? variables, ReadOptions? options);

    /// <summary>
    ///     The generated size and offset constants, typed setters, path operations, and <c>ICStructGenerated</c>
    ///     implementation give the runtime's answers and bytes.
    /// </summary>
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

    /// <summary>
    ///     Parses, serializes, and try-parses through the static <c>ICStructGenerated&lt;T&gt;</c> members, and checks
    ///     that a three-byte prefix fails with the throwing reader's message.
    /// </summary>
    /// <typeparam name="T">The generated root class.</typeparam>
    /// <param name="bytes">The encoded root struct.</param>
    /// <returns>The serialized bytes, trimmed to the written length.</returns>
    private static byte[] RoundTrip<T>(byte[] bytes)
        where T : ICStructGenerated<T>
    {
        T value = T.Parse(bytes);
        Assert.AreEqual("root", T.RootName);
        Assert.IsNotNull(T.Layout);
        var destination = new byte[bytes.Length];
        int written = T.Serialize(value, destination);

        // The non-throwing reader through the interface: a whole value succeeds, a truncated one reports the failure.
        Assert.IsTrue(T.TryParse(bytes, out T? again, out CStructException? none));
        Assert.IsNotNull(again);
        Assert.IsNull(none);
        Assert.IsFalse(T.TryParse(bytes.AsSpan(0, 3), out T? missing, out CStructException? failure));
        Assert.IsNull(missing);
        Assert.AreEqual(Assert.Throws<CStructReadException>(() => T.Parse(bytes.AsSpan(0, 3))).Message, failure!.Message);
        return destination[..written];
    }

    /// <summary>Changes one member with the runtime's path update and with a generated setter.</summary>
    /// <param name="runtime">The runtime layout.</param>
    /// <param name="expected">The bytes the runtime updates in place.</param>
    /// <param name="actual">The bytes the generated setter updates in place.</param>
    /// <param name="path">The member path the runtime updates.</param>
    /// <param name="runtimeValue">The value the runtime writes.</param>
    /// <param name="setter">The generated setter.</param>
    /// <param name="generatedValue">
    ///     The setter's value when its type differs; <see langword="null"/> reuses the runtime value.
    /// </param>
    /// <param name="extra">
    ///     The setter's arguments when it takes more than a value, such as an index and a value.
    /// </param>
    private static void Apply(CStruct runtime, byte[] expected, byte[] actual, string path, object runtimeValue, MethodInfo setter, object? generatedValue, params object[] extra)
    {
        runtime.Update(expected.AsSpan(), path, runtimeValue);
        object[] arguments = extra.Length > 0 ? extra : [generatedValue ?? runtimeValue];
        Invoke(setter, actual, arguments);
    }

    /// <summary>
    ///     Invokes a generated setter whose first parameter is a span, rethrowing its exceptions unwrapped.
    /// </summary>
    /// <param name="setter">The generated setter.</param>
    /// <param name="target">The bytes to change in place.</param>
    /// <param name="arguments">The value, or the index and the value.</param>
    /// <exception cref="AssertFailedException">The setter's signature does not bind to the setter delegate.</exception>
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

    /// <summary>Binds a scalar setter to <see cref="Setter{T}"/> and calls it.</summary>
    /// <typeparam name="T">The member's value type.</typeparam>
    /// <param name="setter">The generated setter.</param>
    /// <param name="target">The bytes to change in place.</param>
    /// <param name="value">The new member value.</param>
    private static void Call<T>(MethodInfo setter, byte[] target, T value) => ((Setter<T>)Delegate.CreateDelegate(typeof(Setter<T>), setter))(target, value);

    /// <summary>Binds an array element setter to <see cref="IndexedSetter{T}"/> and calls it.</summary>
    /// <typeparam name="T">The element's value type.</typeparam>
    /// <param name="setter">The generated setter.</param>
    /// <param name="target">The bytes to change in place.</param>
    /// <param name="index">The zero-based element index.</param>
    /// <param name="value">The new element value.</param>
    private static void CallIndexed<T>(MethodInfo setter, byte[] target, int index, T value) => ((IndexedSetter<T>)Delegate.CreateDelegate(typeof(IndexedSetter<T>), setter))(target, index, value);

    /// <summary>Invokes a generated operation whose first parameter is a read-only span.</summary>
    /// <param name="method">A path operation, or <c>ParseWithDebug</c> when <paramref name="path"/> is null.</param>
    /// <param name="bytes">The encoded root struct.</param>
    /// <param name="path">The member path, or <see langword="null"/> for <c>ParseWithDebug</c>.</param>
    /// <returns>The operation's boxed result.</returns>
    private static object InvokeSpan(MethodInfo method, byte[] bytes, string? path)
    {
        // A ReadOnlySpan<byte> parameter cannot be boxed: bind through a delegate closed over the return type.
        MethodInfo helper = typeof(OperationTests).GetMethod(path is null ? nameof(CallDebug) : nameof(CallPath), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(method.ReturnType);
        return helper.Invoke(null, path is null ? [method, bytes] : [method, bytes, path])!;
    }

    /// <summary>
    ///     Binds a path operation to <see cref="PathCall{TResult}"/> and calls it without variables or options.
    /// </summary>
    /// <typeparam name="TResult">The operation's result type.</typeparam>
    /// <param name="method">The generated path operation.</param>
    /// <param name="bytes">The encoded root struct.</param>
    /// <param name="path">The member path.</param>
    /// <returns>The operation's answer.</returns>
    private static TResult CallPath<TResult>(MethodInfo method, byte[] bytes, string path)
        => ((PathCall<TResult>)Delegate.CreateDelegate(typeof(PathCall<TResult>), method))(bytes, path, null, null);

    /// <summary>
    ///     Binds <c>ParseWithDebug</c> to <see cref="DebugCall{TResult}"/> and calls it without variables or options.
    /// </summary>
    /// <typeparam name="TResult">The value and debug record pair type.</typeparam>
    /// <param name="method">The generated <c>ParseWithDebug</c>.</param>
    /// <param name="bytes">The encoded root struct.</param>
    /// <returns>The parsed value with its debug records.</returns>
    private static TResult CallDebug<TResult>(MethodInfo method, byte[] bytes)
        => ((DebugCall<TResult>)Delegate.CreateDelegate(typeof(DebugCall<TResult>), method))(bytes, null, null);
}
