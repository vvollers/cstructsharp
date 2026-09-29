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
///     both writers reproduce it byte for byte, the debug ranges agree, the runtime resolves each member to the
///     address the reader placed it at and the generated offset constants name, and every truncated prefix fails the
///     same way. A layout without bytes still proves it generates,
///     compiles, builds its runtime layout, agrees on every static size, and fails identically on empty input.
/// </summary>
[TestClass]
public class LayoutParityTests
{
    private static readonly Lazy<JsonDocument> Index = new(() => JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "layouts.json"))));

    /// <summary>The shape of the generated <c>ParseWithDebug</c>.</summary>
    /// <typeparam name="TResult">The value and debug record pair type.</typeparam>
    /// <param name="source">The encoded root struct.</param>
    /// <param name="variables">The caller variables.</param>
    /// <param name="options">The read options.</param>
    /// <returns>The parsed value with its debug records.</returns>
    private delegate TResult DebugCall<out TResult>(ReadOnlySpan<byte> source, IReadOnlyDictionary<string, int>? variables, ReadOptions? options);

    /// <summary>
    ///     Every indexed layout (at least 200) has a generated class whose definition, runtime layout, and static sizes
    ///     match the runtime.
    /// </summary>
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

    /// <summary>
    ///     Every manual fixture's generated class reads, writes, streams, enumerates records, and fails on truncation
    ///     as the runtime does.
    /// </summary>
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

    /// <summary>
    ///     Every benchmark fixture up to 256 KiB with a generated class matches the runtime's values, writes, expected
    ///     errors, and truncation failures.
    /// </summary>
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

    /// <summary>
    ///     The shape layouts, over bytes the runtime writes from their values, and the conditional cases, over filled
    ///     bytes, match the runtime's values, writes, and truncation failures.
    /// </summary>
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

    /// <summary>
    ///     Asserts that a generated class matches the runtime for one input: the value or expected error, the array
    ///     and asynchronous forms, record sequences, debug ranges, addresses, and every truncated prefix through
    ///     <c>Parse</c> and <c>TryParse</c>.
    /// </summary>
    /// <remarks>
    ///     Values are compared strictly (<see cref="ParityComparer"/>): besides equal content, the generated properties
    ///     follow the runtime's member order, scalars have the same CLR type, and the runtime returns the array kind the
    ///     generated element type implies. The runtime/generator differences named in the remarks on
    ///     <see cref="ParityComparer"/> stay lenient: bitfields (read as <see cref="int"/> or <see cref="ulong"/> by the
    ///     runtime) are compared by value, and the kind of a union's array member or of an empty array is not checked.
    /// </remarks>
    /// <param name="id">The case name used in failure messages.</param>
    /// <param name="generated">The generated layout class.</param>
    /// <param name="root">The layout name of the composite to read.</param>
    /// <param name="bytes">The input.</param>
    /// <param name="variables">The caller variables, or <see langword="null"/> for none.</param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <param name="expectedError">
    ///     A non-null value means both readers must fail with the same exception; the name itself is not checked here.
    /// </param>
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
        ParityComparer.AssertSame(runtimeValue, generatedValue, root, strict: true);

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
            ParityComparer.AssertSame(runtimeValue, generatedAsync, root, strict: true);
            object runtimeAsync = runtime.ReadValueAsync(new MemoryStream(bytes, 0, bytes.Length, writable: false, publiclyVisible: false), root, variables, options).AsTask().Result!;
            CollectionAssert.AreEqual(expected, runtime.Serialize(root, runtimeAsync, variables), id + ": runtime ReadValueAsync value");
            using var generatedTarget = new MemoryStream();
            Await(writeAsync.Invoke(null, [generatedTarget, generatedValue, variables, null, CancellationToken.None])!);
            CollectionAssert.AreEqual(expected, generatedTarget.ToArray(), id + ": WriteAsync bytes");
            using var runtimeTarget = new MemoryStream();
            runtime.WriteAsync(runtimeTarget, root, runtimeValue, variables).AsTask().Wait();
            CollectionAssert.AreEqual(expected, runtimeTarget.ToArray(), id + ": runtime WriteAsync bytes");

            // Record sequences: the fixture's bytes as one record, three times over, and with the third record cut
            // short, through the runtime's ParseMany and the generated Records<Root> - the same records member by
            // member, or the same failure (trailing bytes, the truncated record, a record that consumes no bytes).
            if (runtimeValue is StructValue)
            {
                MethodInfo records = generated.GetMethods().Single(method => method.Name == "Records" + rootClass && method.GetParameters()[0].ParameterType == typeof(ReadOnlyMemory<byte>));
                foreach ((string label, byte[] input) in new (string, byte[])[] { ("one record", bytes), ("three records", [.. bytes, .. bytes, .. bytes]), ("a truncated third record", [.. bytes, .. bytes, .. bytes[..Math.Max(0, bytes.Length - 1)]]), })
                {
                    var runtimeRecords = new List<StructValue>();
                    Exception? runtimeError = Catch(() =>
                    {
                        runtimeRecords.AddRange(runtime.ParseMany(new ReadOnlyMemory<byte>(input), root, variables, options));
                        return null;
                    });
                    var generatedRecords = new List<object>();
                    Exception? generatedError = Catch(() =>
                    {
                        generatedRecords.AddRange(((System.Collections.IEnumerable)Invoke(records, new ReadOnlyMemory<byte>(input), variables, options)).Cast<object>());
                        return null;
                    });
                    Assert.AreEqual(runtimeError?.GetType(), generatedError?.GetType(), $"{id} {label}: Records exception type ({runtimeError?.Message} vs {generatedError?.Message})");
                    Assert.AreEqual(runtimeError?.Message, generatedError?.Message, $"{id} {label}: Records");
                    Assert.AreEqual(runtimeRecords.Count, generatedRecords.Count, $"{id} {label}: record count");
                    for (int index = 0; index < runtimeRecords.Count; index++)
                    {
                        ParityComparer.AssertSame(runtimeRecords[index], generatedRecords[index], $"[{index}].{root}", strict: true);
                    }
                }
            }

            // The runtime's debug ranges through the generated ParseWithDebug (a root only). The runtime's ResolveAddress
            // must agree with where the reader placed each value and with the generator's static offsetof constants.
            if (root == generated.GetField("RootName")!.GetValue(null) as string && runtimeValue is StructValue structValue)
            {
                MethodInfo? withDebug = generated.GetMethod("ParseWithDebug");
                if (withDebug is not null)
                {
                    object pair = InvokeSpan(withDebug, bytes, variables, options);
                    var records = (IReadOnlyList<DebugData>)pair.GetType().GetField("Item2")!.GetValue(pair)!;
                    Assert.AreEqual(runtime.ParseWithDebug(bytes, root, variables, options).Debug.Count, records.Count, id + ": debug ranges");
                    ParityComparer.AssertSame(runtimeValue, pair.GetType().GetField("Item1")!.GetValue(pair), root, strict: true);
                    Assert.IsGreaterThan(0, AssertDebugAddresses(id, runtime, bytes, variables, options, structValue, root, records), id + ": no address compared");
                }

                if (generated.GetNestedType("Offsets") is { } offsets)
                {
                    int checkedOffsets = AssertStaticOffsets(id, runtime, bytes, variables, options, offsets, structValue, generatedValue.GetType(), root);
                    Assert.AreEqual(CountConstants(offsets), checkedOffsets, id + ": every generated offset is compared with an address");
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

    /// <summary>
    ///     Asserts that the runtime's <c>ResolveAddress</c> of representative paths is the byte where the reader placed
    ///     the value: every member, nested members, and of each array its first and last elements.
    /// </summary>
    /// <remarks>
    ///     The expected address is the start of the value's debug record: the reader places each value while it decodes,
    ///     and <c>ResolveAddress</c> walks the layout to one path without building values, so the two implementations
    ///     must agree on every offset, alignment gap, runtime count and conditional member. The value is walked in read
    ///     order, pairing each leaf with the next record of its debug path. A debug path names the element of an array of
    ///     composites by index but not the element of an array of scalars, so the scalar elements of one array share it.
    ///     Pointer targets are not walked. The elements between the first and the last follow the same path arithmetic;
    ///     skipping them keeps a large array from costing one traversal per element.
    /// </remarks>
    /// <param name="id">The case name used in failure messages.</param>
    /// <param name="runtime">The runtime layout.</param>
    /// <param name="bytes">The input.</param>
    /// <param name="variables">The caller variables, or <see langword="null"/> for none.</param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <param name="value">The runtime's value of the root.</param>
    /// <param name="root">The root's layout name, the first segment of every path.</param>
    /// <param name="records">The debug records of the whole root, in read order.</param>
    /// <returns>The number of addresses compared.</returns>
    private static int AssertDebugAddresses(string id, CStruct runtime, byte[] bytes, IReadOnlyDictionary<string, int>? variables, ReadOptions? options, StructValue value, string root, IReadOnlyList<DebugData> records)
    {
        var pending = new Dictionary<string, Queue<DebugData>>(StringComparer.Ordinal);
        foreach (DebugData record in records)
        {
            if (!pending.TryGetValue(record.Path, out Queue<DebugData>? queue))
            {
                queue = new Queue<DebugData>();
                pending.Add(record.Path, queue);
            }

            queue.Enqueue(record);
        }

        int compared = 0;
        Walk(value, root, root, true);
        return compared;

        // Pairs each leaf of a value with its debug record and compares the selected leaves' addresses.
        void Walk(object? item, string path, string debugPath, bool selected)
        {
            switch (item)
            {
            case StructValue structValue:
                foreach (KeyValuePair<string, object?> member in structValue)
                {
                    Walk(member.Value, path + "." + member.Key, debugPath + "." + member.Key, selected);
                }

                return;
            case UnionValue union:
                foreach (KeyValuePair<string, object?> member in union.Members)
                {
                    Walk(member.Value, path + "." + member.Key, debugPath + "." + member.Key, selected);
                }

                return;
            case System.Collections.IList list and not byte[]:
                for (int index = 0; index < list.Count; index++)
                {
                    bool composite = list[index] is StructValue or UnionValue;
                    string suffix = "[" + index + "]";
                    Walk(list[index], path + suffix, composite ? debugPath + suffix : debugPath, selected && (index == 0 || index == list.Count - 1));
                }

                return;
            default:
                Assert.IsTrue(pending.TryGetValue(debugPath, out Queue<DebugData>? queue) && queue.Count > 0, id + ": no debug record for " + path);
                DebugData record = queue.Dequeue();
                if (selected)
                {
                    Assert.AreEqual(record.Start, runtime.ResolveAddress(bytes, path, variables, options), id + ": address of " + path);
                    compared++;
                }

                return;
            }
        }
    }

    /// <summary>
    ///     Asserts that every constant of a generated <c>Offsets</c> class equals the runtime's <c>ResolveAddress</c> of
    ///     the member it names, descending into the nested class of each statically placed struct member.
    /// </summary>
    /// <remarks>
    ///     The generator computes these <c>offsetof</c> constants at build time from the static placement of the members;
    ///     the runtime resolves the same member by walking the layout over the input. The input starts at the root, so an
    ///     offset from the root's first byte is also the member's address.
    /// </remarks>
    /// <param name="id">The case name used in failure messages.</param>
    /// <param name="runtime">The runtime layout.</param>
    /// <param name="bytes">The input.</param>
    /// <param name="variables">The caller variables, or <see langword="null"/> for none.</param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <param name="offsets">The generated <c>Offsets</c> class, or one of its nested classes.</param>
    /// <param name="value">The runtime's value of the struct the class describes.</param>
    /// <param name="generatedType">The generated class of that struct, whose property names the constants reuse.</param>
    /// <param name="path">The runtime path of that struct.</param>
    /// <returns>The number of constants compared.</returns>
    private static int AssertStaticOffsets(string id, CStruct runtime, byte[] bytes, IReadOnlyDictionary<string, int>? variables, ReadOptions? options, Type offsets, StructValue value, Type generatedType, string path)
    {
        int compared = 0;
        foreach (KeyValuePair<string, object?> member in value)
        {
            PropertyInfo? property = ParityComparer.FindProperty(generatedType, member.Key);
            if (property is null)
            {
                continue;
            }

            // A member named like its enclosing class is emitted with a Member suffix (C# forbids the clash).
            string memberPath = path + "." + member.Key;
            FieldInfo? constant = offsets.GetField(property.Name) ?? offsets.GetField(property.Name + "Member");
            if (constant is { IsLiteral: true })
            {
                Assert.AreEqual((long)(int)constant.GetRawConstantValue()!, runtime.ResolveAddress(bytes, memberPath, variables, options), id + ": offset of " + memberPath);
                compared++;
            }

            if (offsets.GetNestedType(property.Name) is { } nested && member.Value is StructValue nestedValue)
            {
                compared += AssertStaticOffsets(id, runtime, bytes, variables, options, nested, nestedValue, property.PropertyType, memberPath);
            }
        }

        return compared;
    }

    /// <summary>Counts the constants of a generated <c>Offsets</c> class and its nested classes.</summary>
    /// <param name="offsets">The class.</param>
    /// <returns>The number of constants.</returns>
    private static int CountConstants(Type offsets)
        => offsets.GetFields().Count(field => field.IsLiteral) + offsets.GetNestedTypes().Sum(CountConstants);

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

    /// <summary>Finds the generated class for an entry of <c>layouts.json</c>.</summary>
    /// <param name="layout">The index entry with its <c>source</c>, <c>className</c>, and <c>id</c>.</param>
    /// <returns>The generated class.</returns>
    /// <exception cref="AssertFailedException">No generated class has the entry's name.</exception>
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

    /// <summary>Converts a kebab-case or snake_case name to PascalCase by capitalizing each part.</summary>
    /// <param name="id">The fixture id or layout name.</param>
    /// <returns>The PascalCase name.</returns>
    private static string Pascal(string id) => string.Concat(id.Split('-', '_').Where(part => part.Length > 0).Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));

    /// <summary>
    ///     Chooses the prefix lengths to try: every prefix of an input up to 1024 bytes; for a larger one, the first
    ///     64, about 97 spread prefixes, and the last 16.
    /// </summary>
    /// <param name="length">The input length in bytes.</param>
    /// <returns>Prefix lengths shorter than the input.</returns>
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

    /// <summary>Invokes a static generated method with three arguments, rethrowing its exceptions unwrapped.</summary>
    /// <param name="method">The generated reader, writer, or record sequence method.</param>
    /// <param name="first">The input or the value to write.</param>
    /// <param name="variables">The caller variables, or <see langword="null"/> for none.</param>
    /// <param name="options">The read or write options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The method's result.</returns>
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

    /// <summary>Invokes the generated <c>ParseWithDebug</c>, whose read-only span parameter cannot be boxed.</summary>
    /// <param name="method">The generated <c>ParseWithDebug</c>.</param>
    /// <param name="bytes">The encoded root struct.</param>
    /// <param name="variables">The caller variables, or <see langword="null"/> for none.</param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The boxed pair of the value and its debug records.</returns>
    private static object InvokeSpan(MethodInfo method, byte[] bytes, IReadOnlyDictionary<string, int>? variables, ReadOptions? options)
    {
        MethodInfo helper = typeof(LayoutParityTests).GetMethod(nameof(CallDebug), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(method.ReturnType);
        return helper.Invoke(null, [method, bytes, variables, options])!;
    }

    /// <summary>Binds <c>ParseWithDebug</c> to <see cref="DebugCall{TResult}"/> and calls it.</summary>
    /// <typeparam name="TResult">The value and debug record pair type.</typeparam>
    /// <param name="method">The generated <c>ParseWithDebug</c>.</param>
    /// <param name="bytes">The encoded root struct.</param>
    /// <param name="variables">The caller variables, or <see langword="null"/> for none.</param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The parsed value with its debug records.</returns>
    private static TResult CallDebug<TResult>(MethodInfo method, byte[] bytes, IReadOnlyDictionary<string, int>? variables, ReadOptions? options)
        => ((DebugCall<TResult>)Delegate.CreateDelegate(typeof(DebugCall<TResult>), method))(bytes, variables, options);

    /// <summary>Runs an operation and returns the <see cref="CStructException"/> it throws.</summary>
    /// <param name="action">The operation to run.</param>
    /// <returns>
    ///     The exception, or <see langword="null"/> when the operation succeeds; other exceptions propagate.
    /// </returns>
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
