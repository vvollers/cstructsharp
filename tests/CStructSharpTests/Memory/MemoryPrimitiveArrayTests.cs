namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Memory;
using CStructSharp.Values;

/// <summary>Preserves owned memory-array values and the observable work performed for each scalar element.</summary>
[TestClass]
public class MemoryPrimitiveArrayTests
{
    /// <summary>Numeric arrays retain their managed shape, scalar bit patterns, byte order, and empty-array behavior.</summary>
    /// <param name="spelling">Built-in scalar spelling.</param>
    /// <param name="size">Encoded bytes per element.</param>
    /// <param name="resultType">Expected public array value type.</param>
    [TestMethod]
    [DataRow("uint8", 1, typeof(PrimitiveArray<byte>))]
    [DataRow("int8", 1, typeof(PrimitiveArray<sbyte>))]
    [DataRow("bool", 1, typeof(PrimitiveArray<bool>))]
    [DataRow("int16", 2, typeof(PrimitiveArray<short>))]
    [DataRow("uint16", 2, typeof(PrimitiveArray<ushort>))]
    [DataRow("int24", 3, typeof(PrimitiveArray<int>))]
    [DataRow("uint24", 3, typeof(PrimitiveArray<uint>))]
    [DataRow("int32", 4, typeof(PrimitiveArray<int>))]
    [DataRow("uint32", 4, typeof(PrimitiveArray<uint>))]
    [DataRow("int48", 6, typeof(PrimitiveArray<long>))]
    [DataRow("uint48", 6, typeof(PrimitiveArray<ulong>))]
    [DataRow("int64", 8, typeof(PrimitiveArray<long>))]
    [DataRow("uint64", 8, typeof(PrimitiveArray<ulong>))]
    [DataRow("float32", 4, typeof(PrimitiveArray<float>))]
    [DataRow("float64", 8, typeof(PrimitiveArray<double>))]
    [DataRow("fixed16_16", 4, typeof(PrimitiveArray<double>))]
    [DataRow("ufixed16_16", 4, typeof(PrimitiveArray<double>))]
    [DataRow("fixed2_30", 4, typeof(PrimitiveArray<double>))]
    [DataRow("ufixed8_8", 2, typeof(PrimitiveArray<double>))]
    [DataRow("float16", 2, typeof(List<object>))]
    [DataRow("int128", 16, typeof(List<object>))]
    [DataRow("uint128", 16, typeof(List<object>))]
    public void NumericArrays_MatchIndependentScalarReads(string spelling, int size, Type resultType)
    {
        foreach (bool littleEndian in new[] { false, true, })
        {
            foreach (string suffix in size == 1 ? new[] { string.Empty } : new[] { string.Empty, ">", "<", })
            {
                string codec = spelling + suffix;
                var scalar = new CStruct($"struct Scalar {{ {codec} value; }};", isLittleEndian: littleEndian);
                foreach (int count in new[] { 0, 3, })
                {
                    var bytes = new byte[count * size];
                    if (count > 0)
                    {
                        bytes.AsSpan(size, size).Fill(0xff);
                        for (int index = 0; index < size; index++)
                        {
                            bytes[(2 * size) + index] = (byte)(0x81 + (index * 7));
                        }
                    }

                    MemorySession session = ArraySession(codec, size, count, littleEndian);
                    var region = new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, bytes.Length);
                    var context = new MemoryAccessContext();
                    object? value = session.Read(region, "array", context: context);
                    Assert.AreEqual(resultType, value!.GetType(), codec);
                    var actual = (IList<object?>)value;
                    Assert.AreEqual(count, actual.Count);
                    Assert.AreEqual(1 + (2 * count), context.Requests);
                    Assert.AreEqual((long)bytes.Length, context.BytesRequested);
                    for (int index = 0; index < count; index++)
                    {
                        AssertScalar(scalar.ReadValue(bytes.AsSpan(index * size, size), codec)!, actual[index]!);
                    }
                }
            }
        }
    }

    /// <summary>An element's declared order overrides the schema, and explicit codec order overrides both.</summary>
    [TestMethod]
    public void NumericArrays_PreserveElementByteOrderOverride()
    {
        foreach (string spelling in new[] { "uint16", "uint16>", "uint16<", })
        {
            foreach (bool elementLittle in new[] { false, true, })
            {
                MemorySession session = ArraySession(spelling, 2, 2, !elementLittle, elementLittleEndian: elementLittle);
                var region = new MemoryRegion(new ByteArrayMemorySource("image", [1, 2, 3, 4,]), 0, 4);
                var actual = (PrimitiveArray<ushort>)session.Read(region, "array")!;
                bool little = spelling.EndsWith('<') || (!spelling.EndsWith('>') && elementLittle);
                CollectionAssert.AreEqual(little ? new ushort[] { 0x0201, 0x0403, } : new ushort[] { 0x0102, 0x0304, }, actual.ToArray());
            }
        }
    }

    /// <summary>Large arrays and later reads own separate mutable results that do not alias source bytes or copies.</summary>
    [TestMethod]
    public void NumericArrays_OwnLargeResultsAcrossReads()
    {
        const int Count = 4097;
        var bytes = new byte[Count];
        for (int index = 0; index < bytes.Length; index++)
        {
            bytes[index] = (byte)(index % 251);
        }

        MemorySession session = ArraySession("uint8", 1, Count);
        var source = new ByteArrayMemorySource("image", bytes);
        var region = new MemoryRegion(source, 0, bytes.Length);
        var context = new MemoryAccessContext();
        var first = (PrimitiveArray<byte>)session.Read(region, "array", context: context)!;
        CollectionAssert.AreEqual(bytes, first.ToArray());
        Assert.AreEqual(1 + (2 * Count), context.Requests);
        Assert.AreEqual((long)Count, context.BytesRequested);
        first[0] = (byte)99;
        byte[] copy = first.ToArray();
        copy[1] = 100;
        source.Write(2, [101,], new MemoryAccessContext());
        var second = (PrimitiveArray<byte>)session.Read(region, "array")!;
        Assert.AreNotSame(first, second);
        Assert.AreEqual((byte)99, first[0]);
        Assert.AreEqual((byte)0, second[0]);
        Assert.AreEqual((byte)1, first[1]);
        Assert.AreEqual((byte)1, second[1]);
        Assert.AreEqual((byte)2, first[2]);
        Assert.AreEqual((byte)101, second[2]);
    }

    /// <summary>Enums, explicitly declared scalars, and characters keep their list shape instead of becoming numeric arrays.</summary>
    [TestMethod]
    public void OtherScalars_PreserveListFallbacks()
    {
        foreach ((string spelling, int size, string? declaration) in new (string, int, string?)[]
        {
            ("kind", 1, "enum kind : uint8 { first = 1, second = 2 };"),
            ("uint8", 1, string.Empty),
            ("char", 1, null),
            ("wchar", 2, null),
        })
        {
            foreach (int count in new[] { 0, 2, })
            {
                var bytes = new byte[count * size];
                if (count != 0)
                {
                    bytes[0] = 1;
                    bytes[size] = 2;
                }

                MemorySession session = ArraySession(spelling, size, count, declaration: declaration);
                var region = new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, bytes.Length);
                var values = (List<object?>)session.Read(region, "array")!;
                Assert.AreEqual(count, values.Count);
                if (count != 0 && spelling == "kind")
                {
                    Assert.AreEqual("first", ((EnumValueResult)values[0]!).Name);
                    Assert.AreEqual("second", ((EnumValueResult)values[1]!).Name);
                }
                else if (count != 0)
                {
                    Assert.AreEqual(spelling == "uint8" ? (object)(byte)1 : (char)1, values[0]);
                    Assert.AreEqual(spelling == "uint8" ? (object)(byte)2 : (char)2, values[1]);
                }
            }
        }
    }

    /// <summary>Custom codecs retain numeric or list results and still decode zero bytes when choosing an empty array's shape.</summary>
    [TestMethod]
    public void CustomScalars_PreserveDecodeCallsIncludingEmptyArrays()
    {
        foreach (int count in new[] { 0, 2, })
        {
            foreach (bool text in new[] { false, true, })
            {
                var codec = new CountingByteCodec { Text = text, };
                MemorySession session = ArraySession("counted", 1, count, options: new CStructCompilationOptions { Codecs = [codec,], });
                var region = new MemoryRegion(new ByteArrayMemorySource("image", count == 0 ? [] : new byte[] { 10, 20, }), 0, count);
                var context = new MemoryAccessContext();
                object? value = session.Read(region, "array", context: context);
                if (text)
                {
                    Assert.IsInstanceOfType<List<object?>>(value);
                    CollectionAssert.AreEqual(count == 0 ? Array.Empty<object>() : new object[] { "11", "22", }, (List<object?>)value!);
                }
                else
                {
                    Assert.IsInstanceOfType<PrimitiveArray<byte>>(value);
                    CollectionAssert.AreEqual(count == 0 ? Array.Empty<byte>() : new byte[] { 11, 22, }, ((PrimitiveArray<byte>)value!).ToArray());
                }

                CollectionAssert.AreEqual(count == 0 ? new byte[] { 0, } : new byte[] { 10, 20, }, codec.Inputs.ToArray());
                Assert.AreEqual(1 + (2 * count), context.Requests);
                Assert.AreEqual((long)count, context.BytesRequested);
            }
        }
    }

    /// <summary>Prelude and defined-symbol aliases retain their compiled width and decoded primitive result.</summary>
    [TestMethod]
    public void ConfiguredScalars_PreservePreludeMeaning()
    {
        var options = new CStructCompilationOptions
        {
            Prelude = "#ifdef WIDE\ntypedef uint16 selected;\n#else\ntypedef uint8 selected;\n#endif\n",
            Defined = new HashSet<string> { "WIDE", },
        };
        MemorySession session = ArraySession("selected", 2, 2, options: options);
        var region = new MemoryRegion(new ByteArrayMemorySource("image", [1, 2, 3, 4,]), 0, 4);
        CollectionAssert.AreEqual(new ushort[] { 0x0201, 0x0403, }, ((PrimitiveArray<ushort>)session.Read(region, "array")!).ToArray());
    }

    /// <summary>Partial reads retain request extents, property order, byte accounting, and fresh zeroed element scratch.</summary>
    [TestMethod]
    public void NumericArrays_PreservePartialReadTraceAndScratch()
    {
        MemorySession session = ArraySession("uint16", 2, 2);
        var source = new ObservedSource([1, 2, 3, 4,]) { MaxChunk = 1, };
        var context = new MemoryAccessContext();
        var value = (PrimitiveArray<ushort>)session.Read(new MemoryRegion(source, 100, 4), "array", context: context)!;
        CollectionAssert.AreEqual(new ushort[] { 0x0201, 0x0403, }, value.ToArray());
        CollectionAssert.AreEqual(PartialTrace(), source.Trace);
        Assert.AreEqual(7, context.Requests);
        Assert.AreEqual(6L, context.BytesRequested);
    }

    /// <summary>A zero read and invalid read counts fail at the same element with the same completed property trace.</summary>
    /// <param name="returned">Invalid or unavailable count returned by the second element's source request.</param>
    [TestMethod]
    [DataRow(0)]
    [DataRow(-1)]
    [DataRow(3)]
    public void NumericArrays_PreserveSourceFailureBoundaries(int returned)
    {
        MemorySession session = ArraySession("uint16", 2, 2);
        var source = new ObservedSource([1, 2, 3, 4,]) { StopAt = 2, ReturnedAtStop = returned, };
        var region = new MemoryRegion(source, 100, 4);
        var context = new MemoryAccessContext();

        // The first element completes before the second request reports its unavailable or invalid count.
        MemoryAccessException failure = Assert.Throws<MemoryAccessException>(() => session.Read(region, "array", context: context));
        string detail = returned == 0 ? "Backing bytes are unavailable" : "Backing source returned an invalid read count";
        Assert.AreEqual($"image:0x66 (2 bytes): {detail} (path 'array').", failure.Message);
        Assert.AreEqual(returned == 0 ? MemoryFailure.MissingBytes : MemoryFailure.SourceFailure, failure.Failure);
        Assert.AreEqual(102UL, failure.Address);
        Assert.AreEqual(2, failure.Length);
        Assert.AreSame(region, failure.LogicalRegion);
        CollectionAssert.AreEqual(CompleteTrace()[..(returned == 0 ? 9 : 8)], source.Trace);
        Assert.AreEqual(5, context.Requests);
        Assert.AreEqual(4L, context.BytesRequested);
    }

    /// <summary>A source exception remains the same object and prevents the post-read source-property observations.</summary>
    [TestMethod]
    public void NumericArrays_PreserveThrownSourceFailure()
    {
        MemorySession session = ArraySession("uint16", 2, 2);
        var expected = new MemoryAccessException(MemoryFailure.Unmapped, "backing", 900, 2, "fixture hole");
        var source = new ObservedSource([1, 2, 3, 4,]) { StopAt = 2, Failure = expected, };
        var region = new MemoryRegion(source, 100, 4);
        var context = new MemoryAccessContext();

        // An exception from the leaf source escapes without a replacement or further source access.
        MemoryAccessException actual = Assert.Throws<MemoryAccessException>(() => session.Read(region, "array", context: context));
        Assert.AreSame(expected, actual);
        Assert.AreEqual("backing:0x384 (2 bytes): fixture hole (path 'array').", actual.Message);
        Assert.AreSame(region, actual.LogicalRegion);
        CollectionAssert.AreEqual(CompleteTrace()[..7], source.Trace);
        Assert.AreEqual(5, context.Requests);
        Assert.AreEqual(4L, context.BytesRequested);
    }

    /// <summary>Array admission and later request or byte exhaustion preserve exact work prefixes and failure coordinates.</summary>
    [TestMethod]
    public void NumericArrays_PreserveBudgetBoundaries()
    {
        (int Requests, int Bytes, int ChargedRequests, long ChargedBytes, int TraceLength, ulong Address, int Length)[] cases =
        [
            (2, 100, 1, 0, 2, 100, 4),
            (3, 100, 3, 2, 6, 102, 0),
            (4, 100, 4, 2, 7, 102, 2),
            (100, 1, 2, 0, 3, 100, 2),
            (100, 3, 4, 2, 7, 102, 2),
        ];
        foreach ((int requests, int bytes, int chargedRequests, long chargedBytes, int traceLength, ulong address, int length) in cases)
        {
            MemorySession session = ArraySession("uint16", 2, 2);
            var source = new ObservedSource([1, 2, 3, 4,]);
            var region = new MemoryRegion(source, 100, 4);
            var context = new MemoryAccessContext { MaxRequests = requests, MaxTotalBytes = bytes, };

            // Each limit targets a distinct admission, element, or source-read boundary.
            MemoryAccessException failure = Assert.Throws<MemoryAccessException>(() => session.Read(region, "array", context: context));
            string detail = requests == 2 ? "Array exceeds remaining work budget" : "Memory operation budget exceeded";
            Assert.AreEqual($"image:0x{address:x} ({length} bytes): {detail} (path 'array').", failure.Message);
            Assert.AreEqual(MemoryFailure.BudgetExceeded, failure.Failure);
            Assert.AreEqual(address, failure.Address);
            Assert.AreEqual(length, failure.Length);
            Assert.AreSame(region, failure.LogicalRegion);
            Assert.AreEqual(chargedRequests, context.Requests);
            Assert.AreEqual(chargedBytes, context.BytesRequested);
            CollectionAssert.AreEqual(CompleteTrace()[..traceLength], source.Trace);
        }
    }

    /// <summary>An array nested in a record checks each element's depth before observing its source.</summary>
    [TestMethod]
    public void NumericArrays_PreserveElementDepthChecks()
    {
        MemorySession session = ArraySession("uint16", 2, 2);
        var source = new ObservedSource([1, 2, 3, 4,]);
        var region = new MemoryRegion(source, 100, 4);
        var context = new MemoryAccessContext { MaxNestingDepth = 1, };

        // The record and array charges succeed; the first scalar's depth fails before its charge or read.
        MemoryAccessException failure = Assert.Throws<MemoryAccessException>(() => session.Read(region, "record", context: context));
        Assert.AreEqual("Memory nesting depth exceeded (path 'record').", failure.Message);
        Assert.AreEqual(MemoryFailure.BudgetExceeded, failure.Failure);
        Assert.IsNull(failure.SourceId);
        Assert.IsNull(failure.Address);
        Assert.AreSame(region, failure.LogicalRegion);
        Assert.AreEqual(2, context.Requests);
        Assert.AreEqual(0L, context.BytesRequested);
        CollectionAssert.AreEqual(new[] { "id", "id", }, source.Trace);
    }

    /// <summary>Cancellation before a read or inside the first source call retains the completed request prefix.</summary>
    [TestMethod]
    public void NumericArrays_PreserveCancellationBoundaries()
    {
        foreach (bool cancelledBefore in new[] { false, true, })
        {
            foreach (int chunk in new[] { 1, 2, })
            {
                using var cancellation = new CancellationTokenSource();
                MemorySession session = ArraySession("uint16", 2, 2);
                var source = new ObservedSource([1, 2, 3, 4,]) { MaxChunk = chunk, };

                // Cancellation during the first request is observed before the next partial read or next element.
                source.BeforeRead = _ => cancellation.Cancel();
                if (cancelledBefore)
                {
                    cancellation.Cancel();
                }

                var context = new MemoryAccessContext { CancellationToken = cancellation.Token, };
                var region = new MemoryRegion(source, 100, 4);

                // The token's own cancellation exception must escape without becoming a memory failure.
                OperationCanceledException failure = Assert.Throws<OperationCanceledException>(() => session.Read(region, "array", context: context));
                Assert.AreEqual(cancellation.Token, failure.CancellationToken);
                Assert.AreEqual(cancelledBefore ? 0 : 3, context.Requests);
                Assert.AreEqual(cancelledBefore ? 0L : 2L, context.BytesRequested);
                CollectionAssert.AreEqual(CompleteTrace()[..(cancelledBefore ? 0 : 5)], source.Trace);
            }
        }
    }

    /// <summary>Reentrant reads preserve an outer partial element, and later source mutations affect only unread elements.</summary>
    [TestMethod]
    public void NumericArrays_PreserveReentrancyAndMutationOrder()
    {
        MemorySession session = ArraySession("uint16", 2, 2);
        var source = new ObservedSource([1, 2, 3, 4,]) { MaxChunk = 1, };
        PrimitiveArray<ushort>? nested = null;

        // Reenter after the outer element's first byte, then mutate the source before its second element starts.
        source.BeforeRead = request =>
        {
            if (request == 2)
            {
                var region = new MemoryRegion(new ByteArrayMemorySource("nested", [9, 10, 11, 12,]), 0, 4);
                nested = (PrimitiveArray<ushort>)session.Read(region, "array")!;
            }
            else if (request == 3)
            {
                source.Bytes[0] = 99;
                source.Bytes[2] = 10;
                source.Bytes[3] = 20;
            }
        };
        var context = new MemoryAccessContext();
        var outer = (PrimitiveArray<ushort>)session.Read(new MemoryRegion(source, 100, 4), "array", context: context)!;
        CollectionAssert.AreEqual(new ushort[] { 0x0201, 0x140a, }, outer.ToArray());
        CollectionAssert.AreEqual(new ushort[] { 0x0a09, 0x0c0b, }, nested!.ToArray());
        CollectionAssert.AreEqual(PartialTrace(), source.Trace);
        Assert.AreEqual(7, context.Requests);
        Assert.AreEqual(6L, context.BytesRequested);
    }

    /// <summary>Array element addresses remain unsigned through the final byte of the address space.</summary>
    [TestMethod]
    public void NumericArrays_PreserveMaximumAddressAndEmptyExtent()
    {
        var source = new ObservedSource([7, 9,], ulong.MaxValue - 1);
        MemorySession session = ArraySession("uint8", 1, 2);
        var value = (PrimitiveArray<byte>)session.Read(new MemoryRegion(source, ulong.MaxValue - 1, 2), "array")!;
        CollectionAssert.AreEqual(new byte[] { 7, 9, }, value.ToArray());
        CollectionAssert.AreEqual(new[] { "id", "id", $"read:{ulong.MaxValue - 1}:1:00", "id", "id", "id", $"read:{ulong.MaxValue}:1:00", "id", "id", }, source.Trace);
        source.Trace.Clear();
        var context = new MemoryAccessContext { MaxRequests = 1, MaxTotalBytes = 1, };
        var empty = (PrimitiveArray<byte>)ArraySession("uint8", 1, 0).Read(new MemoryRegion(source, ulong.MaxValue, 0), "array", context: context)!;
        Assert.AreEqual(0, empty.Count);
        Assert.AreEqual(1, context.Requests);
        Assert.AreEqual(0L, context.BytesRequested);
        CollectionAssert.AreEqual(new[] { "id", }, source.Trace);
    }

    /// <summary>Creates an array and a containing record over one explicit scalar definition.</summary>
    /// <param name="spelling">Scalar codec name.</param>
    /// <param name="size">Scalar width in bytes.</param>
    /// <param name="count">Array element count.</param>
    /// <param name="littleEndian">Schema byte order.</param>
    /// <param name="declaration">Optional declarations belonging to the scalar.</param>
    /// <param name="options">Optional caller codec or preprocessor configuration.</param>
    /// <param name="elementLittleEndian">Optional scalar byte-order override.</param>
    /// <returns>A session whose root types are named array and record.</returns>
    private static MemorySession ArraySession(string spelling, int size, int count, bool littleEndian = true, string? declaration = null, CStructCompilationOptions? options = null, bool? elementLittleEndian = null)
        => new(new MemorySchema(
        [
            new("scalar", "scalar", MemoryTypeKind.Scalar, size, scalarType: spelling, declaration: declaration, isLittleEndian: elementLittleEndian),
            new("array", "array", MemoryTypeKind.Array, size * count, elementTypeId: "scalar", count: count),
            new("record", "record", MemoryTypeKind.Struct, size * count, [new("values", "array", 0),]),
        ],
        isLittleEndian: littleEndian,
        options: options));

    /// <summary>Compares scalar values exactly, including floating-point NaN payloads and signed zero.</summary>
    /// <param name="expected">Value decoded by an independent scalar layout.</param>
    /// <param name="actual">Array element produced by the memory session.</param>
    private static void AssertScalar(object expected, object actual)
    {
        Assert.AreEqual(expected.GetType(), actual.GetType());
        if (expected is float single)
        {
            Assert.AreEqual(BitConverter.SingleToInt32Bits(single), BitConverter.SingleToInt32Bits((float)actual));
        }
        else if (expected is double number)
        {
            Assert.AreEqual(BitConverter.DoubleToInt64Bits(number), BitConverter.DoubleToInt64Bits((double)actual));
        }
        else if (expected is Half half)
        {
            Assert.AreEqual(BitConverter.HalfToInt16Bits(half), BitConverter.HalfToInt16Bits((Half)actual));
        }
        else
        {
            Assert.AreEqual(expected, actual);
        }
    }

    /// <summary>Returns the exact source trace for two complete two-byte element reads.</summary>
    /// <returns>Property accesses and read requests, including their incoming scratch bytes.</returns>
    private static string[] CompleteTrace()
        => ["id", "id", "read:100:2:0000", "id", "id", "id", "read:102:2:0000", "id", "id",];

    /// <summary>Returns the exact source trace when each two-byte element takes two one-byte reads.</summary>
    /// <returns>Property accesses and partial requests, including their incoming scratch bytes.</returns>
    private static string[] PartialTrace()
        => ["id", "id", "read:100:2:0000", "id", "id", "read:101:1:00", "id", "id", "id", "read:102:2:0000", "id", "id", "read:103:1:00", "id", "id",];

    /// <summary>A mutable positional source whose bytes depend on the incoming scratch contents and whose calls are traced.</summary>
    private sealed class ObservedSource : IMemorySource
    {
        private readonly ulong address;
        private int reads;

        /// <summary>Creates a source over fixture-owned mutable bytes.</summary>
        /// <param name="bytes">Bytes available to reads and mutation callbacks.</param>
        /// <param name="address">Unsigned address of the first byte.</param>
        internal ObservedSource(byte[] bytes, ulong address = 100)
        {
            this.Bytes = bytes;
            this.address = address;
        }

        /// <summary>Gets the backing bytes, which callbacks may mutate between requests.</summary>
        internal byte[] Bytes { get; }

        /// <summary>Gets the ordered property and read trace.</summary>
        internal List<string> Trace { get; } = new();

        /// <summary>Gets the maximum positive count returned from a successful request.</summary>
        internal int MaxChunk { get; init; } = int.MaxValue;

        /// <summary>Gets the one-based request on which the configured failure occurs; zero disables it.</summary>
        internal int StopAt { get; init; }

        /// <summary>Gets the count returned on the configured failure request.</summary>
        internal int ReturnedAtStop { get; init; }

        /// <summary>Gets the original exception thrown on the configured failure request, if any.</summary>
        internal MemoryAccessException? Failure { get; init; }

        /// <summary>Gets or sets an action performed after charging and before copying a request's bytes.</summary>
        internal Action<int>? BeforeRead { get; set; }

        /// <summary>Gets the source label and records the property observation.</summary>
        public string Id
        {
            get
            {
                this.Trace.Add("id");
                return "image";
            }
        }

        /// <summary>Gets the fixture generation and records any unexpected generation observation.</summary>
        public long Generation
        {
            get
            {
                this.Trace.Add("generation");
                return 0;
            }
        }

        /// <summary>Charges and traces a read, then combines available bytes with the destination's initial contents.</summary>
        /// <param name="address">Unsigned address of the requested bytes.</param>
        /// <param name="destination">Caller-owned scratch, expected to be fresh zeroes for each new element.</param>
        /// <param name="context">Shared budget charged for the requested extent before any callback or copy.</param>
        /// <returns>The copied count, or the configured count on a failing request.</returns>
        /// <exception cref="MemoryAccessException">The budget is exhausted or the configured source failure occurs.</exception>
        /// <exception cref="OperationCanceledException">The token was cancelled before the charge.</exception>
        public int Read(ulong address, Span<byte> destination, MemoryAccessContext context)
        {
            this.Trace.Add($"read:{address}:{destination.Length}:{Convert.ToHexString(destination)}");
            context.Charge("image", address, destination.Length);
            int request = ++this.reads;
            this.BeforeRead?.Invoke(request);
            if (request == this.StopAt)
            {
                if (this.Failure is { } failure)
                {
                    throw failure;
                }

                return this.ReturnedAtStop;
            }

            int count = Math.Min(destination.Length, this.MaxChunk);
            int offset = checked((int)(address - this.address));
            for (int index = 0; index < count; index++)
            {
                // A source can observe scratch; stale bytes would change the resulting decoded value here.
                destination[index] ^= this.Bytes[offset + index];
            }

            return count;
        }
    }

    /// <summary>A stateful fixed-size custom codec that exposes every decode through its result and recorded input.</summary>
    private sealed class CountingByteCodec : ICustomCodec
    {
        /// <summary>Gets whether decoded values are strings instead of numeric bytes.</summary>
        internal bool Text { get; init; }

        /// <summary>Gets the custom scalar name.</summary>
        public string Name => "counted";

        /// <summary>Gets the one-byte encoded size.</summary>
        public int? FixedSize => 1;

        /// <summary>Gets the one-byte alignment.</summary>
        public int Alignment => 1;

        /// <summary>Gets the byte observed by each decode call.</summary>
        internal List<byte> Inputs { get; } = new();

        /// <summary>Returns the input byte plus this call's one-based ordinal, optionally as decimal text.</summary>
        /// <param name="source">One complete scalar's bytes.</param>
        /// <param name="value">Receives the ordinal-adjusted byte or its decimal string.</param>
        /// <param name="bytesConsumed">Receives one.</param>
        /// <returns>Done after decoding the byte.</returns>
        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
        {
            this.Inputs.Add(source[0]);
            byte number = (byte)(source[0] + this.Inputs.Count);
            value = this.Text ? number.ToString(System.Globalization.CultureInfo.InvariantCulture) : number;
            bytesConsumed = 1;
            return OperationStatus.Done;
        }

        /// <summary>Writes a byte without changing the read-call observations.</summary>
        /// <param name="destination">At least one byte of writable storage.</param>
        /// <param name="value">Byte to encode.</param>
        /// <param name="bytesWritten">Receives one.</param>
        /// <returns>Done after encoding the byte.</returns>
        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            destination[0] = (byte)value;
            bytesWritten = 1;
            return OperationStatus.Done;
        }
    }
}
