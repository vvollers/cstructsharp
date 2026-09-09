namespace CStructSharpTests;

using System.Collections.Immutable;
using System.Dynamic;
using System.Reflection;
using CStructSharp;
using CStructSharp.Structure;

/// <summary>Verifies that operation-time behavior is owned by immutable compiled descriptors.</summary>
[TestClass]
public class CompiledIntermediateRepresentationTests
{
    /// <summary>
    ///     word and base_word both resolve to uint16.
    /// </summary>
    /// <remarks>
    ///     The compiled model must record two-byte elements, a four-byte array, and a separate two-byte pointer slot at
    ///     offset 6. Cached readers, writers, and immutable field ordering let all operations use the same validated
    ///     facts instead of rediscovering the layout.
    /// </remarks>
    [TestMethod]
    public void CompiledDescriptors_CacheCanonicalTypeShapeAndPlacement()
    {
        const string layout = """
                              typedef uint16 base_word;
                              typedef base_word word;
                              struct root {
                                  word count;
                                  word values[2];
                                  word *link;
                              };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 2);
        CompiledLayoutModel model = cstruct.CompiledModel;

        Assert.AreSame(model.Symbols["uint16"].Symbol, model.Symbols["base_word"].Symbol);
        Assert.AreSame(model.Symbols["uint16"].Symbol, model.Symbols["word"].Symbol);
        Assert.AreEqual(0, model.Symbols["word"].PointerDepth);

        Struct root = cstruct.GetStruct("root");
        var compiledRoot = (CompiledCompositeType)model.Composites[root].Definition!;
        Assert.AreEqual(3, compiledRoot.Fields.Length);

        CompiledField count = compiledRoot.Fields[0];
        Assert.AreEqual("uint16", count.EffectiveField.Type.Name);
        Assert.AreEqual("uint16", count.CodecName);
        Assert.AreEqual(2, count.Alignment);
        Assert.AreEqual(2, count.FixedElementSize);
        Assert.AreEqual(1, count.FixedArrayCount);
        Assert.AreEqual(2, count.FixedStorageSize);
        Assert.AreEqual(0, count.FixedOffset);
        Assert.IsNotNull(count.Reader);
        Assert.IsNotNull(count.Writer);

        CompiledField values = compiledRoot.Fields[1];
        Assert.AreEqual(2, values.FixedArrayCount);
        Assert.AreEqual(2, values.FixedElementSize);
        Assert.AreEqual(4, values.FixedStorageSize);
        Assert.AreEqual(2, values.FixedOffset);
        Assert.AreEqual(2, values.SelectArrayElement().FixedStorageSize);

        CompiledField link = compiledRoot.Fields[2];
        Assert.AreEqual(1, link.PointerDepth);
        Assert.AreEqual("pointer", link.CodecName);
        Assert.AreEqual(2, link.FixedElementSize);
        Assert.AreEqual(6, link.FixedOffset);
        CompiledField pointerTarget = link.SelectPointerTarget(0, null, null, null, 2);
        Assert.AreEqual(0, pointerTarget.PointerDepth);
        Assert.AreEqual(2, pointerTarget.Alignment);
        Assert.AreEqual(2, pointerTarget.FixedElementSize);
        Assert.AreSame(link.Reader, pointerTarget.Reader);
        Assert.AreSame(link.Writer, pointerTarget.Writer);
        CompiledField remainingPointer = link.SelectPointerTarget(1, null, null, null, 2);
        Assert.AreEqual(1, remainingPointer.PointerDepth);
        Assert.AreEqual("pointer", remainingPointer.CodecName);
        Assert.AreEqual(2, remainingPointer.FixedStorageSize);
        Assert.IsInstanceOfType(model.Declarations, typeof(System.Collections.Immutable.ImmutableDictionary<string, CStructElement>));
        Assert.IsInstanceOfType(model.Fields, typeof(System.Collections.Immutable.ImmutableDictionary<Field, CompiledField>));
        Assert.AreEqual(3, model.Fields.Count);
        Assert.IsInstanceOfType(compiledRoot.Fields, typeof(System.Collections.Immutable.ImmutableArray<CompiledField>));
        Assert.IsInstanceOfType(
            compiledRoot.FieldsByName,
            typeof(System.Collections.Immutable.ImmutableDictionary<string, CompiledField>));
        Assert.AreSame(values, compiledRoot.FieldsByName["values"]);
        Assert.Throws<CStructLayoutException>(
            () => model.Symbols["word"].Symbol.Bind(
                new CompiledPrimitiveType(model.Symbols["word"].Symbol)));
    }

    /// <summary>
    ///     COUNT defaults to 2 but can be overridden for an operation.
    /// </summary>
    /// <remarks>
    ///     The compiler must therefore keep values' size and tail's offset variable rather than freezing the default
    ///     into fixed metadata. Address lookup must use the current count, including an override, when finding the
    ///     tail.
    /// </remarks>
    [TestMethod]
    public void CompiledDescriptors_KeepRuntimeSizeStrategiesVariable()
    {
        const string layout = """
                              #define COUNT 2
                              struct root {
                                  uint16 values[COUNT];
                                  byte tail;
                              };
                              """;
        var cstruct = new CStruct(layout);
        Struct root = cstruct.GetStruct("root");
        var compiledRoot =
            (CompiledCompositeType)cstruct.CompiledModel.Composites[root].Definition!;

        Assert.IsNull(compiledRoot.Symbol.FixedSize);
        Assert.IsNull(compiledRoot.Fields[0].FixedArrayCount);
        Assert.IsNull(compiledRoot.Fields[0].FixedStorageSize);
        Assert.IsNull(compiledRoot.Fields[1].FixedOffset);

        using var stream = new MemoryStream(new byte[16]);
        Assert.AreEqual(4L, cstruct.ResolveAddress(stream, "root.tail"));
        Assert.AreEqual(
            6L,
            cstruct.ResolveAddress(
                stream,
                "root.tail",
                new Dictionary<string, Expr> { ["COUNT"] = new Literal(3), }));
    }

    /// <summary>
    ///     left.value contains one byte and right.value contains a uint16.
    /// </summary>
    /// <remarks>
    ///     Their identical field names must refer to distinct compiled child types with sizes one and two. Neither
    ///     anonymous child becomes a global symbol named value, preventing unrelated scopes from sharing the wrong
    ///     layout.
    /// </remarks>
    [TestMethod]
    public void CompiledDescriptors_KeepInlineTypeIdentityLexicallyScoped()
    {
        const string layout = """
                              struct left { struct { byte x; } value; };
                              struct right { struct { uint16 y; } value; };
                              """;
        var cstruct = new CStruct(layout);
        Struct left = cstruct.GetStruct("left");
        Struct right = cstruct.GetStruct("right");
        var compiledLeft =
            (CompiledCompositeType)cstruct.CompiledModel.Composites[left].Definition!;
        var compiledRight =
            (CompiledCompositeType)cstruct.CompiledModel.Composites[right].Definition!;

        Assert.AreNotSame(compiledLeft.Fields[0].Type.Symbol, compiledRight.Fields[0].Type.Symbol);
        Assert.IsFalse(cstruct.CompiledModel.Symbols.ContainsKey("value"));
        Assert.AreSame(compiledLeft.Fields[0], compiledLeft.FieldsByName["value"]);
        Assert.AreSame(compiledRight.Fields[0], compiledRight.FieldsByName["value"]);
        Assert.AreEqual(4, cstruct.CompiledModel.Composites.Count);
        Assert.AreEqual(1, compiledLeft.Fields[0].Type.Symbol.FixedSize);
        Assert.AreEqual(2, compiledRight.Fields[0].Type.Symbol.FixedSize);
    }

    /// <summary>
    ///     An anonymous promoted member's own compiled field (LANG-14) is reachable through
    ///     <see cref="CompiledCompositeType.PromotedFields"/>, a named inline struct's composite has none, and the
    ///     accessor only ever reports one level - the outer composite's grandchild is not included.
    /// </summary>
    [TestMethod]
    public void CompiledDescriptors_ExposePromotedFieldsOneLevelAtATime()
    {
        var named = new CStruct("struct root { struct { uint8 x; } inner; };", pointerSize: 1);
        var compiledNamed =
            (CompiledCompositeType)named.CompiledModel.Composites[named.GetStruct("root")].Definition!;
        Assert.AreEqual(0, compiledNamed.PromotedFields.Count);

        var promoted = new CStruct("struct root { struct { uint8 x; }; };", pointerSize: 1);
        var compiledPromoted =
            (CompiledCompositeType)promoted.CompiledModel.Composites[promoted.GetStruct("root")].Definition!;
        Assert.AreEqual(1, compiledPromoted.PromotedFields.Count);
        Assert.IsTrue(compiledPromoted.PromotedFields.Contains(compiledPromoted.Fields[0]));

        var transitive = new CStruct("struct root { struct { struct { uint8 x; }; }; };", pointerSize: 1);
        var compiledTransitive =
            (CompiledCompositeType)transitive.CompiledModel.Composites[transitive.GetStruct("root")].Definition!;
        Assert.AreEqual(1, compiledTransitive.PromotedFields.Count);
    }

    /// <summary>
    ///     The compiled model must record shared low/high bit offsets, uint16 enum storage, and a terminated name[]
    ///     whose length remains variable.
    /// </summary>
    /// <remarks>
    ///     A char pointer must select a string reader after dereferencing, and union members must all have offset zero.
    ///     These facts must be prepared consistently before stream operations.
    /// </remarks>
    [TestMethod]
    public void CompiledDescriptors_CaptureEnumUnionBitfieldAndFlexibleStrategies()
    {
        const string layout = """
                              enum mode : uint16 { Off = 0, On = 1 };
                              struct payload {
                                  uint8 low:4;
                                  uint8 high:4;
                                  mode state;
                                  char name[];
                              };
                              union choice {
                                  uint32 wide;
                                  uint8 narrow;
                              };
                              """;
        var cstruct = new CStruct(layout, aligned: true);
        CompiledLayoutModel model = cstruct.CompiledModel;

        CompiledTypeReference mode = model.Symbols["mode"];
        Assert.AreEqual(CompiledTypeKind.Enum, mode.Symbol.Kind);
        var compiledMode = (CompiledEnumType)mode.Symbol.Definition!;
        Assert.AreEqual("uint16", compiledMode.Underlying.TerminalName);
        Assert.AreSame(model.Symbols["uint16"].Symbol, compiledMode.Underlying.Symbol);

        var payload = (CompiledCompositeType)model.Composites[cstruct.GetStruct("payload")].Definition!;
        Assert.AreEqual(0, payload.FieldsByName["low"].FixedOffset);
        Assert.AreEqual(0, payload.FieldsByName["low"].BitOffset);
        Assert.AreEqual(1, payload.FieldsByName["low"].BitStorageSize);
        Assert.AreEqual(0, payload.FieldsByName["high"].FixedOffset);
        Assert.AreEqual(4, payload.FieldsByName["high"].BitOffset);
        Assert.AreEqual("uint16", payload.FieldsByName["state"].CodecName);
        Assert.AreEqual(2, payload.FieldsByName["state"].FixedOffset);

        CompiledField name = payload.FieldsByName["name"];
        Assert.AreEqual(CompiledArrayKind.Flexible, name.Array.Kind);
        Assert.AreEqual(4, name.FixedOffset);
        Assert.IsNull(name.FixedStorageSize);
        Assert.IsTrue(name.IsUnsizedCharacterArray);
        Assert.IsNotNull(name.TerminatedReader);
        Assert.IsNotNull(name.TerminatedWriter);
        Assert.IsNull(payload.Symbol.FixedSize);

        var textPointerLayout = new CStruct("struct text_root { char *text; };", pointerSize: 2);
        Struct textRoot = textPointerLayout.GetStruct("text_root");
        var compiledTextRoot =
            (CompiledCompositeType)textPointerLayout.CompiledModel.Composites[textRoot].Definition!;
        CompiledField textPointer = compiledTextRoot.FieldsByName["text"];
        CompiledField terminatedTarget = textPointer.SelectPointerTarget(
            0,
            "cstring",
            textPointer.TerminatedReader,
            textPointer.TerminatedWriter,
            2);
        Assert.AreEqual("cstring", terminatedTarget.EffectiveField.Type.Name);
        Assert.AreEqual(0, terminatedTarget.PointerDepth);
        Assert.AreEqual(1, terminatedTarget.Alignment);
        Assert.IsNull(terminatedTarget.FixedElementSize);
        Assert.AreSame(textPointer.TerminatedReader, terminatedTarget.Reader);
        Assert.AreSame(textPointer.TerminatedWriter, terminatedTarget.Writer);

        var choice = (CompiledCompositeType)model.Composites[cstruct.GetStruct("choice")].Definition!;
        Assert.AreEqual(CompiledTypeKind.Union, choice.Symbol.Kind);
        Assert.AreEqual(4, choice.Symbol.FixedSize);
        Assert.IsTrue(choice.Fields.All(field => field.FixedOffset == 0));
        Assert.AreEqual(
            choice.Symbol.FixedSize,
            choice.Fields.Max(field => field.FixedStorageSize));
    }

    /// <summary>
    ///     A chain of aliases describes count, two array values, and a pointer to another uint16.
    /// </summary>
    /// <remarks>
    ///     The packed root remains eight bytes and the pointer reaches 0xDEF0. Metadata queries and every read/write
    ///     path must agree, even though construction-time tables are frozen and cannot be modified after compilation.
    /// </remarks>
    [TestMethod]
    public void PrimitiveTypedefSlice_UsesCompiledFactsAcrossEveryOperation()
    {
        const string layout = """
                              typedef uint16 base_word;
                              typedef base_word word;
                              struct root {
                                  word count;
                                  word values[2];
                                  word *link;
                              };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 2);
        byte[] bytes = [0x34, 0x12, 0x78, 0x56, 0xBC, 0x9A, 0x08, 0x00, 0xF0, 0xDE,];

        AssertPrivateDictionaryRejectsMutation(
            cstruct,
            "cStructElements",
            "word",
            new Typedef(new Identifier("word"), new Identifier("uint8")));
        AssertPrivateDictionaryRejectsMutation(
            cstruct,
            "cStructElements",
            "root",
            new Struct(
                new Identifier("root"),
                System.Collections.Immutable.ImmutableList.Create(
                    new Field(new Identifier("uint8"), new Identifier("wrong"), Field.NoArray, 0)),
                false));
        AssertPrivateDictionaryRejectsMutation(cstruct, "fieldAlignments", "uint16", (byte)1);
        AssertPrivateDictionaryRejectsMutation<Func<Stream, object>>(
            cstruct,
            "fieldHandlers",
            "uint16",
            _ => throw new AssertFailedException("Parse performed a runtime primitive-handler lookup."));
        AssertPrivateDictionaryRejectsMutation<Action<Stream, object>>(
            cstruct,
            "writeHandlers",
            "uint16",
            (_, _) => throw new AssertFailedException("Write performed a runtime primitive-handler lookup."));

        Assert.AreEqual(3, cstruct.GetStruct("root").Fields.Count);
        Assert.AreEqual(2, cstruct.GetStructAlignmentInBytes("root"));
        Assert.AreEqual(8, cstruct.GetStructSizeInBytes("root"));

        using (var parseStream = new MemoryStream(bytes))
        {
            dynamic parsed = cstruct.ParseStream(parseStream, "root");
            Assert.AreEqual((ushort)0x1234, (ushort)parsed.count);
            Assert.AreEqual((ushort)0x5678, (ushort)parsed.values[0]);
            Assert.AreEqual((ushort)0x9ABC, (ushort)parsed.values[1]);
            Assert.AreEqual(8L, ((CStructSharp.Pointer)parsed.link).Address);
            Assert.AreEqual((ushort)0xDEF0, (ushort)((CStructSharp.Pointer)parsed.link).Value!);
            Assert.AreEqual(8L, parseStream.Position);
        }

        using (var debugStream = new MemoryStream(bytes))
        {
            (List<DebugData> debug, dynamic result) = cstruct.ParseStreamWithDebug(debugStream, "root");
            dynamic parsed = ((IDictionary<string, object?>)result)["root"]!;
            Assert.AreEqual((ushort)0x1234, (ushort)parsed.count);
            Assert.IsTrue(debug.Any(item => item.TypeName == "uint16"));
            Assert.AreEqual(8L, debugStream.Position);
        }

        using (var queryStream = new MemoryStream(bytes) { Position = 1, })
        {
            Assert.AreEqual(5L, cstruct.ResolveAddress(queryStream, "root.values[1]"));
            Assert.AreEqual(2, cstruct.GetDynamicArrayLength(queryStream, "root.values"));
            Assert.AreEqual(1L, queryStream.Position);
        }

        var value = new
        {
            count = (ushort)0x1234,
            values = new ushort[] { 0x5678, 0x9ABC, },
            link = 8,
        };
        CollectionAssert.AreEqual(bytes[..8], cstruct.Serialize("root", value));

        using (var writeStream = new MemoryStream())
        {
            cstruct.WriteStream(writeStream, "root", value);
            CollectionAssert.AreEqual(bytes[..8], writeStream.ToArray());
        }

        using (var updateStream = new MemoryStream((byte[])bytes.Clone()))
        {
            cstruct.UpdateStream(updateStream, "root.values[1]", (ushort)0x1357);
            cstruct.UpdateStream(updateStream, "root.link.value", (ushort)0x2468);
            CollectionAssert.AreEqual(
                new byte[] { 0x34, 0x12, 0x78, 0x56, 0x57, 0x13, 0x08, 0x00, 0x68, 0x24, },
                updateStream.ToArray());
            Assert.AreEqual(0L, updateStream.Position);
        }
    }

    /// <summary>
    ///     This reflection test checks that superseded private layout caches and sizing helpers are absent.
    /// </summary>
    /// <remarks>
    ///     There is no binary fixture: the expected result is one source of compiled layout facts. Keeping multiple
    ///     independent sizing engines could let parsing, address lookup, and writing disagree after a change.
    /// </remarks>
    [TestMethod]
    public void CompiledModel_IsTheOnlyLayoutCompiler()
    {
        const BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;
        const BindingFlags PrivateStatic = BindingFlags.Static | BindingFlags.NonPublic;

        Assert.IsNull(typeof(CStruct).GetField("compiledStructLayouts", PrivateInstance));
        Assert.IsNull(typeof(CStruct).GetMethod("ValidateAndCompileLayouts", PrivateInstance));
        Assert.IsNull(typeof(CStruct).GetMethod("ResolveFieldAliases", PrivateStatic));
        Assert.IsFalse(
            typeof(CStruct).GetMethods(PrivateInstance).
                Any(method => method.Name == "GetStructSizeInBytes"));
    }

    /// <summary>
    ///     A hand-built two-dimensional shape (LANG-05) peels one dimension per call, exactly mirroring a 1-D
    ///     array's existing single-call collapse to <see cref="CompiledArrayShape.Scalar"/> once every dimension
    ///     has been consumed - no parser/compiled-model integration is exercised here, only the shape peeling
    ///     mechanism itself.
    /// </summary>
    [TestMethod]
    public void CompiledArrayShape_PeelsOneDimensionPerCall()
    {
        var twoDimensional = new CompiledArrayShape(
            CompiledArrayKind.Fixed,
            new Literal(3),
            3,
            ImmutableArray<string>.Empty,
            ImmutableArray.Create(
                new CompiledArrayDimension(new Literal(3), 3),
                new CompiledArrayDimension(new Literal(4), 4)));

        Assert.AreEqual(12, twoDimensional.TotalFixedElementCount);

        CompiledArrayShape oneDimensional = twoDimensional.PeelOuterDimension();
        Assert.AreEqual(CompiledArrayKind.Fixed, oneDimensional.Kind);
        Assert.AreEqual(4, oneDimensional.FixedCount);
        Assert.AreEqual(1, oneDimensional.Dimensions.Length);
        Assert.AreEqual(4, oneDimensional.TotalFixedElementCount);

        CompiledArrayShape scalar = oneDimensional.PeelOuterDimension();
        Assert.AreEqual(CompiledArrayKind.Scalar, scalar.Kind);
        Assert.AreEqual(0, scalar.Dimensions.Length);
        Assert.AreEqual(1, scalar.TotalFixedElementCount);
    }

    /// <summary>Every array shape compiled today (1-D or scalar) has exactly the Dimensions length LANG-05 expects.</summary>
    [TestMethod]
    public void CompiledArrayShape_ExistingOneDimensionalShapesHaveASingleDimensionEntry()
    {
        var cstruct = new CStruct("struct root { uint8 fixed_values[3]; uint8 tail; };", pointerSize: 1);
        Struct root = cstruct.GetStruct("root");
        var compiledRoot = (CompiledCompositeType)cstruct.CompiledModel.Composites[root].Definition!;

        CompiledArrayShape fixedShape = compiledRoot.Fields[0].Array;
        Assert.AreEqual(1, fixedShape.Dimensions.Length);
        Assert.AreEqual(3, fixedShape.TotalFixedElementCount);

        CompiledArrayShape scalarShape = compiledRoot.Fields[1].Array;
        Assert.AreEqual(0, scalarShape.Dimensions.Length);
        Assert.AreEqual(1, scalarShape.TotalFixedElementCount);
    }

    /// <summary>
    ///     <see cref="CompiledSizeQueries.GetCompiledFieldStorageSize"/> now sources its element count from the
    ///     new <see cref="CompiledSizeQueries.GetCompiledFieldTotalElementCount"/> (LANG-05) instead of
    ///     <see cref="CompiledSizeQueries.GetCompiledArrayCount"/> directly - for an existing 1-D field this must
    ///     produce the exact same result, since a 1-D field's Dimensions list has exactly the one entry
    ///     <see cref="CompiledSizeQueries.GetCompiledArrayCount"/> already evaluates. A genuine multidimensional
    ///     field is exercised once the grammar supports declaring one (see the LANG-05 grammar/compiled-model
    ///     seam).
    /// </summary>
    [TestMethod]
    public void CompiledSizeQueries_StorageSize_UnchangedForExistingOneDimensionalArrays()
    {
        var cstruct = new CStruct("struct root { uint8 values[4]; uint8 tail; };", pointerSize: 1, aligned: false);

        Assert.AreEqual(5, cstruct.GetStructSizeInBytes("root"));

        Struct root = cstruct.GetStruct("root");
        var compiledRoot = (CompiledCompositeType)cstruct.CompiledModel.Composites[root].Definition!;
        CompiledField valuesField = compiledRoot.Fields[0];
        Assert.AreEqual(1, valuesField.Array.Dimensions.Length);
        Assert.AreEqual(4, valuesField.FixedArrayCount);
        Assert.AreEqual(4, valuesField.Array.TotalFixedElementCount);
        Assert.AreEqual(4, valuesField.FixedStorageSize);
    }

    /// <summary>Uses reflection to prove that a private construction table discarded its mutable builder.</summary>
    private static void AssertPrivateDictionaryRejectsMutation<TValue>(
        CStruct cstruct,
        string fieldName,
        string key,
        TValue replacement)
    {
        FieldInfo field = typeof(CStruct).GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic) ??
                          throw new AssertFailedException("Private compatibility table was not found: " + fieldName);
        object table = field.GetValue(cstruct) ??
                       throw new AssertFailedException("Private construction table was null: " + fieldName);
        PropertyInfo frozen = table.GetType().GetProperty("IsFrozen") ??
                              throw new AssertFailedException("Construction table has no frozen state: " + fieldName);
        Assert.AreEqual(true, frozen.GetValue(table), fieldName);

        PropertyInfo indexer = table.GetType().GetProperty("Item") ??
                               throw new AssertFailedException("Construction table has no indexer: " + fieldName);
        TargetInvocationException exception = Assert.Throws<TargetInvocationException>(
            () => indexer.SetValue(table, replacement, new object[] { key, }));
        Assert.IsInstanceOfType<InvalidOperationException>(exception.InnerException);
    }
}
