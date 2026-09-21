namespace CStructSharp.Generated.Parity;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Generators.Tests;
using CStructSharp.Values;

/// <summary>
///     Every fixture layout of the repository, generated into this project (<c>Layouts.g.cs</c>) and compared with
///     the runtime on both target frameworks: the value the generated <c>Parse</c> produces equals the runtime's,
///     both writers reproduce it byte for byte, the debug ranges and the addresses of the paths a fixture lists
///     agree, and every truncated prefix fails the same way. A layout without bytes still proves it generates,
///     compiles, builds its runtime layout, agrees on every static size, and fails identically on empty input.
/// </summary>
[TestClass]
public class LayoutParityTests
{
    private static readonly Lazy<JsonDocument> Index = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "layouts.json"))));

    private delegate TResult DebugCall<out TResult>(ReadOnlySpan<byte> source, IReadOnlyDictionary<string, int>? variables, ReadOptions? options);

    [TestMethod]
    public void EveryLayout_GeneratesAndBuildsItsRuntimeLayout()
    {
        var failures = new List<string>();
        int count = 0;
        foreach (JsonElement layout in Index.Value.RootElement.GetProperty("layouts").EnumerateArray())
        {
            count++;
            Type generated = LayoutType(layout);
            try
            {
                var runtime = (CStruct)generated.GetProperty("Layout")!.GetValue(null)!;
                Assert.AreEqual(layout.GetProperty("definition").GetString(), generated.GetField("Definition")!.GetValue(null));

                // Every top-level declaration with a generated class and a static size agrees with the runtime's sizeof.
                Type? sizes = generated.GetNestedType("Sizes");
                foreach (string declaration in runtime.Layout.Declarations.Select(item => item.Name))
                {
                    string className = ClassName(generated, declaration);
                    FieldInfo? size = sizes?.GetField(className);
                    if (size is not null && generated.GetNestedType(className) is not null)
                    {
                        Assert.AreEqual(runtime.GetStructSizeInBytes(declaration), (int)size.GetValue(null)!, generated.Name + "." + className);
                    }
                }
            }
            catch (Exception exception) when (exception is AssertFailedException or CStructException or TargetInvocationException)
            {
                failures.Add(generated.FullName + ": " + (exception.InnerException ?? exception).Message);
            }
        }

        Assert.IsTrue(count >= 200, "expected the tool's layouts, found " + count);
        Assert.IsEmpty(failures, string.Join("\n", failures));
    }

    [TestMethod]
    public void ManualFixtures_ValuesWritesAddressesAndTruncations_MatchTheRuntime()
    {
        var failures = new List<string>();
        foreach (ManualFixture fixture in ManualFixtures.Load())
        {
            Type generated = Type.GetType("CStructSharp.Generated.Parity.Layouts.Manual." + Pascal(fixture.Id)) ?? throw new AssertFailedException("no layout class for " + fixture.Id);
            try
            {
                Compare(fixture.Id, generated, fixture.Root, Convert.FromHexString(fixture.Bytes!), fixture.Variables.Count == 0 ? null : fixture.Variables, null, null);
            }
            catch (Exception exception) when (exception is AssertFailedException or CStructException or TargetInvocationException or InvalidOperationException)
            {
                failures.Add(fixture.Id + ": " + (exception.InnerException ?? exception).Message);
            }
        }

        Assert.IsEmpty(failures, string.Join("\n\n", failures));
    }

    [TestMethod]
    public void BenchmarkFixtures_ValuesWritesAndTruncations_MatchTheRuntime()
    {
        var failures = new List<string>();
        foreach (BenchmarkFixture fixture in BenchmarkFixtures.Load(256 * 1024))
        {
            Type? generated = Type.GetType("CStructSharp.Generated.Parity.Layouts.Benchmarks." + Pascal(fixture.Id));
            if (generated is null)
            {
                continue;
            }

            try
            {
                Compare(fixture.Id, generated, fixture.Root, fixture.Bytes, fixture.Variables.Count == 0 ? null : fixture.Variables, fixture.ReadOptions, fixture.ExpectedError);
            }
            catch (Exception exception) when (exception is AssertFailedException or CStructException or TargetInvocationException or InvalidOperationException)
            {
                failures.Add(fixture.Id + ": " + (exception.InnerException ?? exception).Message);
            }
        }

        Assert.IsEmpty(failures, string.Join("\n\n", failures));
    }

    [TestMethod]
    public void ShapesAndConditionalCases_ValuesWritesAndTruncations_MatchTheRuntime()
    {
        var failures = new List<string>();
        foreach (JsonElement layout in Index.Value.RootElement.GetProperty("layouts").EnumerateArray())
        {
            string source = layout.GetProperty("source").GetString()!;
            if (source is not ("Shapes" or "Conditional"))
            {
                continue;
            }

            Type generated = LayoutType(layout);
            var runtime = (CStruct)generated.GetProperty("Layout")!.GetValue(null)!;
            string root = layout.GetProperty("root").GetString()!;
            byte[] bytes;
            if (source == "Shapes")
            {
                // The shape's values, written by the runtime, are the bytes both readers see.
                object shapeValue = ShapeValue(layout.GetProperty("values"))!;
                if (generated.GetNestedType(ClassName(generated, root))?.GetProperty("SelectedMember") is not null)
                {
                    // A union shape sets exactly one member.
                    KeyValuePair<string, object?> member = ((Dictionary<string, object?>)shapeValue).Single();
                    shapeValue = UnionValue.FromMember(root, member.Key, member.Value);
                }

                bytes = runtime.Serialize(root, shapeValue);
            }
            else
            {
                bytes = new byte[layout.GetProperty("size").GetInt32()];
                Array.Fill(bytes, (byte)layout.GetProperty("fill").GetInt32());
            }

            try
            {
                Compare(layout.GetProperty("id").GetString()!, generated, root, bytes, null, null, null);
            }
            catch (Exception exception) when (exception is AssertFailedException or CStructException or TargetInvocationException or InvalidOperationException)
            {
                failures.Add(layout.GetProperty("id").GetString() + ": " + (exception.InnerException ?? exception).Message);
            }
        }

        Assert.IsEmpty(failures, string.Join("\n\n", failures));
    }

    private static void Compare(string id, Type generated, string root, byte[] bytes, IReadOnlyDictionary<string, int>? variables, ReadOptions? options, string? expectedError)
    {
        var runtime = (CStruct)generated.GetProperty("Layout")!.GetValue(null)!;
        string rootClass = ClassName(generated, root);
        MethodInfo parse = generated.GetMethods().Single(method => method.Name == "Parse" + rootClass && method.GetParameters()[0].ParameterType == typeof(byte[]));
        MethodInfo serialize = generated.GetMethods().Single(method => method.Name == "Serialize" + rootClass && method.GetParameters().Length == 3);
        MethodInfo tryParse = generated.GetMethods().Single(method => method.Name == "TryParse" + rootClass && method.GetParameters().Length == 5 && method.GetParameters()[0].ParameterType == typeof(byte[]));
        if (expectedError is not null)
        {
            Exception? runtimeError = Catch(() => runtime.ReadValue(bytes, root, variables, options));
            Exception? generatedError = Catch(() => Invoke(parse, bytes, variables, options));
            Assert.IsNotNull(runtimeError, id + ": the runtime did not fail");
            Assert.AreEqual(runtimeError.GetType(), generatedError?.GetType(), id + ": exception type");
            Assert.AreEqual(runtimeError.Message, generatedError!.Message, id);
            return;
        }

        object runtimeValue = runtime.ReadValue(bytes, root, variables, options)!;
        object generatedValue = Invoke(parse, bytes, variables, options);
        ParityComparer.AssertSame(runtimeValue, generatedValue, root);

        if (runtimeValue is StructValue or UnionValue)
        {
            byte[] expected;
            try
            {
                expected = runtime.Serialize(root, runtimeValue, variables);
            }
            catch (CStructException)
            {
                Assert.Throws<CStructException>(() => Invoke(serialize, generatedValue, variables, null), id + ": the runtime refused to write the value back but the generated writer did not");
                return;
            }

            CollectionAssert.AreEqual(expected, (byte[])Invoke(serialize, generatedValue, variables, null), id + ": serialized bytes");

            // The awaitable stream forms: the same value through ParseAsync (a hidden-buffer stream, so the bytes are
            // copied) and the same bytes through WriteAsync, on both the generated class and the runtime.
            MethodInfo parseAsync = generated.GetMethods().Single(method => method.Name == "Parse" + rootClass + "Async");
            MethodInfo writeAsync = generated.GetMethods().Single(method => method.Name == "Write" + rootClass + "Async");
            using var asyncSource = new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: false);
            object generatedAsync = Await(parseAsync.Invoke(null, [asyncSource, variables, options, CancellationToken.None])!)!;
            ParityComparer.AssertSame(runtimeValue, generatedAsync, root);
            object runtimeAsync = runtime.ReadValueAsync(new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: false), root, variables, options).AsTask().Result!;
            CollectionAssert.AreEqual(expected, runtime.Serialize(root, runtimeAsync, variables), id + ": runtime ReadValueAsync value");
            using var generatedTarget = new MemoryStream();
            Await(writeAsync.Invoke(null, [generatedTarget, generatedValue, variables, null, CancellationToken.None])!);
            CollectionAssert.AreEqual(expected, generatedTarget.ToArray(), id + ": WriteAsync bytes");
            using var runtimeTarget = new MemoryStream();
            runtime.WriteAsync(runtimeTarget, root, runtimeValue, variables).AsTask().Wait();
            CollectionAssert.AreEqual(expected, runtimeTarget.ToArray(), id + ": runtime WriteAsync bytes");

            // The runtime's debug ranges through the generated ParseWithDebug (a root only), and the fixture's addresses.
            if (root == generated.GetField("RootName")!.GetValue(null) as string && runtimeValue is StructValue)
            {
                MethodInfo? withDebug = generated.GetMethod("ParseWithDebug");
                if (withDebug is not null)
                {
                    object pair = InvokeSpan(withDebug, bytes, variables, options);
                    var records = (IReadOnlyList<DebugData>)pair.GetType().GetField("Item2")!.GetValue(pair)!;
                    Assert.AreEqual(runtime.ParseWithDebug(bytes, root, variables, options).Debug.Count, records.Count, id + ": debug ranges");
                    ParityComparer.AssertSame(runtimeValue, pair.GetType().GetField("Item1")!.GetValue(pair), root);
                }
            }
        }

        foreach (int length in SweepLengths(bytes.Length))
        {
            byte[] prefix = bytes[..length];
            Exception? runtimeError = Catch(() => runtime.ReadValue(prefix, root, variables, options));
            Exception? generatedError = Catch(() => Invoke(parse, prefix, variables, options));
            Assert.AreEqual(runtimeError?.GetType(), generatedError?.GetType(), $"{id} truncated to {length}: exception type ({runtimeError?.Message} vs {generatedError?.Message})");
            Assert.AreEqual(runtimeError?.Message, generatedError?.Message, $"{id} truncated to {length}");

            // The non-throwing form reports the same failure without throwing; a stream is left at its origin.
            object?[] attempt = [prefix, null, null, variables, options];
            bool succeeded = (bool)tryParse.Invoke(null, attempt)!;
            Assert.AreEqual(generatedError is null, succeeded, $"{id} truncated to {length}: TryParse");
            Assert.AreEqual(generatedError?.Message, (attempt[2] as CStructException)?.Message, $"{id} truncated to {length}: TryParse failure");
            Assert.AreEqual(generatedError is null, attempt[1] is not null, $"{id} truncated to {length}: TryParse value");
        }
    }

    /// <summary>A shape fixture's value as the runtime writer takes it: integers, doubles, nested objects, and arrays.</summary>
    private static object? ShapeValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
        case JsonValueKind.Number:
            // Boxed as the narrowest integer that holds it (a conditional expression would promote every branch to double).
            if (element.TryGetInt32(out int integer))
            {
                return integer;
            }

            return element.TryGetInt64(out long wide) ? wide : element.GetDouble();
        case JsonValueKind.String:
            return element.GetString();
        case JsonValueKind.True:
            return true;
        case JsonValueKind.False:
            return false;
        case JsonValueKind.Array:
            return element.EnumerateArray().Select(ShapeValue).ToArray();
        case JsonValueKind.Object:
            {
                var nested = new Dictionary<string, object?>(StringComparer.Ordinal);
                foreach (JsonProperty property in element.EnumerateObject())
                {
                    nested[property.Name] = ShapeValue(property.Value);
                }

                return nested;
            }

        default:
            return null;
        }
    }

    private static Type LayoutType(JsonElement layout)
        => Type.GetType("CStructSharp.Generated.Parity.Layouts." + layout.GetProperty("source").GetString() + "." + layout.GetProperty("className").GetString())
           ?? throw new AssertFailedException("no layout class for " + layout.GetProperty("id").GetString());

    /// <summary>The generated class name of a layout composite: the nested type whose Definition name matches (PascalCase, or kept).</summary>
    private static string ClassName(Type generated, string layoutName)
    {
        foreach (Type nested in generated.GetNestedTypes())
        {
            if (nested.Name == layoutName || nested.Name == Pascal(layoutName))
            {
                return nested.Name;
            }
        }

        return Pascal(layoutName);
    }

    private static string Pascal(string id) => string.Concat(id.Split('-', '_').Where(part => part.Length > 0).Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));

    private static IEnumerable<int> SweepLengths(int length)
    {
        if (length <= 1024)
        {
            for (int prefix = 0; prefix < length; prefix++)
            {
                yield return prefix;
            }

            yield break;
        }

        for (int prefix = 0; prefix < 64; prefix++)
        {
            yield return prefix;
        }

        for (int prefix = 64; prefix < length; prefix += Math.Max(1, length / 97))
        {
            yield return prefix;
        }

        for (int prefix = Math.Max(64, length - 16); prefix < length; prefix++)
        {
            yield return prefix;
        }
    }

    /// <summary>Completes a generated <c>ValueTask</c>/<c>ValueTask&lt;T&gt;</c> obtained through reflection and returns its result (<see langword="null"/> for a plain task).</summary>
    private static object? Await(object task)
    {
        try
        {
            Type type = task.GetType();
            var asTask = (Task)type.GetMethod("AsTask")!.Invoke(task, null)!;
            asTask.Wait();
            return type.IsGenericType ? asTask.GetType().GetProperty("Result")!.GetValue(asTask) : null;
        }
        catch (AggregateException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static object Invoke(MethodInfo method, object first, IReadOnlyDictionary<string, int>? variables, object? options)
    {
        try
        {
            return method.Invoke(null, [first, variables, options])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static object InvokeSpan(MethodInfo method, byte[] bytes, IReadOnlyDictionary<string, int>? variables, ReadOptions? options)
    {
        MethodInfo helper = typeof(LayoutParityTests).GetMethod(nameof(CallDebug), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(method.ReturnType);
        return helper.Invoke(null, [method, bytes, variables, options])!;
    }

    private static TResult CallDebug<TResult>(MethodInfo method, byte[] bytes, IReadOnlyDictionary<string, int>? variables, ReadOptions? options)
        => ((DebugCall<TResult>)Delegate.CreateDelegate(typeof(DebugCall<TResult>), method))(bytes, variables, options);

    private static Exception? Catch(Func<object?> action)
    {
        try
        {
            action();
            return null;
        }
        catch (CStructException exception)
        {
            return exception;
        }
    }
}
