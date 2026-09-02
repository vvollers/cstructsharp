namespace CStructSharp.Tests;

using System.Dynamic;
using System.Globalization;
using System.Numerics;
using CStructSharp.Structure;
using CstructEnum = CStructSharp.Structure.Enum;

/// <summary>Verifies exact enum compilation, reading, writing, and traversal across every supported integer domain.</summary>
[TestClass]
public class EnumDomainTests
{
    /// <summary>Gets every signed/unsigned enum width in both layout byte orders.</summary>
    public static IEnumerable<object[]> IntegerDomains
    {
        get
        {
            (string Type, BigInteger Minimum, BigInteger Maximum, int Size)[] domains =
            [
                ("int8", sbyte.MinValue, sbyte.MaxValue, 1),
                ("uint8", byte.MinValue, byte.MaxValue, 1),
                ("int16", short.MinValue, short.MaxValue, 2),
                ("uint16", ushort.MinValue, ushort.MaxValue, 2),
                ("int32", int.MinValue, int.MaxValue, 4),
                ("uint32", uint.MinValue, uint.MaxValue, 4),
                ("int64", long.MinValue, long.MaxValue, 8),
                ("uint64", ulong.MinValue, ulong.MaxValue, 8),
            ];

            foreach ((string type, BigInteger minimum, BigInteger maximum, int size) in domains)
            {
                yield return [type, minimum, maximum, size, false,];
                yield return [type, minimum, maximum, size, true,];
            }
        }
    }

    /// <summary>Gets each accepted direct spelling plus one scalar typedef chain.</summary>
    public static IEnumerable<object[]> AcceptedBackingTypes
    {
        get
        {
            string[] spellings =
            [
                "byte", "uint8", "int8",
                "uint16", "ushort", "int16", "short",
                "uint32", "uint", "int32", "int",
                "uint64", "ulong", "int64", "long",
            ];
            foreach (string spelling in spellings)
            {
                yield return [string.Empty, spelling,];
            }

            yield return ["typedef uint64 storage_base; typedef storage_base storage;", "storage",];
        }
    }

    /// <summary>Reads, debugs, addresses, serializes, writes, and updates exact boundary values for every domain.</summary>
    /// <param name="backingType">The canonical signed or unsigned backing type.</param>
    /// <param name="minimum">The backing domain's inclusive minimum.</param>
    /// <param name="maximum">The backing domain's inclusive maximum.</param>
    /// <param name="size">The backing storage size in bytes.</param>
    /// <param name="isLittleEndian">Whether neutral multi-byte storage uses least-significant-byte-first order.</param>
    [TestMethod]
    [DynamicData(nameof(IntegerDomains))]
    public void IntegerDomains_RoundTripBoundariesAcrossOperations(
        string backingType,
        BigInteger minimum,
        BigInteger maximum,
        int size,
        bool isLittleEndian)
    {
        string layout = FormattableString.Invariant(
            $"enum state : {backingType} {{ Minimum = {minimum}, Known = 1, Maximum = {maximum} }}; struct root {{ state value; }};");
        var cstruct = new CStruct(layout, isLittleEndian: isLittleEndian);

        foreach (BigInteger candidate in new[] { minimum, new BigInteger(2), maximum, })
        {
            byte[] expected = Encode(candidate, size, isLittleEndian);
            using var readStream = new MemoryStream(expected);
            dynamic parsed = cstruct.ParseStream(readStream, "root");
            var parsedValue = (EnumValueResult)parsed.value;
            string? expectedName = candidate == minimum
                                       ? "Minimum"
                                       : candidate == maximum
                                           ? "Maximum"
                                           : null;

            Assert.AreEqual("state", parsedValue.Enum);
            Assert.AreEqual(expectedName, parsedValue.Name);
            Assert.AreEqual(candidate, parsedValue.Value);
            Assert.AreEqual(backingType, parsedValue.StorageType);
            Assert.AreEqual(size * 8, parsedValue.BitWidth);
            Assert.AreEqual(backingType.StartsWith("int", StringComparison.Ordinal), parsedValue.IsSigned);
            Assert.AreEqual(ToRawBits(candidate, size * 8), parsedValue.RawBits);
            Assert.AreEqual(expectedName ?? candidate.ToString(CultureInfo.InvariantCulture), parsedValue.ToString());
            CollectionAssert.AreEqual(expected, cstruct.Serialize("root", parsed));
            CollectionAssert.AreEqual(
                expected,
                cstruct.Serialize(
                    "root",
                    new Dictionary<string, object> { ["value"] = candidate, }));
            if (expectedName is not null)
            {
                CollectionAssert.AreEqual(
                    expected,
                    cstruct.Serialize(
                        "root",
                        new Dictionary<string, object> { ["value"] = expectedName, }));
            }

            using var debugStream = new MemoryStream(expected);
            (List<DebugData> debug, dynamic debugWrapper) =
                cstruct.ParseStreamWithDebug(debugStream, "root");
            var debugValues = (IDictionary<string, object?>)(ExpandoObject)debugWrapper;
            dynamic debugParsed = (ExpandoObject)debugValues["root"]!;
            Assert.AreEqual(candidate, ((EnumValueResult)debugParsed.value).Value);
            Assert.AreEqual(candidate, (BigInteger)debug.Single().Value!);

            using var addressStream = new MemoryStream(expected);
            Assert.AreEqual(0L, cstruct.ResolveAddress(addressStream, "root.value"));

            using var writeStream = new MemoryStream();
            cstruct.WriteStream(
                writeStream,
                "root",
                new Dictionary<string, object> { ["value"] = candidate, });
            CollectionAssert.AreEqual(expected, writeStream.ToArray());

            using var updateStream = new MemoryStream(new byte[size]);
            cstruct.UpdateStream(updateStream, "root.value", candidate);
            CollectionAssert.AreEqual(expected, updateStream.ToArray());
        }
    }

    /// <summary>Accepts exactly the documented integral scalar spellings and follows scalar typedef chains.</summary>
    /// <param name="prefix">Optional typedef declarations placed before the enum.</param>
    /// <param name="backingType">The direct or typedef backing name used by the enum.</param>
    [TestMethod]
    [DynamicData(nameof(AcceptedBackingTypes))]
    public void BackingTypes_AcceptIntegralSpellingsAndTypedefs(string prefix, string backingType)
    {
        var cstruct = new CStruct(
            prefix + $" enum state : {backingType} {{ Known = 1 }}; struct root {{ state value; }};");
        using var stream = new MemoryStream(new byte[cstruct.GetStructSizeInBytes("root")]);

        dynamic parsed = cstruct.ParseStream(stream, "root");
        var result = (EnumValueResult)parsed.value;

        Assert.AreEqual(BigInteger.Zero, result.Value);
        Assert.AreEqual(CanonicalBacking(backingType), result.StorageType);
    }

    /// <summary>Rejects non-integral, variable-sized, aggregate, enum, pointer, and unknown backing declarations.</summary>
    [TestMethod]
    public void BackingTypes_RejectEverythingOutsideFixedIntegralScalars()
    {
        string[] layouts =
        [
            "enum state : char { Value };",
            "enum state : wchar { Value };",
            "enum state : string { Value };",
            "enum state : cstring { Value };",
            "enum state : uint16> { Value };",
            "enum state : uint16< { Value };",
            "enum state : missing { Value };",
            "struct storage { byte value; }; enum state : storage { Value };",
            "union storage { byte value; }; enum state : storage { Value };",
            "enum storage : uint8 { Value }; enum state : storage { Value };",
            "typedef uint8 *storage; enum state : storage { Value };",
            "typedef second first; typedef first second; enum state : first { Value };",
        ];

        foreach (string layout in layouts)
        {
            Assert.Throws<CStructLayoutException>(
                () => new CStruct(layout),
                "Layout should reject enum backing declaration: " + layout);
        }
    }

    /// <summary>Checks the compact descriptor's exact range, raw-bit, and natural CLR storage contracts.</summary>
    [TestMethod]
    public void IntegerCodec_ConvertsEveryDomainAndRejectsInvalidInputs()
    {
        (string Type, BigInteger Minimum, BigInteger Maximum, Type StorageType)[] domains =
        [
            ("int8", sbyte.MinValue, sbyte.MaxValue, typeof(sbyte)),
            ("uint8", byte.MinValue, byte.MaxValue, typeof(byte)),
            ("int16", short.MinValue, short.MaxValue, typeof(short)),
            ("uint16", ushort.MinValue, ushort.MaxValue, typeof(ushort)),
            ("int32", int.MinValue, int.MaxValue, typeof(int)),
            ("uint32", uint.MinValue, uint.MaxValue, typeof(uint)),
            ("int64", long.MinValue, long.MaxValue, typeof(long)),
            ("uint64", ulong.MinValue, ulong.MaxValue, typeof(ulong)),
        ];

        foreach ((string type, BigInteger minimum, BigInteger maximum, Type storageType) in domains)
        {
            Assert.IsTrue(CStruct.EnumIntegerCodec.TryCreate(type, out CStruct.EnumIntegerCodec? codec));
            Assert.IsNotNull(codec);
            Assert.AreEqual(type, codec.StorageType);
            Assert.AreEqual(type.StartsWith("int", StringComparison.Ordinal), codec.IsSigned);
            Assert.AreEqual(minimum, codec.Minimum);
            Assert.AreEqual(maximum, codec.Maximum);
            Assert.AreEqual(codec.BitWidth / 8, codec.SizeInBytes);

            foreach (BigInteger candidate in new[] { minimum, BigInteger.Zero, maximum, })
            {
                ulong rawBits = ToRawBits(candidate, codec.BitWidth);
                Assert.AreEqual(rawBits, codec.ToRawBits(candidate));
                Assert.AreEqual(candidate, codec.FromRawBits(rawBits));

                object storageValue = codec.ToStorageValue(candidate);
                Assert.AreEqual(storageType, storageValue.GetType());
                Assert.AreEqual(candidate, codec.FromStorageValue(storageValue));
            }

            Assert.IsFalse(codec.Contains(minimum - BigInteger.One));
            Assert.IsFalse(codec.Contains(maximum + BigInteger.One));
            Assert.Throws<OverflowException>(() => codec.EnsureInRange(minimum - BigInteger.One));
            Assert.Throws<OverflowException>(() => codec.ToRawBits(maximum + BigInteger.One));
            Assert.Throws<OverflowException>(() => codec.ToStorageValue(maximum + BigInteger.One));
            Assert.Throws<InvalidOperationException>(() => codec.FromStorageValue(true));
            Assert.Throws<InvalidOperationException>(() => codec.FromStorageValue(minimum - BigInteger.One));
        }

        Assert.IsFalse(CStruct.EnumIntegerCodec.TryCreate("char", out _));
        Assert.IsFalse(CStruct.EnumIntegerCodec.TryConvertIntegral(true, out _));
        Assert.IsFalse(CStruct.EnumIntegerCodec.TryConvertIntegral(1.0, out _));
    }

    /// <summary>Keeps standalone enum declarations eager and ordinary layout expressions checked to Int32.</summary>
    [TestMethod]
    public void StandaloneModel_EvaluatesImmediatelyAndKeepsInt32ProjectionChecked()
    {
        var standalone = new CstructEnum(
            new Identifier("state"),
            [
                new EnumValue(new Identifier("First"), new Literal(new BigInteger(4_294_967_296))),
                new EnumValue(new Identifier("Next"), NoneExpr.Instance),
            ],
            new Identifier("uint64"));

        AssertMember(standalone, "First", 4_294_967_296);
        AssertMember(standalone, "Next", 4_294_967_297);
        Assert.Throws<Exception>(
            () => new CstructEnum(
                new Identifier("invalid"),
                [new EnumValue(new Identifier("Value"), new Identifier("missing")),]));

        var wideLiteral = new Literal(BigInteger.One << 63);
        Assert.AreEqual(BigInteger.One << 63, wideLiteral.ExactValue);
        Assert.Throws<OverflowException>(() => _ = wideLiteral.Value);
    }

    /// <summary>Applies exact evaluator depth, work, cycle, cache, and shift limits across identifier dependencies.</summary>
    [TestMethod]
    public void ExactEvaluator_EnforcesDependencyWorkDepthAndShiftLimits()
    {
        Expr addition = new BinaryOp(BinaryOperatorType.Add, new Literal(1), new Literal(2));
        var exactBoundary = new ExpressionEvaluator(new ExpressionEvaluationLimits(3, 3));
        Assert.AreEqual(new BigInteger(3), exactBoundary.EvaluateExact(addition, null, 8));
        Assert.AreEqual(
            BigInteger.One,
            exactBoundary.EvaluateExact(
                new BinaryOp(BinaryOperatorType.ShiftLeft, new Literal(1), new Literal(0)),
                null,
                8));
        Assert.Throws<InvalidOperationException>(
            () => exactBoundary.EvaluateExact(
                new BinaryOp(BinaryOperatorType.ShiftLeft, new Literal(1), new Literal(8)),
                null,
                8));

        var depthVariables = new Dictionary<string, Expr>
        {
            ["FIRST"] = new Identifier("SECOND"),
            ["SECOND"] = new Literal(7),
        };
        Assert.AreEqual(
            new BigInteger(7),
            exactBoundary.EvaluateExact(new Identifier("FIRST"), depthVariables, 8));

        var excessiveDepthVariables = new Dictionary<string, Expr>(depthVariables)
        {
            ["SECOND"] = new Identifier("THIRD"),
            ["THIRD"] = new Literal(7),
        };
        Assert.Throws<CStructLayoutException>(
            () => exactBoundary.EvaluateExact(new Identifier("FIRST"), excessiveDepthVariables, 8));

        var workLimited = new ExpressionEvaluator(new ExpressionEvaluationLimits(8, 3));
        var repeatedVariables = new Dictionary<string, Expr> { ["VALUE"] = new Literal(2), };
        Expr repeated = new BinaryOp(
            BinaryOperatorType.Add,
            new Identifier("VALUE"),
            new Identifier("VALUE"));
        Assert.Throws<CStructLayoutException>(
            () => workLimited.EvaluateExact(repeated, repeatedVariables, 8));
        var cacheBoundary = new ExpressionEvaluator(new ExpressionEvaluationLimits(8, 4));
        Assert.AreEqual(
            new BigInteger(4),
            cacheBoundary.EvaluateExact(repeated, repeatedVariables, 8));

        var cycleVariables = new Dictionary<string, Expr>
        {
            ["FIRST"] = new Identifier("SECOND"),
            ["SECOND"] = new Identifier("FIRST"),
        };
        var cycleEvaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(8, 20));
        Assert.Throws<CStructLayoutException>(
            () => cycleEvaluator.EvaluateExact(new Identifier("FIRST"), cycleVariables, 8));
        Assert.Throws<KeyNotFoundException>(
            () => cycleEvaluator.EvaluateExact(new Identifier("MISSING"), null, 8));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => cycleEvaluator.EvaluateExact(new Literal(1), null, 0));
    }

    /// <summary>Evaluates wide literals and arithmetic without an intermediate Int32 narrowing or wrapping step.</summary>
    [TestMethod]
    public void Expressions_UseExactWidthAwareMathematics()
    {
        const string layout = """
                              #define HIGH (1 << 63)
                              #define WIDE HIGH | 0x7FFFFFFFFFFFFFFF
                              enum unsigned_values : uint64 {
                                  Zero = 0,
                                  FromDefine = WIDE,
                                  Binary = 0b1111111111111111111111111111111111111111111111111111111111111111,
                                  Octal = 0o1777777777777777777777,
                                  Decimal = 18446744073709551615,
                                  Add = 0xFFFFFFFF + 1,
                                  Subtract = 4294967297 - 1,
                                  Multiply = 0x10000 * 0x10000,
                                  Shift = 1 << 63,
                                  And = 0xFFFFFFFFFFFFFFFF & 255,
                                  Or = (1 << 63) | 1,
                                  Divide = 18446744073709551614 / 2,
                                  FromEarlier = Shift + 1
                              };
                              enum signed_values : int64 {
                                  AllBits = ~0,
                                  NegativeShift = -2 >> 1,
                                  Truncate = -5 / 2,
                                  Minimum = -9223372036854775808
                              };
                              struct root { unsigned_values wide; signed_values signed; };
                              """;
        var cstruct = new CStruct(layout);
        var unsignedValues = (CstructEnum)cstruct.CStructElements["unsigned_values"];
        var signedValues = (CstructEnum)cstruct.CStructElements["signed_values"];

        AssertMember(unsignedValues, "FromDefine", ulong.MaxValue);
        AssertMember(unsignedValues, "Binary", ulong.MaxValue);
        AssertMember(unsignedValues, "Octal", ulong.MaxValue);
        AssertMember(unsignedValues, "Decimal", ulong.MaxValue);
        AssertMember(unsignedValues, "Add", 4_294_967_296);
        AssertMember(unsignedValues, "Subtract", 4_294_967_296);
        AssertMember(unsignedValues, "Multiply", 4_294_967_296);
        AssertMember(unsignedValues, "Shift", BigInteger.One << 63);
        AssertMember(unsignedValues, "And", 255);
        AssertMember(unsignedValues, "Or", (BigInteger.One << 63) + 1);
        AssertMember(unsignedValues, "Divide", 9_223_372_036_854_775_807);
        AssertMember(unsignedValues, "FromEarlier", (BigInteger.One << 63) + 1);
        AssertMember(signedValues, "AllBits", -1);
        AssertMember(signedValues, "NegativeShift", -1);
        AssertMember(signedValues, "Truncate", -2);
        AssertMember(signedValues, "Minimum", long.MinValue);
    }

    /// <summary>Uses the compiled enum alignment and the instance byte order at an offset after a narrow field.</summary>
    /// <param name="isLittleEndian">Whether neutral enum storage uses least-significant-byte-first order.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void AlignedEnums_UseTheirValidatedStorageDescriptor(bool isLittleEndian)
    {
        var cstruct = new CStruct(
            "enum state : uint32 { Known = 0x01020304 }; " +
            "struct root { byte prefix; state value; byte tail; };",
            aligned: true,
            isLittleEndian: isLittleEndian);
        byte[] bytes = new byte[12];
        bytes[0] = 0xA5;
        Encode(0x01020304, 4, isLittleEndian).CopyTo(bytes, 4);
        bytes[8] = 0x5A;
        using var stream = new MemoryStream(bytes);

        dynamic parsed = cstruct.ParseStream(stream, "root");

        Assert.AreEqual("Known", ((EnumValueResult)parsed.value).Name);
        Assert.AreEqual((byte)0x5A, (byte)parsed.tail);
        stream.Position = 0;
        Assert.AreEqual(4L, cstruct.ResolveAddress(stream, "root.value"));
        CollectionAssert.AreEqual(bytes, cstruct.Serialize("root", parsed));
    }

    /// <summary>Rejects explicit values, implicit increments, and shifts outside the declared backing domain.</summary>
    [TestMethod]
    public void Expressions_RejectDomainOverflowAndInvalidShiftsDuringCompilation()
    {
        string[] invalidLayouts =
        [
            "enum state : uint8 { Invalid = -1 };",
            "enum state : uint8 { Invalid = 256 };",
            "enum state : int8 { Invalid = -129 };",
            "enum state : int8 { Invalid = 128 };",
            "enum state : uint16 { Invalid = 65536 };",
            "enum state : uint16 { Invalid = -1 };",
            "enum state : int16 { Invalid = -32769 };",
            "enum state : int16 { Invalid = 32768 };",
            "enum state : uint32 { Invalid = 4294967296 };",
            "enum state : uint32 { Invalid = -1 };",
            "enum state : int32 { Invalid = -2147483649 };",
            "enum state : int32 { Invalid = 2147483648 };",
            "enum state : uint64 { Invalid = 18446744073709551616 };",
            "enum state : uint64 { Invalid = -1 };",
            "enum state : int64 { Invalid = -9223372036854775809 };",
            "enum state : int64 { Invalid = 9223372036854775808 };",
            "enum state : uint8 { Maximum = 255, Overflow };",
            "enum state : int8 { Maximum = 127, Overflow };",
            "enum state : uint16 { Maximum = 65535, Overflow };",
            "enum state : int16 { Maximum = 32767, Overflow };",
            "enum state : uint32 { Maximum = 4294967295, Overflow };",
            "enum state : int32 { Maximum = 2147483647, Overflow };",
            "enum state : uint64 { Maximum = 18446744073709551615, Overflow };",
            "enum state : int64 { Maximum = 9223372036854775807, Overflow };",
            "enum state : uint64 { Invalid = ~0 };",
            "enum state : uint64 { Invalid = 1 << 64 };",
            "enum state : int8 { Invalid = 1 << -1 };",
            "enum state : uint8 { Invalid = 1 / 0 };",
        ];

        foreach (string layout in invalidLayouts)
        {
            Assert.Throws<CStructLayoutException>(
                () => new CStruct(layout),
                "Layout should reject enum expression: " + layout);
        }

        Assert.Throws<CStructLayoutException>(
            () => new CStruct("#define WIDE 1 << 63\nstruct root { byte values[WIDE]; };"));
        Assert.Throws<CStructLayoutException>(
            () => new CStruct("#define UNUSED 1 << 63\nstruct root { byte value; };"));
    }

    /// <summary>Uses the same enum descriptor for direct roots, arrays, nested structs, pointers, and union members.</summary>
    /// <param name="isLittleEndian">Whether neutral enum storage uses least-significant-byte-first order.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void Shapes_UseOneExactEnumCodec(bool isLittleEndian)
    {
        BigInteger maximum = ulong.MaxValue;
        byte[] encoded = Encode(maximum, 8, isLittleEndian);

        var rootEnum = new CStruct(
            "enum state : uint64 { Known = 1 };",
            pointerSize: 1,
            isLittleEndian: isLittleEndian);
        using (var rootStream = new MemoryStream(encoded))
        {
            var parsed = (EnumValueResult)rootEnum.ParseStream(rootStream, "state");
            Assert.AreEqual(maximum, parsed.Value);
            CollectionAssert.AreEqual(encoded, rootEnum.Serialize("state", parsed));
        }

        const string compositeLayout = """
                                       enum state : uint64 { Known = 1 };
                                       struct nested { state value; };
                                       struct root { state values[2]; nested child; state *target; };
                                       """;
        var composite = new CStruct(
            compositeLayout,
            pointerSize: 1,
            isLittleEndian: isLittleEndian);
        byte[] compositeBytes = new byte[33];
        encoded.CopyTo(compositeBytes, 0);
        Encode(BigInteger.One, 8, isLittleEndian).CopyTo(compositeBytes, 8);
        encoded.CopyTo(compositeBytes, 16);
        compositeBytes[24] = 25;
        Encode(BigInteger.One, 8, isLittleEndian).CopyTo(compositeBytes, 25);
        using (var stream = new MemoryStream(compositeBytes))
        {
            dynamic parsed = composite.ParseStream(stream, "root");
            Assert.AreEqual(maximum, ((EnumValueResult)((IList<object>)parsed.values)[0]).Value);
            Assert.AreEqual("Known", ((EnumValueResult)((IList<object>)parsed.values)[1]).Name);
            Assert.AreEqual(maximum, ((EnumValueResult)parsed.child.value).Value);
            Assert.AreEqual(
                BigInteger.One,
                ((EnumValueResult)((Pointer)parsed.target).Value!).Value);

            stream.Position = 0;
            composite.UpdateStream(stream, "root.target.value", maximum);
            CollectionAssert.AreEqual(encoded, stream.ToArray()[25..]);
        }

        var union = new CStruct(
            "enum state : uint64 { Known = 1 }; union choice { state value; uint64 raw; };",
            pointerSize: 1,
            isLittleEndian: isLittleEndian);
        using (var stream = new MemoryStream(encoded))
        {
            var parsed = (UnionValue)union.ParseStream(stream, "choice");
            Assert.AreEqual(maximum, ((EnumValueResult)parsed.Members["value"]!).Value);
            CollectionAssert.AreEqual(encoded, union.Serialize("choice", parsed));
            CollectionAssert.AreEqual(
                Encode(BigInteger.One, 8, isLittleEndian),
                union.Serialize("choice", UnionValue.FromMember("choice", "value", "Known")));
        }
    }

    /// <summary>Publishes an in-range enum scalar to later array expressions for read, path, and write operations.</summary>
    [TestMethod]
    public void ScalarEnum_CanDriveALaterArrayLength()
    {
        const string layout = """
                              enum count_type : uint8 { Two = 2 };
                              struct root { count_type count; byte values[count]; byte tail; };
                              """;
        var cstruct = new CStruct(layout);
        byte[] bytes = [2, 0xA5, 0x5A, 0x7E,];
        using var stream = new MemoryStream(bytes);

        dynamic parsed = cstruct.ParseStream(stream, "root");

        Assert.AreEqual("Two", ((EnumValueResult)parsed.count).Name);
        CollectionAssert.AreEqual(
            new byte[] { 0xA5, 0x5A, },
            ((IList<object>)parsed.values).Cast<byte>().ToArray());
        stream.Position = 0;
        Assert.AreEqual(3L, cstruct.ResolveAddress(stream, "root.tail"));
        CollectionAssert.AreEqual(bytes, cstruct.Serialize("root", parsed));
        CollectionAssert.AreEqual(
            bytes,
            cstruct.Serialize(
                "root",
                new Dictionary<string, object>
                {
                    ["count"] = "Two",
                    ["values"] = new byte[] { 0xA5, 0x5A, },
                    ["tail"] = (byte)0x7E,
                }));
    }

    /// <summary>Does not reuse a stale caller value when a wide enum scalar cannot enter the Int32 expression domain.</summary>
    [TestMethod]
    public void WideEnumScalar_ShadowsStaleExpressionVariables()
    {
        const string layout = """
                              enum count_type : uint64 { Maximum = 18446744073709551615 };
                              struct root { count_type count; byte values[count]; };
                              """;
        var cstruct = new CStruct(layout);
        byte[] bytes = [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xA5,];
        var variables = new Dictionary<string, Expr> { ["count"] = new Literal(1), };

        Assert.Throws<CStructLayoutException>(
            () => cstruct.ParseStream(new MemoryStream(bytes), "root", variables));
        Assert.Throws<CStructLayoutException>(
            () => cstruct.ResolveAddress(new MemoryStream(bytes), "root.values[0]", variables));
        Assert.Throws<CStructLayoutException>(
            () => cstruct.Serialize(
                "root",
                new Dictionary<string, object>
                {
                    ["count"] = ulong.MaxValue,
                    ["values"] = new byte[] { 0xA5, },
                },
                variables));
    }

    /// <summary>Accepts convenient exact inputs while rejecting coercive, contradictory, or out-of-domain values.</summary>
    [TestMethod]
    public void Writer_AcceptsIntegralAndStructuredInputsButRejectsCoercion()
    {
        const string layout = "enum state : uint64 { Known = 42 }; struct root { state value; };";
        var cstruct = new CStruct(layout);
        object[] accepted =
        [
            (sbyte)42,
            (byte)42,
            (short)42,
            (ushort)42,
            42,
            42U,
            42L,
            42UL,
            new BigInteger(42),
            "42",
            "Known",
            new Dictionary<string, object?> { ["Enum"] = "state", ["Name"] = "Known", },
            new Dictionary<string, object?> { ["Value"] = "42", },
            new EnumInputProperty { Enum = "state", Name = "Known", Value = 42UL, },
            new EnumInputField { Enum = "state", Name = "Known", Value = 42UL, },
        ];

        foreach (object value in accepted)
        {
            CollectionAssert.AreEqual(
                new byte[] { 42, 0, 0, 0, 0, 0, 0, 0, },
                cstruct.Serialize(
                    "root",
                    new Dictionary<string, object> { ["value"] = value, }),
                "Accepted value: " + value.GetType().Name);
        }

        dynamic expando = new ExpandoObject();
        expando.Enum = "state";
        expando.Name = "Known";
        expando.Value = "42";
        CollectionAssert.AreEqual(
            new byte[] { 42, 0, 0, 0, 0, 0, 0, 0, },
            cstruct.Serialize(
                "root",
                new Dictionary<string, object> { ["value"] = expando, }));

        object?[] rejected =
        [
            null,
            true,
            42F,
            42D,
            42M,
            -1,
            BigInteger.One << 64,
            "42.0",
            "Missing",
            new Dictionary<string, object?>(),
            new Dictionary<string, object?> { ["Enum"] = "other", ["Value"] = 42, },
            new Dictionary<string, object?> { ["Name"] = "Known", ["Value"] = 41, },
            new Dictionary<string, object?> { ["Name"] = "Missing", },
            new Dictionary<string, object?> { ["Value"] = null, },
        ];

        foreach (object? value in rejected)
        {
            Assert.Throws<CStructWriteException>(
                () => cstruct.Serialize(
                    "root",
                    new Dictionary<string, object?> { ["value"] = value, }),
                "Rejected value: " + (value?.GetType().Name ?? "null"));
        }
    }

    /// <summary>Fails before committing bytes or changing the caller's stream position for invalid enum updates.</summary>
    [TestMethod]
    public void Writer_InvalidUpdateIsTransactional()
    {
        var cstruct = new CStruct(
            "enum state : uint16 { Known = 1 }; struct root { byte prefix; state value; byte tail; };");
        byte[] original = [0xA5, 0x01, 0x00, 0x5A,];
        using var stream = new MemoryStream((byte[])original.Clone()) { Position = 2, };

        Assert.Throws<CStructWriteException>(
            () => cstruct.UpdateStream(stream, "root.value", 65_536));

        CollectionAssert.AreEqual(original, stream.ToArray());
        Assert.AreEqual(2L, stream.Position);
    }

    /// <summary>Rejects a parsed result from a same-named enum whose signedness or width differs.</summary>
    [TestMethod]
    public void Writer_ValidatesParsedResultDomainMetadata()
    {
        var unsignedLayout = new CStruct(
            "enum state : uint16 { Known = 1 }; struct root { state value; };");
        var signedLayout = new CStruct(
            "enum state : int16 { Known = 1 }; struct root { state value; };");
        using var stream = new MemoryStream([1, 0,]);
        dynamic parsed = unsignedLayout.ParseStream(stream, "root");

        Assert.Throws<CStructWriteException>(() => signedLayout.Serialize("root", parsed));

        EnumValueResult[] invalidResults =
        [
            new("state", "Known", 1, 1, "int16", 16, false),
            new("state", "Known", 1, 1, "uint16", 8, false),
            new("state", "Known", 1, 1, "uint16", 16, true),
            new("state", "Known", 1, 2, "uint16", 16, false),
            new("other", "Known", 1, 1, "uint16", 16, false),
            new("state", "Known", 2, 2, "uint16", 16, false),
        ];
        foreach (EnumValueResult invalid in invalidResults)
        {
            Assert.Throws<CStructWriteException>(
                () => unsignedLayout.Serialize(
                    "root",
                    new Dictionary<string, object> { ["value"] = invalid, }));
        }
    }

    /// <summary>Returns the first declared symbolic name for aliases and keeps unknown values exact.</summary>
    [TestMethod]
    public void Results_UseFirstDeclaredNameAndExactUnknownText()
    {
        var cstruct = new CStruct(
            "enum state : uint64 { First = 7, Alias = 7 }; struct root { state value; };");
        using var knownStream = new MemoryStream(Encode(7, 8, true));
        using var unknownStream = new MemoryStream(Encode(ulong.MaxValue, 8, true));

        dynamic known = cstruct.ParseStream(knownStream, "root");
        dynamic unknown = cstruct.ParseStream(unknownStream, "root");

        Assert.AreEqual("First", ((EnumValueResult)known.value).Name);
        Assert.AreEqual("First", ((EnumValueResult)known.value).ToString());
        Assert.IsNull(((EnumValueResult)unknown.value).Name);
        Assert.AreEqual(ulong.MaxValue.ToString(CultureInfo.InvariantCulture), ((EnumValueResult)unknown.value).ToString());
    }

    private static void AssertMember(CstructEnum enm, string name, BigInteger expected)
    {
        EnumValue member = enm.Values.Single(value => value.Name.Name == name);
        Assert.AreEqual(expected, ((Literal)member.Value).ExactValue, enm.Name.Name + "." + name);
    }

    private static string CanonicalBacking(string backingType)
    {
        return backingType switch
        {
            "byte" => "uint8",
            "ushort" => "uint16",
            "short" => "int16",
            "uint" => "uint32",
            "int" => "int32",
            "ulong" or "storage" => "uint64",
            "long" => "int64",
            _ => backingType,
        };
    }

    private static byte[] Encode(BigInteger value, int size, bool isLittleEndian)
    {
        int bitWidth = checked(size * 8);
        BigInteger unsigned = value < BigInteger.Zero ? (BigInteger.One << bitWidth) + value : value;
        var bytes = new byte[size];
        for (int index = 0; index < size; index++)
        {
            int destination = isLittleEndian ? index : size - index - 1;
            bytes[destination] = (byte)(unsigned & byte.MaxValue);
            unsigned >>= 8;
        }

        return bytes;
    }

    private static ulong ToRawBits(BigInteger value, int bitWidth)
    {
        BigInteger raw = value < BigInteger.Zero ? (BigInteger.One << bitWidth) + value : value;
        return checked((ulong)raw);
    }

    private sealed class EnumInputProperty
    {
        public string? Enum { get; init; }

        public string? Name { get; init; }

        public ulong Value { get; init; }
    }

    private sealed class EnumInputField
    {
#pragma warning disable SA1401 // Public test fixture fields intentionally exercise field binding.
        public string? Enum;
        public string? Name;
        public ulong Value;
#pragma warning restore SA1401
    }
}
