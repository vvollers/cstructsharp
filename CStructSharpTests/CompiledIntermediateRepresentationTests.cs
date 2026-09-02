namespace CStructSharpTests;

using System.Dynamic;
using System.Reflection;
using CStructSharp;
using CStructSharp.Structure;

/// <summary>Verifies that operation-time behavior is owned by immutable compiled descriptors.</summary>
[TestClass]
public class CompiledIntermediateRepresentationTests
{
    /// <summary>
    ///     Verifies canonical alias identity, direct codec attachment, fixed array stride/storage, pointer shape, field
    ///     placement, and immutable declaration-order collections.
    /// </summary>
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
        CStruct.CompiledLayoutModel model = cstruct.CompiledModel;

        Assert.AreSame(model.Symbols["uint16"].Symbol, model.Symbols["base_word"].Symbol);
        Assert.AreSame(model.Symbols["uint16"].Symbol, model.Symbols["word"].Symbol);
        Assert.AreEqual(0, model.Symbols["word"].PointerDepth);

        Struct root = cstruct.GetStruct("root");
        var compiledRoot = (CStruct.CompiledCompositeType)model.Composites[root].Definition!;
        Assert.AreEqual(3, compiledRoot.Fields.Length);

        CStruct.CompiledField count = compiledRoot.Fields[0];
        Assert.AreEqual("uint16", count.EffectiveField.Type.Name);
        Assert.AreEqual("uint16", count.CodecName);
        Assert.AreEqual(2, count.Alignment);
        Assert.AreEqual(2, count.FixedElementSize);
        Assert.AreEqual(1, count.FixedArrayCount);
        Assert.AreEqual(2, count.FixedStorageSize);
        Assert.AreEqual(0, count.FixedOffset);
        Assert.IsNotNull(count.Reader);
        Assert.IsNotNull(count.Writer);

        CStruct.CompiledField values = compiledRoot.Fields[1];
        Assert.AreEqual(2, values.FixedArrayCount);
        Assert.AreEqual(2, values.FixedElementSize);
        Assert.AreEqual(4, values.FixedStorageSize);
        Assert.AreEqual(2, values.FixedOffset);
        Assert.AreEqual(2, values.SelectArrayElement().FixedStorageSize);

        CStruct.CompiledField link = compiledRoot.Fields[2];
        Assert.AreEqual(1, link.PointerDepth);
        Assert.AreEqual("pointer", link.CodecName);
        Assert.AreEqual(2, link.FixedElementSize);
        Assert.AreEqual(6, link.FixedOffset);
        CStruct.CompiledField pointerTarget = link.SelectPointerTarget(0, null, null, null, 2);
        Assert.AreEqual(0, pointerTarget.PointerDepth);
        Assert.AreEqual(2, pointerTarget.Alignment);
        Assert.AreEqual(2, pointerTarget.FixedElementSize);
        Assert.AreSame(link.Reader, pointerTarget.Reader);
        Assert.AreSame(link.Writer, pointerTarget.Writer);
        CStruct.CompiledField remainingPointer = link.SelectPointerTarget(1, null, null, null, 2);
        Assert.AreEqual(1, remainingPointer.PointerDepth);
        Assert.AreEqual("pointer", remainingPointer.CodecName);
        Assert.AreEqual(2, remainingPointer.FixedStorageSize);
        Assert.IsInstanceOfType(model.Declarations, typeof(System.Collections.Immutable.ImmutableDictionary<string, CStructElement>));
        Assert.IsInstanceOfType(model.Fields, typeof(System.Collections.Immutable.ImmutableDictionary<Field, CStruct.CompiledField>));
        Assert.AreEqual(3, model.Fields.Count);
        Assert.IsInstanceOfType(compiledRoot.Fields, typeof(System.Collections.Immutable.ImmutableArray<CStruct.CompiledField>));
        Assert.IsInstanceOfType(
            compiledRoot.FieldsByName,
            typeof(System.Collections.Immutable.ImmutableDictionary<string, CStruct.CompiledField>));
        Assert.AreSame(values, compiledRoot.FieldsByName["values"]);
        Assert.Throws<CStructLayoutException>(
            () => model.Symbols["word"].Symbol.Bind(
                new CStruct.CompiledPrimitiveType(model.Symbols["word"].Symbol)));
    }

    /// <summary>
    ///     Verifies that caller-dependent array counts retain an explicit runtime strategy and do not leak a default
    ///     define value into supposedly fixed offsets or extents.
    /// </summary>
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
            (CStruct.CompiledCompositeType)cstruct.CompiledModel.Composites[root].Definition!;

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

    /// <summary>Verifies that identical inline spellings retain separate identities and never enter the global symbol map.</summary>
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
            (CStruct.CompiledCompositeType)cstruct.CompiledModel.Composites[left].Definition!;
        var compiledRight =
            (CStruct.CompiledCompositeType)cstruct.CompiledModel.Composites[right].Definition!;

        Assert.AreNotSame(compiledLeft.Fields[0].Type.Symbol, compiledRight.Fields[0].Type.Symbol);
        Assert.IsFalse(cstruct.CompiledModel.Symbols.ContainsKey("value"));
        Assert.AreSame(compiledLeft.Fields[0], compiledLeft.FieldsByName["value"]);
        Assert.AreSame(compiledRight.Fields[0], compiledRight.FieldsByName["value"]);
        Assert.AreEqual(4, cstruct.CompiledModel.Composites.Count);
        Assert.AreEqual(1, compiledLeft.Fields[0].Type.Symbol.FixedSize);
        Assert.AreEqual(2, compiledRight.Fields[0].Type.Symbol.FixedSize);
    }

    /// <summary>
    ///     Verifies that enum storage, union extent, bit slices, and terminated flexible-array behavior are compiled
    ///     into direct immutable strategies rather than inferred independently by each operation.
    /// </summary>
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
        CStruct.CompiledLayoutModel model = cstruct.CompiledModel;

        CStruct.CompiledTypeReference mode = model.Symbols["mode"];
        Assert.AreEqual(CStruct.CompiledTypeKind.Enum, mode.Symbol.Kind);
        var compiledMode = (CStruct.CompiledEnumType)mode.Symbol.Definition!;
        Assert.AreEqual("uint16", compiledMode.Underlying.TerminalName);
        Assert.AreSame(model.Symbols["uint16"].Symbol, compiledMode.Underlying.Symbol);

        var payload = (CStruct.CompiledCompositeType)model.Composites[cstruct.GetStruct("payload")].Definition!;
        Assert.AreEqual(0, payload.FieldsByName["low"].FixedOffset);
        Assert.AreEqual(0, payload.FieldsByName["low"].BitOffset);
        Assert.AreEqual(1, payload.FieldsByName["low"].BitStorageSize);
        Assert.AreEqual(0, payload.FieldsByName["high"].FixedOffset);
        Assert.AreEqual(4, payload.FieldsByName["high"].BitOffset);
        Assert.AreEqual("uint16", payload.FieldsByName["state"].CodecName);
        Assert.AreEqual(2, payload.FieldsByName["state"].FixedOffset);

        CStruct.CompiledField name = payload.FieldsByName["name"];
        Assert.AreEqual(CStruct.CompiledArrayKind.Flexible, name.Array.Kind);
        Assert.AreEqual(4, name.FixedOffset);
        Assert.IsNull(name.FixedStorageSize);
        Assert.IsTrue(name.IsUnsizedCharacterArray);
        Assert.IsNotNull(name.TerminatedReader);
        Assert.IsNotNull(name.TerminatedWriter);
        Assert.IsNull(payload.Symbol.FixedSize);

        var textPointerLayout = new CStruct("struct text_root { char *text; };", pointerSize: 2);
        Struct textRoot = textPointerLayout.GetStruct("text_root");
        var compiledTextRoot =
            (CStruct.CompiledCompositeType)textPointerLayout.CompiledModel.Composites[textRoot].Definition!;
        CStruct.CompiledField textPointer = compiledTextRoot.FieldsByName["text"];
        CStruct.CompiledField terminatedTarget = textPointer.SelectPointerTarget(
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

        var choice = (CStruct.CompiledCompositeType)model.Composites[cstruct.GetStruct("choice")].Definition!;
        Assert.AreEqual(CStruct.CompiledTypeKind.Union, choice.Symbol.Kind);
        Assert.AreEqual(4, choice.Symbol.FixedSize);
        Assert.IsTrue(choice.Fields.All(field => field.FixedOffset == 0));
        Assert.AreEqual(
            choice.Symbol.FixedSize,
            choice.Fields.Max(field => field.FixedStorageSize));
    }

    /// <summary>
    ///     Proves that a compiled primitive typedef chain retains its resolved codec, width, pointer shape, and array
    ///     stride while every parsed-symbol and handler construction table rejects post-construction mutation.
    /// </summary>
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
    ///     Keeps the immutable compiled model as the only layout compiler instead of retaining the superseded
    ///     construction-time cache, parsed-field alias walker, and parsed-field sizing engine beside it.
    /// </summary>
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
