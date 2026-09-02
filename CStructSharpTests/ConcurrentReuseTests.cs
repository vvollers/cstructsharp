namespace CStructSharp.Tests;

using System.Collections.ObjectModel;
using System.Dynamic;
using System.Reflection;

/// <summary>Defines the lock-free concurrent-reuse contract accepted by ADR-007 and ADR-011.</summary>
[TestClass]
public class ConcurrentReuseTests
{
    private const string Layout = """
                                  union choice { uint16 wide; uint8 narrow; };
                                  struct root {
                                      uint8 prefix;
                                      uint16> big;
                                      uint16< little;
                                      uint16 values[N];
                                      uint8 low:4;
                                      uint8 high:4;
                                      choice selected;
                                      char label[2];
                                      uint8 **ptr;
                                  };
                                  """;

    /// <summary>
    ///     Requires readonly instance references, genuinely immutable public metadata, and explicitly frozen recursive
    ///     symbols before a constructed layout becomes observable.
    /// </summary>
    [TestMethod]
    public void ConstructedLayout_PublishesOnlyFrozenSharedState()
    {
        var cstruct = new CStruct(
            "enum mode : uint8 { One=1 }; struct node { node *next; mode value; };",
            pointerSize: 1);

        FieldInfo[] instanceFields = typeof(CStruct).GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
        Assert.IsNotEmpty(instanceFields);
        foreach (FieldInfo field in instanceFields)
        {
            Assert.IsTrue(field.IsInitOnly, $"CStruct.{field.Name} must be readonly after construction.");
        }

        AssertFrozenMetadata(cstruct.CStructElements, nameof(cstruct.CStructElements));
        AssertFrozenMetadata(cstruct.FieldAlignments, nameof(cstruct.FieldAlignments));
        AssertFrozenMetadata(cstruct.FieldHandlers, nameof(cstruct.FieldHandlers));
        AssertFrozenMetadata(cstruct.WriteHandlers, nameof(cstruct.WriteHandlers));

        Type symbolType = typeof(CStruct).GetNestedType(
                              "CompiledTypeSymbol",
                              BindingFlags.NonPublic) ??
                          throw new AssertFailedException("CompiledTypeSymbol was not found.");
        PropertyInfo frozenProperty = symbolType.GetProperty(
                                          "IsFrozen",
                                          BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic) ??
                                      throw new AssertFailedException(
                                          "CompiledTypeSymbol must expose its construction-sealed state.");
        IEnumerable<object> symbols = cstruct.CompiledModel.Symbols.Values.
            Select(reference => (object)reference.Symbol).
            Concat(cstruct.CompiledModel.Composites.Values).
            Distinct(ReferenceEqualityComparer.Instance);
        foreach (object symbol in symbols)
        {
            Assert.AreEqual(true, frozenProperty.GetValue(symbol), "Every published compiled symbol must be frozen.");
        }
    }

    /// <summary>Allows one construction bind, rejects rebinding, and refuses to publish an unbound recursive symbol.</summary>
    [TestMethod]
    public void CompiledTypeSymbol_SealsExactlyOnceAfterBinding()
    {
        var symbol = new CStruct.CompiledTypeSymbol(
            "value",
            CStruct.CompiledTypeKind.Primitive,
            null,
            1,
            1,
            null,
            null);
        var definition = new CStruct.CompiledPrimitiveType(symbol);

        symbol.Bind(definition);

        Assert.IsTrue(symbol.IsBound);
        Assert.IsFalse(symbol.IsFrozen);
        Assert.Throws<CStructLayoutException>(() => symbol.Bind(definition));

        symbol.Freeze();

        Assert.IsTrue(symbol.IsFrozen);
        Assert.Throws<CStructLayoutException>(() => symbol.Bind(definition));

        var unbound = new CStruct.CompiledTypeSymbol(
            "unbound",
            CStruct.CompiledTypeKind.Primitive,
            null,
            1,
            1,
            null,
            null);
        Assert.Throws<CStructLayoutException>(() => unbound.Freeze());
        Assert.IsFalse(unbound.IsFrozen);
    }

    /// <summary>
    ///     Reuses one layout, variable snapshot, and option values across every core operation on distinct streams.
    ///     A barrier makes each group enter the library together instead of relying on scheduler timing.
    /// </summary>
    /// <param name="aligned">Whether portable field alignment is enabled.</param>
    /// <param name="isLittleEndian">Whether neutral numeric fields use little-endian encoding.</param>
    /// <returns>A task that completes after every coordinated operation worker finishes.</returns>
    [TestMethod]
    [DataRow(false, true)]
    [DataRow(false, false)]
    [DataRow(true, true)]
    [DataRow(true, false)]
    public async Task ConstructedLayout_SupportsConcurrentCoreOperations(
        bool aligned,
        bool isLittleEndian)
    {
        var cstruct = new CStruct(
            Layout,
            pointerSize: 1,
            aligned: aligned,
            isLittleEndian: isLittleEndian);
        IReadOnlyDictionary<string, int> variables = new ReadOnlyDictionary<string, int>(
            new Dictionary<string, int> { ["N"] = 2, });
        var readOptions = new ReadOptions();
        var writeOptions = new WriteOptions();
        var updateOptions = new UpdateOptions();

        IDictionary<string, object?> payload = CreatePayload(0);
        byte[] sizingBytes = cstruct.Serialize("root", payload, variables, writeOptions);
        int pointerCell = checked(sizingBytes.Length + 1);
        int targetAddress = checked(pointerCell + 2);
        payload = CreatePayload(pointerCell);
        byte[] rootBytes = cstruct.Serialize("root", payload, variables, writeOptions);
        byte[] source = new byte[targetAddress + 1];
        rootBytes.CopyTo(source, 0);
        source[pointerCell] = checked((byte)targetAddress);
        source[targetAddress] = 0x44;

        Action[] operationBodies =
        [
            () =>
            {
                using var stream = new MemoryStream((byte[])source.Clone());
                dynamic parsed = cstruct.ParseStream(stream, "root", variables, readOptions);
                Assert.AreEqual((byte)0xA5, (byte)parsed.prefix);
                Assert.AreEqual((ushort)0x1234, (ushort)parsed.big);
                Assert.AreEqual((ushort)0x5678, (ushort)parsed.little);
            },
            () =>
            {
                using var stream = new MemoryStream((byte[])source.Clone());
                Assert.AreEqual(
                    (ushort)0x1234,
                    cstruct.ReadValue<ushort>(stream, "root.big", variables, readOptions));
            },
            () =>
            {
                using var stream = new MemoryStream((byte[])source.Clone());
                (List<DebugData> debug, dynamic parsed) =
                    cstruct.ParseStreamWithDebug(stream, "root", variables, readOptions);
                Assert.IsNotEmpty(debug);
                Assert.IsNotNull(parsed);
            },
            () =>
            {
                using var stream = new MemoryStream((byte[])source.Clone());
                Assert.AreEqual(
                    targetAddress,
                    cstruct.ResolveAddress(stream, "root.ptr.value.value", variables, readOptions));
                Assert.AreEqual(0L, stream.Position);
            },
            () =>
            {
                using var stream = new MemoryStream((byte[])source.Clone());
                Assert.AreEqual(
                    2,
                    cstruct.GetDynamicArrayLength(stream, "root.values", variables, readOptions));
                Assert.AreEqual(0L, stream.Position);
            },
            () => CollectionAssert.AreEqual(
                rootBytes,
                cstruct.Serialize("root", CreatePayload(pointerCell), variables, writeOptions)),
            () =>
            {
                using var stream = new MemoryStream();
                cstruct.WriteStream(
                    stream,
                    "root",
                    CreatePayload(pointerCell),
                    variables,
                    writeOptions);
                CollectionAssert.AreEqual(rootBytes, stream.ToArray());
            },
            () =>
            {
                using var stream = new MemoryStream((byte[])source.Clone());
                cstruct.UpdateStream(
                    stream,
                    "root.values[1]",
                    (ushort)0xBEEF,
                    variables,
                    updateOptions);
                stream.Position = 0;
                dynamic parsed = cstruct.ParseStream(stream, "root", variables, readOptions);
                Assert.AreEqual((ushort)0xBEEF, (ushort)parsed.values[1]);
            },
            () =>
            {
                using var stream = new MemoryStream((byte[])source.Clone());
                cstruct.UpdateStream(
                    stream,
                    "root.ptr.value.value",
                    (byte)0x7E,
                    variables,
                    updateOptions);
                Assert.AreEqual((byte)0x7E, stream.ToArray()[targetAddress]);
                Assert.AreEqual(0L, stream.Position);
            },
            () =>
            {
                Assert.IsGreaterThan(0, cstruct.GetStructAlignmentInBytes("root"));
                Assert.AreEqual(
                    rootBytes.Length,
                    cstruct.Serialize("root", CreatePayload(pointerCell), variables, writeOptions).Length);
            },
        ];

        using var start = new Barrier(operationBodies.Length);
        Task[] operations = operationBodies.Select(
            operation => Task.Factory.StartNew(
                () =>
                {
                    start.SignalAndWait();
                    for (int iteration = 0; iteration < 24; iteration++)
                    {
                        operation();
                    }
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default)).ToArray();
        await Task.WhenAll(operations);
    }

    private static void AssertFrozenMetadata<TKey, TValue>(
        IReadOnlyDictionary<TKey, TValue> metadata,
        string name)
        where TKey : notnull
    {
        Assert.AreEqual(
            "System.Collections.Frozen",
            metadata.GetType().Namespace,
            $"{name} must be a true frozen snapshot, not a wrapper over a mutable dictionary.");
    }

    private static IDictionary<string, object?> CreatePayload(int pointerCell)
    {
        return new Dictionary<string, object?>
        {
            ["prefix"] = (byte)0xA5,
            ["big"] = (ushort)0x1234,
            ["little"] = (ushort)0x5678,
            ["values"] = new ushort[] { 0x1111, 0x2222, },
            ["low"] = (byte)5,
            ["high"] = (byte)10,
            ["selected"] = UnionValue.FromMember("choice", "wide", (ushort)0xBEEF),
            ["label"] = "AZ",
            ["ptr"] = pointerCell,
        };
    }
}
