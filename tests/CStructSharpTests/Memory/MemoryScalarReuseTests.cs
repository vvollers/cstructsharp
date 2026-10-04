namespace CStructSharp.Tests;

using System.Buffers;
using System.Collections.Concurrent;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;
using CStructSharp.Memory;
using CStructSharp.Values;

/// <summary>Protects scalar compilation behavior when several memory type IDs name the same codec.</summary>
[TestClass]
public class MemoryScalarReuseTests
{
    /// <summary>Repeated roots keep their declared signedness, width, byte order, and owned serialization bytes.</summary>
    [TestMethod]
    public void RepeatedScalars_KeepValueShapeAndByteOrder()
    {
        (string Root, int Size, bool? Little, object Value, byte[] Bytes)[] cases =
        [
            ("uint16", 2, null, (ushort)0xFF80, [0x80, 0xFF]),
            ("uint16", 2, false, (ushort)0x80FF, [0x80, 0xFF]),
            ("int16", 2, null, (short)-128, [0x80, 0xFF]),
            ("uint16>", 2, true, (ushort)0x80FF, [0x80, 0xFF]),
            ("int16<", 2, false, (short)-128, [0x80, 0xFF]),
            ("uint32", 4, null, 0x87654321U, [0x21, 0x43, 0x65, 0x87]),
            ("uint64", 8, null, 0xFEDCBA9876543210UL, [0x10, 0x32, 0x54, 0x76, 0x98, 0xBA, 0xDC, 0xFE]),
        ];
        var definitions = new List<MemoryTypeDefinition>();
        for (int index = 0; index < cases.Length; index++)
        {
            for (int alias = 0; alias < 2; alias++)
            {
                definitions.Add(new($"t{index}_{alias}", "alias", MemoryTypeKind.Scalar, cases[index].Size, scalarType: cases[index].Root, isLittleEndian: cases[index].Little));
            }
        }

        var session = new MemorySession(new MemorySchema(definitions));
        for (int index = 0; index < cases.Length; index++)
        {
            for (int alias = 0; alias < 2; alias++)
            {
                string id = $"t{index}_{alias}";
                var source = new ByteArrayMemorySource("image", cases[index].Bytes);
                var region = new MemoryRegion(source, 0, cases[index].Size);
                Assert.AreEqual(cases[index].Value, session.Read(region, id), id);
                byte[] serialized = session.Serialize(id, cases[index].Value);
                CollectionAssert.AreEqual(cases[index].Bytes, serialized, id);
                serialized[0] ^= 0xFF;
                CollectionAssert.AreEqual(cases[index].Bytes, session.Serialize(id, cases[index].Value), id);
                MemoryPatch patch = session.PlanUpdate(region, id, string.Empty, cases[index].Value);
                CollectionAssert.AreEqual(cases[index].Bytes, patch.Fragments[0].Replacement, id);
                CollectionAssert.AreEqual(cases[index].Bytes, source.ToArray(), id);
            }
        }
    }

    /// <summary>Pointer roots may match scalar roots while keeping pointer result shape and each target's identity.</summary>
    [TestMethod]
    public void RepeatedPointers_KeepTargetsAndStoredPointerShape()
    {
        MemoryTypeDefinition[] definitions =
        [
            new("number", "number", MemoryTypeKind.Scalar, 4, scalarType: "uint32"),
            new("alias", "alias", MemoryTypeKind.Scalar, 4, scalarType: "uint32"),
            new("record", "record", MemoryTypeKind.Struct, 4, [new("value", "alias", 0)]),
            new("numberPointer", "number *", MemoryTypeKind.Pointer, 4, elementTypeId: "number"),
            new("recordPointer", "record *", MemoryTypeKind.Pointer, 4, elementTypeId: "record"),
            new("widePointer", "opaque *", MemoryTypeKind.Pointer, 8),
        ];
        var session = new MemorySession(new MemorySchema(definitions));
        var source = new ByteArrayMemorySource("image", [8, 0, 0, 0, 0, 0, 0, 0, 42, 0, 0, 0]);
        var region = new MemoryRegion(source, 0, 12);
        Assert.AreEqual(8U, session.Read(region, "number"));
        Assert.AreEqual(new StoredPointer(8, 4), session.Read(region, "numberPointer"));
        Assert.AreEqual(new StoredPointer(8, 4), session.Read(region, "recordPointer"));
        Assert.AreEqual(new StoredPointer(8, 8), session.Read(region, "widePointer"));
        Assert.AreEqual(42U, session.Read(region, "numberPointer", "value"));
        Assert.AreEqual(42U, session.Read(region, "recordPointer", "value.value"));
        CollectionAssert.AreEqual(new byte[] { 8, 0, 0, 0 }, session.Serialize("recordPointer", new StoredPointer(8, 4)));
        CollectionAssert.AreEqual(new byte[] { 12, 0, 0, 0 }, session.PlanUpdate(region, "numberPointer", string.Empty, new StoredPointer(12, 4)).Fragments[0].Replacement);

        // Pointer writes must retain their explicit width even when an integer uses the same scalar codec.
        Assert.Throws<CStructWriteException>(() => session.Serialize("recordPointer", 8U));
    }

    /// <summary>Every repeated scalar validates its own size, with exact strict errors and sorted best-effort notes.</summary>
    [TestMethod]
    public void RepeatedRoots_ValidateEachSizeInEitherOrder()
    {
        var valid = new MemoryTypeDefinition("valid", "word", MemoryTypeKind.Scalar, 2, scalarType: "uint16");
        var invalid = new MemoryTypeDefinition("invalid", "word", MemoryTypeKind.Scalar, 4, scalarType: "uint16");
        foreach (MemoryTypeDefinition[] order in new[] { new[] { valid, invalid }, new[] { invalid, valid } })
        {
            // Reordering the valid alias must not hide or rename the invalid definition's error.
            CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new MemorySchema(order));
            Assert.AreEqual("Scalar size disagrees with codec for 'invalid'.", failure.Message);
            var schema = new MemorySchema(order, bestEffort: true);
            CollectionAssert.AreEqual(new[] { "invalid: demoted to a 4-byte raw-bytes placeholder - Scalar size disagrees with codec for 'invalid'." }, schema.Diagnostics.ToArray());
            Assert.AreEqual(MemoryTypeKind.RawBytes, schema.GetType("invalid").Kind);
            Assert.AreEqual(MemoryTypeKind.Scalar, schema.GetType("valid").Kind);
            var session = new MemorySession(schema);
            var region = new MemoryRegion(new ByteArrayMemorySource("image", [1, 2, 3, 4]), 0, 4);
            Assert.AreEqual((ushort)0x0201, session.Read(region, "valid"));
            CollectionAssert.AreEqual(new byte[] { 1, 2, 3, 4 }, (byte[])session.Read(region, "invalid")!);
        }
    }

    /// <summary>Sharing a pointer's integer spelling never bypasses validation of a later pointer's target ID.</summary>
    [TestMethod]
    public void RepeatedPointerRoots_ValidateEveryTarget()
    {
        MemoryTypeDefinition[] definitions =
        [
            new("word", "word", MemoryTypeKind.Scalar, 4, scalarType: "uint32"),
            new("valid", "word *", MemoryTypeKind.Pointer, 4, elementTypeId: "word"),
            new("invalid", "absent *", MemoryTypeKind.Pointer, 4, elementTypeId: "absent"),
        ];

        // The invalid pointer has the same integer codec as the preceding valid definitions.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new MemorySchema(definitions));
        Assert.AreEqual("'invalid' references unknown memory type 'absent'.", failure.Message);
        var schema = new MemorySchema(definitions, bestEffort: true);
        CollectionAssert.AreEqual(new[] { "invalid: demoted to a 4-byte raw-bytes placeholder - 'invalid' references unknown memory type 'absent'." }, schema.Diagnostics.ToArray());
        Assert.AreEqual(MemoryTypeKind.Pointer, schema.GetType("valid").Kind);
    }

    /// <summary>Each schema retains its own pointer width, C long width, and default byte order.</summary>
    [TestMethod]
    public void SeparateSchemas_KeepCompilationSettings()
    {
        foreach (int width in new[] { 4, 8 })
        {
            foreach (bool little in new[] { true, false })
            {
                MemoryTypeDefinition[] definitions =
                [
                    new("pointerInteger", "pointerInteger", MemoryTypeKind.Scalar, width, scalarType: "uintptr_t"),
                    new("pointerAlias", "pointerAlias", MemoryTypeKind.Scalar, width, scalarType: "uintptr_t"),
                    new("long", "long", MemoryTypeKind.Scalar, width, scalarType: "unsigned long"),
                ];
                var session = new MemorySession(new MemorySchema(definitions, isLittleEndian: little, pointerSize: width, options: new CStructCompilationOptions { CLongWidth = width * 8 }));
                byte[] bytes = new byte[width];
                bytes[little ? 0 : width - 1] = 42;
                var region = new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, width);
                foreach (string id in new[] { "pointerInteger", "pointerAlias", "long" })
                {
                    Assert.AreEqual(42UL, Convert.ToUInt64(session.Read(region, id)));

                    if (id == "long")
                    {
                        CollectionAssert.AreEqual(bytes, session.Serialize(id, 42UL));
                    }
                    else
                    {
                        // The reader accepts uintptr_t; the existing root writer rejects that spelling.
                        Assert.Throws<CStructPathException>(() => session.Serialize(id, 42UL));
                    }
                }
            }
        }
    }

    /// <summary>Declared enums with identical roots retain their own value names, including alongside a primitive alias.</summary>
    [TestMethod]
    public void DeclaredScalars_KeepTheirDeclarations()
    {
        MemoryTypeDefinition[] definitions =
        [
            new("plain", "plain", MemoryTypeKind.Scalar, 2, scalarType: "uint16"),
            new("first", "first", MemoryTypeKind.Scalar, 2, scalarType: "kind", declaration: "enum kind : uint16 { first = 1 };"),
            new("second", "second", MemoryTypeKind.Scalar, 2, scalarType: "kind", declaration: "enum kind : uint16 { second = 1 };"),
        ];
        var session = new MemorySession(new MemorySchema(definitions));
        var region = new MemoryRegion(new ByteArrayMemorySource("image", [1, 0]), 0, 2);
        Assert.AreEqual((ushort)1, session.Read(region, "plain"));
        foreach (string id in new[] { "first", "second" })
        {
            var value = (EnumValueResult)session.Read(region, id)!;
            Assert.AreEqual(id, value.Name);
            CollectionAssert.AreEqual(new byte[] { 1, 0 }, session.Serialize(id, value));
            CollectionAssert.AreEqual(new byte[] { 1, 0 }, session.PlanUpdate(region, id, string.Empty, value).Fragments[0].Replacement);
        }
    }

    /// <summary>Prelude aliases and caller-defined symbols continue to determine every repeated scalar's layout.</summary>
    [TestMethod]
    public void PreludeAndDefinedOptions_KeepAliasMeaning()
    {
        const string prelude = "#ifdef WIDE\ntypedef uint32 selected;\n#else\ntypedef uint16 selected;\n#endif\n";
        foreach (bool wide in new[] { false, true })
        {
            int size = wide ? 4 : 2;
            var options = new CStructCompilationOptions { Prelude = prelude, Defined = new HashSet<string>(wide ? new[] { "WIDE" } : Array.Empty<string>()) };
            var session = new MemorySession(new MemorySchema(
                [
                    new("one", "one", MemoryTypeKind.Scalar, size, scalarType: "selected"),
                    new("two", "two", MemoryTypeKind.Scalar, size, scalarType: "selected"),
                ],
                options: options));
            byte[] bytes = new byte[size];
            bytes[0] = 42;
            var region = new MemoryRegion(new ByteArrayMemorySource("image", bytes), 0, size);
            Assert.AreEqual(42UL, Convert.ToUInt64(session.Read(region, "one")));
            CollectionAssert.AreEqual(bytes, session.Serialize("two", 42));
        }
    }

    /// <summary>Custom registration retains all construction callbacks, even when the registered codec is unused.</summary>
    /// <param name="used">Whether the repeated scalars name the registered custom codec.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void CustomRegistrations_PreserveConstructionInteractions(bool used)
    {
        var codec = new ObservedCodec();
        var options = new CStructCompilationOptions { Codecs = new[] { codec } };
        string root = used ? "observed" : "uint8";
        MemoryTypeDefinition[] definitions =
        [
            new("one", "one", MemoryTypeKind.Scalar, 1, scalarType: root),
            new("two", "two", MemoryTypeKind.Scalar, 1, scalarType: root),
            new("three", "three", MemoryTypeKind.Scalar, 1, scalarType: root),
        ];
        foreach (MemoryTypeDefinition definition in definitions)
        {
            _ = new MemorySchema([definition], options: options);
        }

        string[] expected = codec.Interactions.ToArray();
        Assert.IsGreaterThan(0, expected.Length);
        codec.Interactions.Clear();
        var session = new MemorySession(new MemorySchema(definitions, options: options));
        CollectionAssert.AreEqual(expected, codec.Interactions.ToArray());
        var region = new MemoryRegion(new ByteArrayMemorySource("image", [42]), 0, 1);
        Assert.AreEqual((byte)42, session.Read(region, "two"));
        CollectionAssert.AreEqual(new byte[] { 7 }, session.Serialize("three", (byte)7));
    }

    /// <summary>Concurrent first reads and writes through repeated roots retain independent results and patch bytes.</summary>
    [TestMethod]
    public void RepeatedScalars_SupportConcurrentFirstUse()
    {
        var schema = new MemorySchema([
            new("one", "one", MemoryTypeKind.Scalar, 4, scalarType: "uint32"),
            new("two", "two", MemoryTypeKind.Scalar, 4, scalarType: "uint32"),
        ]);

        // Each worker owns its source and context while sharing the schema's lazily prepared scalar codecs.
        Parallel.For(0, 32, index =>
        {
            var session = new MemorySession(schema);
            string id = index % 2 == 0 ? "one" : "two";
            var region = new MemoryRegion(new ByteArrayMemorySource("image", [(byte)index, 0, 0, 0]), 0, 4);
            Assert.AreEqual((uint)index, session.Read(region, id));
            byte[] expected = [(byte)(index + 1), 0, 0, 0];
            CollectionAssert.AreEqual(expected, session.Serialize(id, (uint)(index + 1)));
            CollectionAssert.AreEqual(expected, session.PlanUpdate(region, id, string.Empty, (uint)(index + 1)).Fragments[0].Replacement);
        });
    }

    /// <summary>Repeated definitions retain schema budgets, and cancellation prevents construction and all scalar operations.</summary>
    [TestMethod]
    public void RepeatedScalars_KeepBudgetsAndCancellation()
    {
        MemoryTypeDefinition[] definitions =
        [
            new("one", "one", MemoryTypeKind.Scalar, 4, scalarType: "uint32"),
            new("two", "two", MemoryTypeKind.Scalar, 4, scalarType: "uint32"),
        ];

        // Equal scalar roots still represent two definitions for the schema's type budget.
        Assert.Throws<CStructLayoutException>(() => new MemorySchema(definitions, maxTypes: 1));
        var session = new MemorySession(new MemorySchema(definitions));
        var region = new MemoryRegion(new ByteArrayMemorySource("image", [1, 0, 0, 0]), 0, 4);
        var limited = new MemoryAccessContext { MaxTotalBytes = 3 };

        // The full scalar request must be charged before decoding, even when its codec has already been prepared.
        MemoryAccessException failure = Assert.Throws<MemoryAccessException>(() => session.Read(region, "two", context: limited));
        Assert.AreEqual(MemoryFailure.BudgetExceeded, failure.Failure);
        Assert.AreEqual(0L, limited.BytesRequested);
        Assert.AreEqual(1, limited.Requests);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var cancelled = new MemoryAccessContext { CancellationToken = cancellation.Token };

        // Each entry point must observe cancellation before reading bytes or publishing a staged result.
        Assert.Throws<OperationCanceledException>(() => new MemorySchema(definitions, cancellationToken: cancellation.Token));

        // Read cancellation precedes scalar decoding.
        Assert.Throws<OperationCanceledException>(() => session.Read(region, "two", context: cancelled));

        // Serialization cancellation precedes result allocation and encoding.
        Assert.Throws<OperationCanceledException>(() => session.Serialize("two", 1U, cancelled));

        // Patch cancellation precedes reading the expected bytes.
        Assert.Throws<OperationCanceledException>(() => session.PlanUpdate(region, "two", string.Empty, 1U, cancelled));
        Assert.AreEqual(0L, cancelled.BytesRequested);
        Assert.AreEqual(0, cancelled.Requests);
    }

    /// <summary>A one-byte codec whose thread-safe trace makes metadata property callbacks observable.</summary>
    private sealed class ObservedCodec : ICustomCodec
    {
        /// <summary>Gets the ordered callbacks made while registering and using this codec.</summary>
        public ConcurrentQueue<string> Interactions { get; } = new();

        /// <summary>Gets the registered name and records its observation.</summary>
        public string Name
        {
            get
            {
                this.Interactions.Enqueue(nameof(this.Name));
                return "observed";
            }
        }

        /// <summary>Gets the encoded width in bytes and records its observation.</summary>
        public int? FixedSize
        {
            get
            {
                this.Interactions.Enqueue(nameof(this.FixedSize));
                return 1;
            }
        }

        /// <summary>Gets the one-byte alignment and records its observation.</summary>
        public int Alignment
        {
            get
            {
                this.Interactions.Enqueue(nameof(this.Alignment));
                return 1;
            }
        }

        /// <summary>Reads a single byte and records the callback without taking ownership of input.</summary>
        /// <param name="source">The input window.</param>
        /// <param name="value">Receives the byte, or null for empty input.</param>
        /// <param name="bytesConsumed">Receives one on success, zero otherwise.</param>
        /// <returns>Done when the input contains a byte, otherwise NeedMoreData.</returns>
        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
        {
            this.Interactions.Enqueue(nameof(this.Read));
            value = source.IsEmpty ? null : source[0];
            bytesConsumed = source.IsEmpty ? 0 : 1;
            return source.IsEmpty ? OperationStatus.NeedMoreData : OperationStatus.Done;
        }

        /// <summary>Writes a single byte and records the callback without retaining the output window.</summary>
        /// <param name="destination">The output window.</param>
        /// <param name="value">The byte to encode.</param>
        /// <param name="bytesWritten">Receives one on success, zero otherwise.</param>
        /// <returns>Done for a byte written, DestinationTooSmall for no room, or InvalidData for another value type.</returns>
        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            this.Interactions.Enqueue(nameof(this.Write));
            bytesWritten = 0;
            if (value is not byte number)
            {
                return OperationStatus.InvalidData;
            }

            if (destination.IsEmpty)
            {
                return OperationStatus.DestinationTooSmall;
            }

            destination[0] = number;
            bytesWritten = 1;
            return OperationStatus.Done;
        }
    }
}
