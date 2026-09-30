namespace CStructSharp.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using CStructSharp.Diagnostics;
using CStructSharp.Generated;
using CStructSharp.Reading;
using CStructSharp.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
///     Pins the direct paths for whole fixed roots in memory - <c>Parse</c>, <c>ReadValue</c>, <c>ReadValue&lt;T&gt;</c>
///     and both <c>Serialize</c> forms run the static plan straight over the caller's span - together with the typed
///     conversions and the allocation-free path walk they rely on. Each must be indistinguishable from the general
///     reader and writer: the same values, bytes, exception types, messages, paths and offsets, for every truncation,
///     budget and limit. The internal <see cref="ExecutionPath.NoDirectAccess"/> option routes a call through the general path.
/// </summary>
[TestClass]
public class DirectRootAccessTests
{
    private const string Layout = """
        enum kind : uint8 { a = 1, b = 2 };
        struct vec { float32 x; float32 y; };
        typedef struct _rec { uint16 id; kind which; char tag[4]; vec pos; uint32 samples[3]; bool ok; } rec;
        struct plain { uint8 a; uint32 b; uint16 c[2]; };
        """;

    private static readonly string[] Roots = ["plain", "rec", "_rec"];

    /// <summary>Parse, ReadValue and ReadValue&lt;T&gt; of a whole fixed root agree with the general path on every input and limit.</summary>
    [TestMethod]
    public void DirectRead_MatchesGeneralReader_OnValuesFailuresAndContext()
    {
        foreach (bool aligned in new[] { false, true })
        {
            var layout = new CStruct(Layout, aligned: aligned);
            foreach (string root in Roots)
            {
                byte[] bytes = SampleBytes(layout, root);
                var cases = new List<(string Label, byte[] Input, ReadOptions? Options)>();
                for (int length = 0; length <= bytes.Length; length++)
                {
                    cases.Add(($"truncated to {length}", bytes[..length], null));
                }

                for (long budget = 1; budget <= bytes.Length; budget++)
                {
                    cases.Add(($"budget {budget}", bytes, new ReadOptions { MaxTotalBytesRead = budget }));
                }

                for (int limit = 1; limit <= 3; limit++)
                {
                    cases.Add(($"nesting {limit}", bytes, new ReadOptions { MaxNestingDepth = limit }));
                    cases.Add(($"arrays {limit}", bytes, new ReadOptions { MaxArrayElements = limit }));
                }

                cases.Add(("trimmed text", bytes, new ReadOptions { TrimFixedText = true }));
                cases.Add(("longer input", [.. bytes, 0xEE, 0xFF], null));
                cases.Add(("invalid limit", bytes, new ReadOptions { MaxArrayElements = -1 }));
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                cases.Add(("cancelled", bytes, new ReadOptions { CancellationToken = cancelled.Token }));

                foreach ((string label, byte[] input, ReadOptions? options) in cases)
                {
                    string caseLabel = $"aligned={aligned} {root} {label}";
                    AssertSame(o => layout.Parse(input, root, options: o), options, caseLabel + " / Parse");
                    AssertSame(o => layout.ReadValue(input, root, options: o), options, caseLabel + " / ReadValue");
                    AssertSame(o => layout.Parse(input, root, new Dictionary<string, int>(), o), options, caseLabel + " / variables");
                    if (root == "plain")
                    {
                        AssertSame(o => layout.ReadValue<PlainRecord>(input, root, options: o), options, caseLabel + " / ReadValue<T>");
                        AssertSame(o => layout.ReadValue<NarrowPlain>(input, root, options: o), options, caseLabel + " / failing mapper");
                        AssertSame(o => layout.ReadValue<ThrowingPlain>(input, root, options: o), options, caseLabel + " / throwing mapper");
                    }
                }
            }
        }
    }

    /// <summary>Serialize into a span and into a new array agree with the general path on bytes, counts and failures, and a failed write leaves the destination untouched.</summary>
    [TestMethod]
    public void DirectWrite_MatchesGeneralWriter_OnBytesFailuresAndContext()
    {
        foreach (bool aligned in new[] { false, true })
        {
            var layout = new CStruct(Layout, aligned: aligned);
            int size = layout.GetStructSizeInBytes("plain");
            StructValue parsed = layout.Parse(SampleBytes(layout, "plain"), "plain");
            var values = new List<(string Label, object Value)>
            {
                ("parsed", parsed),
                ("mapped", new PlainRecord { A = 7, B = 0x01020304, C = [5, 6] }),
                ("wrapped", new Dictionary<string, object?> { ["plain"] = parsed }),
                ("plain arrays", new Dictionary<string, object?> { ["a"] = (byte)1, ["b"] = 2u, ["c"] = new ushort[] { 3, 4 } }),
                ("short array", new Dictionary<string, object?> { ["a"] = (byte)1, ["b"] = 2u, ["c"] = new ushort[] { 3 } }),
                ("missing member", new Dictionary<string, object?> { ["a"] = (byte)1, ["c"] = new ushort[] { 3, 4 } }),
                ("extra member", new Dictionary<string, object?> { ["a"] = (byte)1, ["b"] = 2u, ["c"] = new ushort[] { 3, 4 }, ["z"] = 1 }),
                ("out of range", new Dictionary<string, object?> { ["a"] = 300, ["b"] = 2u, ["c"] = new ushort[] { 3, 4 } }),
                ("covariant array", new Dictionary<string, object?> { ["a"] = (byte)1, ["b"] = 2u, ["c"] = (object)new short[] { -1, 4 } }),
            };
            var options = new List<(string Label, WriteOptions? Options)>
            {
                ("default", null),
                ("reject unknown", new WriteOptions { UnknownMembers = UnknownMemberPolicy.Reject }),
                ("nesting 1", new WriteOptions { MaxNestingDepth = 1 }),
                ("arrays 1", new WriteOptions { MaxArrayElements = 1 }),
                ("invalid limit", new WriteOptions { MaxNestingDepth = 0 }),
            };
            for (long budget = size - 2; budget <= size; budget++)
            {
                options.Add(($"budget {budget}", new WriteOptions { MaxTotalBytesWritten = budget }));
            }

            foreach ((string valueLabel, object value) in values)
            {
                foreach ((string optionLabel, WriteOptions? writeOptions) in options)
                {
                    string label = $"aligned={aligned} {valueLabel} {optionLabel}";
                    AssertSameWrite(o => layout.Serialize("plain", value, options: o), writeOptions, label + " / array");
                    for (int capacity = size - 1; capacity <= size + 1; capacity++)
                    {
                        int length = capacity;
                        AssertSameWrite((destination, o) => layout.Serialize(destination, "plain", value, options: o), writeOptions, length, $"{label} / span of {length}");
                    }
                }
            }
        }
    }

    /// <summary>The typed conversion takes its shortcuts only where the general conversion would give the same value or failure.</summary>
    [TestMethod]
    public void TypedConversion_MatchesGeneralConversion()
    {
        var layout = new CStruct(Layout);
        StructValue plain = layout.Parse(SampleBytes(layout, "plain"), "plain");
        var big = new StructValue { ["a"] = (byte)1, ["b"] = 70_000u, ["c"] = new PrimitiveArray<ushort>([1, 2]) };
        object?[] inputs = [plain, big, plain["c"], plain["b"], null, "text"];
        foreach (object? input in inputs)
        {
            AssertSameConversion<PlainRecord>(input);
            AssertSameConversion<NarrowPlain>(input);
            AssertSameConversion<ThrowingPlain>(input);
            AssertSameConversion<ushort[]>(input);
            AssertSameConversion<int[]>(input);
            AssertSameConversion<ushort[,]>(input);
            AssertSameConversion<uint>(input);
            AssertSameConversion<long?>(input);
            AssertSameConversion<object>(input);
            AssertSameConversion<StructValue>(input);
        }
    }

    /// <summary>A path walk allocates no segment strings, and its failure messages still quote the normalized path consumed so far.</summary>
    [TestMethod]
    public void PathWalk_ReportsTheConsumedPath()
    {
        var layout = new CStruct(Layout);
        StructValue record = layout.Parse(SampleBytes(layout, "rec"), "rec");
        Assert.AreEqual(2u, record.Get<uint>("samples[1]"));
        Assert.AreEqual(2L, record.Get<long>("samples[01]"), "a converted element takes the general path");
        Assert.AreEqual(1.5f, record.Get<float>("pos.x"));
        Assert.AreEqual('R', record.Get<char>("tag[1]"));

        AssertPathFailure(record, "pos.z", "Path 'pos.z' cannot select 'z' in 'pos': the struct has no such member (members: x, y).");
        AssertPathFailure(record, "samples[01].q", "Path 'samples[01].q' cannot select 'q' in 'samples[1]': a UInt32 has no members.");
        AssertPathFailure(record, "samples[7]", "Path 'samples[7]' cannot index 'samples': index 7 is outside the 3 element(s).");
        AssertPathFailure(record, "pos..x", "Path 'pos..x' is malformed at position 3.");
        AssertPathFailure(record, "samples[x]", "Path 'samples[x]' has an invalid index at position 7; indices are non-negative integers in square brackets.");
        AssertPathFailure(record, "nothing", "Path 'nothing' cannot select 'nothing' in the value: the struct has no such member (members: id, which, tag, pos, samples, ok).");

        Assert.IsFalse(record.TryGet("samples[7]", out uint missing));
        Assert.AreEqual(0u, missing);
        Assert.IsFalse(record.TryGet("id", out byte _, out CStructException? narrowing), "258 does not fit a byte");
        Assert.IsInstanceOfType<CStructReadException>(narrowing);
        Assert.IsTrue(record.TryGet("samples[2]", out byte fits, out _));
        Assert.AreEqual((byte)3, fits);
    }

    /// <summary>Mapped property names resolve to the shape's own member name whatever string instance the caller passes.</summary>
    [TestMethod]
    public void MappedMemberNames_ResolveForAnyStringInstance()
    {
        StructValue value = new CStruct("struct s { uint8 bit_depth; uint8 Mode; uint8 mode; uint8 x; };").Parse(new byte[4], "s");
        string fresh = new string("X".ToCharArray());
        for (int round = 0; round < 2; round++)
        {
            Assert.AreEqual("x", MappedTypes.MemberName(value, "X"));
            Assert.AreEqual("x", MappedTypes.MemberName(value, fresh));
            Assert.AreEqual("bit_depth", MappedTypes.MemberName(value, "BitDepth"));
            Assert.AreEqual("MODE", MappedTypes.MemberName(value, "MODE"), "two case-insensitive matches are ambiguous");
            Assert.AreEqual("Nope", MappedTypes.MemberName(value, "Nope"));
        }

        Assert.AreSame(value.Keys.Single(key => key == "x"), MappedTypes.MemberName(value, fresh), "the shape's own instance is returned");
    }

    /// <summary>The generated fixed readers' cursor step takes a struct only when every limit the member-by-member reader checks holds.</summary>
    [TestMethod]
    public void ReadCursor_TryTakeFixed_HonoursEveryLimit()
    {
        byte[] bytes = new byte[12];
        Assert.IsTrue(Take(bytes, null, 8, 1, 8, 1, 0, out int position) && position == 8);
        Assert.IsTrue(Take(bytes, new ReadOptions { MaxTotalBytesRead = 6 }, 8, 1, 6, 1, 0, out _), "padding is not charged");
        Assert.IsFalse(Take(bytes, new ReadOptions { MaxTotalBytesRead = 5 }, 8, 1, 6, 1, 0, out position));
        Assert.AreEqual(0, position, "a refused take consumes nothing");
        Assert.IsFalse(Take(bytes, null, 13, 1, 13, 1, 0, out _), "short input");
        Assert.IsFalse(Take(bytes, new ReadOptions { MaxNestingDepth = 1 }, 8, 1, 8, 2, 0, out _), "nesting");
        Assert.IsTrue(Take(bytes, new ReadOptions { MaxNestingDepth = 2 }, 8, 1, 8, 2, 0, out _));
        Assert.IsFalse(Take(bytes, new ReadOptions { MaxArrayElements = 3 }, 8, 1, 8, 1, 4, out _), "array limit");
        Assert.IsFalse(Take(bytes, null, 8, 4, 8, 1, 0, out _, start: 2), "misaligned start");
        Assert.IsTrue(Take(bytes, null, 8, 4, 8, 1, 0, out position, start: 4) && position == 8);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.IsFalse(Take(bytes, new ReadOptions { CancellationToken = cancelled.Token }, 8, 1, 8, 1, 0, out _), "cancelled");
    }

    /// <summary>
    ///     The generated writers' failures carry the runtime writer's texts: a bitfield whose bits overrun its unit, and
    ///     the data-dependent array and union failures the cursor formats for generated code.
    /// </summary>
    [TestMethod]
    public void WriteCursor_Failures_UseTheRuntimeTexts()
    {
        CStructWriteException bits = Assert.Throws<CStructWriteException>(() =>
        {
            var overrun = new WriteCursor(new byte[4]);
            overrun.WriteBits(new BitfieldSlot(0, 1, 6), 4, (byte)1, true, false, "flags", "uint8");
        });
        StringAssert.StartsWith(bits.Message, "Bitfield exceeds its storage unit: flags");

        var cursor = new WriteCursor(new byte[4]);
        StringAssert.StartsWith(cursor.FailArrayTooMany("v", 2, "v", "uint8").Message, "Array value for v exceeds its permitted element count of 2");
        StringAssert.StartsWith(cursor.FailArrayLengthMismatch("v", 2, 3, "v", "uint8").Message, "Array length mismatch for v: expected 2, got 3");
        StringAssert.StartsWith(cursor.FailRawStorageLength("u", 2, 3, null, null).Message, "Raw storage length mismatch for u: expected 2, got 3");
        StringAssert.StartsWith(cursor.FailUnknownUnionMember("u", "x", null, null).Message, "Union 'u' has no member named 'x'");
    }

    /// <summary>The generated fixed writers' cursor step reserves a struct only where the member-by-member writer would write exactly it.</summary>
    [TestMethod]
    public void WriteCursor_TryReserveFixed_HonoursEveryLimit()
    {
        byte[] destination = Enumerable.Repeat((byte)0xCC, 12).ToArray();
        var cursor = new WriteCursor(destination);
        Assert.IsTrue(cursor.TryReserveFixed(8, 1, 1, 0, out Span<byte> bytes));
        Assert.IsTrue(bytes.ToArray().All(b => b == 0), "reserved bytes are cleared");
        Assert.AreEqual(8, cursor.Length);
        Assert.IsFalse(cursor.TryReserveFixed(8, 1, 1, 0, out _), "destination too small");
        cursor.Position = 2;
        Assert.IsFalse(cursor.TryReserveFixed(2, 1, 1, 0, out _), "writing over existing bytes preserves padding, so it is refused");

        var budget = new WriteCursor(new byte[12], new WriteOptions { MaxTotalBytesWritten = 7 });
        Assert.IsFalse(budget.TryReserveFixed(8, 1, 1, 0, out _), "byte budget");
        var nesting = new WriteCursor(new byte[12], new WriteOptions { MaxNestingDepth = 1 });
        Assert.IsFalse(nesting.TryReserveFixed(8, 1, 2, 0, out _), "nesting");
        var arrays = new WriteCursor(new byte[12], new WriteOptions { MaxArrayElements = 2 });
        Assert.IsFalse(arrays.TryReserveFixed(8, 1, 1, 3, out _), "array limit");
        var misaligned = new WriteCursor(new byte[12]);
        misaligned.Pad(2, null, null);
        Assert.IsFalse(misaligned.TryReserveFixed(4, 4, 1, 0, out _), "misaligned start");
        var growable = new WriteCursor(options: null, path: null);
        Assert.IsTrue(growable.TryReserveFixed(1000, 1, 1, 0, out Span<byte> grown) && grown.Length == 1000, "an owned buffer grows");
        Assert.AreEqual(1000, growable.ToArray().Length);
    }

    /// <summary>Encodes a sample value of <paramref name="root"/> with the general path.</summary>
    private static byte[] SampleBytes(CStruct layout, string root)
    {
        StructValue value = root == "plain"
            ? new StructValue { ["a"] = (byte)0x11, ["b"] = 0x22334455u, ["c"] = new ushort[] { 0x6677, 0x8899 } }
            : new StructValue
            {
                ["id"] = (ushort)0x0102,
                ["which"] = 2,
                ["tag"] = "IR",
                ["pos"] = new StructValue { ["x"] = 1.5f, ["y"] = -2f },
                ["samples"] = new uint[] { 1, 2, 3 },
                ["ok"] = true,
            };
        return layout.Serialize(root, value, options: new WriteOptions { ExecutionPath = ExecutionPath.NoFastPaths });
    }

    /// <summary>Runs <see cref="ReadCursor.TryTakeFixed"/> from <paramref name="start"/> and reports how far the cursor moved.</summary>
    private static bool Take(byte[] bytes, ReadOptions? options, int size, int alignment, long charged, int nesting, int arrays, out int position, int start = 0)
    {
        var cursor = new ReadCursor(bytes, options) { Position = start };
        bool taken = cursor.TryTakeFixed(size, alignment, charged, nesting, arrays, out ReadOnlySpan<byte> _);
        position = cursor.Position - start;
        return taken;
    }

    /// <summary>Asserts that reading <paramref name="path"/> fails with exactly <paramref name="message"/>, thrown and unthrown.</summary>
    private static void AssertPathFailure(StructValue value, string path, string message)
    {
        CStructPathException failure = Assert.ThrowsExactly<CStructPathException>(() => value.Get<int>(path));
        Assert.AreEqual(message, failure.Message, path);
        Assert.IsFalse(value.TryGet(path, out int _, out CStructException? unthrown), path);
        Assert.AreEqual(message, unthrown!.Message, path + ": TryGet");
    }

    /// <summary>Asserts that the typed conversion and the general conversion (as its callers cast it) agree for one input.</summary>
    private static void AssertSameConversion<T>(object? input)
    {
        OperationOutcome fast = OperationOutcome.Of(() => TypedValueConverter.Convert<T>(input, "root.value"), typeof(ArgumentException), typeof(OperationCanceledException), typeof(InvalidCastException));
        OperationOutcome general = OperationOutcome.Of(() => (T)TypedValueConverter.Convert(input, typeof(T), "root.value")!, typeof(ArgumentException), typeof(OperationCanceledException), typeof(InvalidCastException));
        OperationOutcome.AssertSame(general, fast, typeof(T).Name + " from " + (input?.GetType().Name ?? "null"));
    }

    /// <summary>Asserts that a read behaves the same with the caller's options and with the direct path excluded from them.</summary>
    private static void AssertSame(Func<ReadOptions?, object?> operation, ReadOptions? options, string label)
    {
        OperationOutcome fast = OperationOutcome.Of(() => operation(options), typeof(ArgumentException), typeof(OperationCanceledException), typeof(InvalidCastException));
        OperationOutcome general = OperationOutcome.Of(() => operation((options ?? new ReadOptions()) with { ExecutionPath = ExecutionPath.NoDirectAccess }), typeof(ArgumentException), typeof(OperationCanceledException), typeof(InvalidCastException));
        OperationOutcome.AssertSame(general, fast, label);
    }

    /// <summary>Asserts that a write to a new array behaves the same with the caller's options and with the direct path excluded from them.</summary>
    private static void AssertSameWrite(Func<WriteOptions?, object?> operation, WriteOptions? options, string label)
    {
        OperationOutcome fast = OperationOutcome.Of(() => operation(options), typeof(ArgumentException), typeof(OperationCanceledException), typeof(InvalidCastException));
        OperationOutcome general = OperationOutcome.Of(() => operation(WithoutDirectAccess(options)), typeof(ArgumentException), typeof(OperationCanceledException), typeof(InvalidCastException));
        OperationOutcome.AssertSame(general, fast, label);
    }

    /// <summary>Asserts that a span write behaves the same with the caller's options and with the direct path excluded, destination bytes included.</summary>
    private static void AssertSameWrite(Func<byte[], WriteOptions?, int> write, WriteOptions? options, int capacity, string label)
    {
        byte[] fastDestination = Enumerable.Repeat((byte)0xCC, capacity).ToArray();
        byte[] generalDestination = Enumerable.Repeat((byte)0xCC, capacity).ToArray();
        OperationOutcome fast = OperationOutcome.Of(() => write(fastDestination, options), typeof(ArgumentException), typeof(OperationCanceledException), typeof(InvalidCastException));
        OperationOutcome general = OperationOutcome.Of(() => write(generalDestination, WithoutDirectAccess(options)), typeof(ArgumentException), typeof(OperationCanceledException), typeof(InvalidCastException));
        OperationOutcome.AssertSame(general, fast, label);
        CollectionAssert.AreEqual(generalDestination, fastDestination, label + ": destination");
    }

    /// <summary>Returns the write options with direct span access excluded.</summary>
    private static WriteOptions WithoutDirectAccess(WriteOptions? options) => (options ?? new WriteOptions()) with { ExecutionPath = ExecutionPath.NoDirectAccess };

    /// <summary>A hand-written mapped class for <c>plain</c>.</summary>
    internal sealed class PlainRecord : ICStructMapped<PlainRecord>
    {
        /// <summary>Gets or sets <c>a</c>.</summary>
        public byte A { get; set; }

        /// <summary>Gets or sets <c>b</c>.</summary>
        public uint B { get; set; }

        /// <summary>Gets or sets <c>c</c>.</summary>
        public ushort[] C { get; set; } = [];

        /// <inheritdoc/>
        public static PlainRecord ReadFrom(StructValue source)
        {
            return new PlainRecord { A = source.Get<byte>(MappedTypes.MemberName(source, "A")), B = source.Get<uint>(MappedTypes.MemberName(source, "B")), C = source.Get<ushort[]>(MappedTypes.MemberName(source, "C")) };
        }

        /// <inheritdoc/>
        public static void WriteTo(PlainRecord value, StructValue target)
        {
            target[MappedTypes.MemberName(target, "A")] = value.A;
            target[MappedTypes.MemberName(target, "B")] = value.B;
            target[MappedTypes.MemberName(target, "C")] = value.C;
        }

        /// <summary>Registers the mapper before any test runs.</summary>
        [ModuleInitializer]
        internal static void Register() => MappedTypes.Register<PlainRecord>();
    }

    /// <summary>A mapper whose member conversion fails for a large <c>b</c>: the failure must carry the same path and offset.</summary>
    internal sealed class NarrowPlain : ICStructMapped<NarrowPlain>
    {
        /// <summary>Gets or sets <c>b</c>, which a large value cannot fit.</summary>
        public byte B { get; set; }

        /// <inheritdoc/>
        public static NarrowPlain ReadFrom(StructValue source) => new() { B = source.Get<byte>("b") };

        /// <inheritdoc/>
        public static void WriteTo(NarrowPlain value, StructValue target) => target["b"] = value.B;

        /// <summary>Registers the mapper before any test runs.</summary>
        [ModuleInitializer]
        internal static void Register() => MappedTypes.Register<NarrowPlain>();
    }

    /// <summary>A mapper that throws an ordinary exception, which the conversion wraps.</summary>
    internal sealed class ThrowingPlain : ICStructMapped<ThrowingPlain>
    {
        /// <summary>Always refuses, to exercise the conversion's wrapping of an ordinary exception.</summary>
        /// <param name="source">The parsed struct (unused).</param>
        /// <returns>Never returns.</returns>
        public static ThrowingPlain ReadFrom(StructValue source) => throw new InvalidOperationException("mapper refused");

        /// <summary>Always refuses.</summary>
        /// <param name="value">The instance (unused).</param>
        /// <param name="target">The target (unused).</param>
        public static void WriteTo(ThrowingPlain value, StructValue target) => throw new InvalidOperationException("mapper refused");

        /// <summary>Registers the mapper before any test runs.</summary>
        [ModuleInitializer]
        internal static void Register() => MappedTypes.Register<ThrowingPlain>();
    }
}
