namespace CStructSharp.Tests;

using System.Collections;
using System.Collections.Immutable;
using System.Numerics;
using CStructSharp.Diagnostics;
using CStructSharp.Introspection;
using CStructSharp.Syntax;
using CStructSharp.Values;

/// <summary>Compatibility assertions for core value contracts reused when projecting and editing memory layouts.</summary>
[TestClass]
public class MemoryCoreCompatibilityTests
{
    /// <summary>Published constants keep their kind and escaped value when rendered into another layout.</summary>
    [TestMethod]
    public void Constants_RenderEscapesAndPreserveIndependentBytes()
    {
        var layout = new CStruct("#define TEXT \"a\\\\\\\"\\n\\r\\t\\x01\\x7f\"\n#define BYTES b\"A\\x00\"\n#define EMPTY\n#define MAC(x) x+1\n#define N 2\nstruct Root { uint8 value; };");
        foreach (LayoutConstant value in layout.Constants.Values)
        {
            var roundTrip = new CStruct(value + "\nstruct Root { uint8 value; };");
            LayoutConstant copy = roundTrip.Constants[value.Name];
            Assert.AreEqual(value.Kind, copy.Kind);
            if (value.Value is byte[] bytes)
            {
                CollectionAssert.AreEqual(bytes, (byte[])copy.Value!);
                bytes[0] = 99;
                Assert.AreEqual((byte)'A', ((byte[])value.Value!)[0]);
            }
            else
            {
                Assert.AreEqual(value.Value, copy.Value);
            }
        }

        var expression = new LayoutConstant("external", LayoutConstantKind.Expression, null);
        Assert.AreEqual("#define external (expression)", expression.ToString());
    }

    /// <summary>Header constant/include identities distinguish kind and content while equal definitions remain hash compatible.</summary>
    [TestMethod]
    public void HeaderElements_KeepValueEqualityContracts()
    {
        var bytes = new ConstantDefinition(new Identifier("B"), LayoutConstantKind.Bytes, new byte[] { 1, 2, });
        var equal = new ConstantDefinition(new Identifier("B"), LayoutConstantKind.Bytes, new byte[] { 1, 2, });
        Assert.IsTrue(bytes.Equals(equal));
        Assert.AreEqual(bytes.GetHashCode(), equal.GetHashCode());
        Assert.IsFalse(bytes.Equals(null));
        Assert.IsFalse(bytes.Equals(new ConstantDefinition(new Identifier("X"), LayoutConstantKind.Bytes, new byte[] { 1, 2, })));
        Assert.IsFalse(bytes.Equals(new ConstantDefinition(new Identifier("B"), LayoutConstantKind.Text, "ab")));
        Assert.IsFalse(bytes.Equals(new ConstantDefinition(new Identifier("B"), LayoutConstantKind.Bytes, new byte[] { 2, 1, })));
        Assert.IsFalse(bytes.Equals(new ConstantDefinition(new Identifier("B"), LayoutConstantKind.Bytes, "ab")));
        var text = new ConstantDefinition(new Identifier("T"), LayoutConstantKind.Text, "hello");
        Assert.IsTrue(text.Equals(new ConstantDefinition(new Identifier("T"), LayoutConstantKind.Text, "hello")));
        Assert.IsFalse(text.Equals(new ConstantDefinition(new Identifier("T"), LayoutConstantKind.Text, "other")));
        StringAssert.Contains(text.ToString(), "hello");

        var include = new IncludeDirective("types.h", true);
        var same = new IncludeDirective("types.h", true);
        Assert.IsTrue(include.Equals(same));
        Assert.AreEqual(include.GetHashCode(), same.GetHashCode());
        Assert.IsFalse(include.Equals(null));
        Assert.IsFalse(include.Equals(new IncludeDirective("other.h", true)));
        Assert.IsFalse(include.Equals(new IncludeDirective("types.h", false)));
        StringAssert.Contains(include.ToString(), "<types.h>");
        StringAssert.Contains(new IncludeDirective("types.h", false).ToString(), "\"types.h\"");
    }

    /// <summary>Conditional expression and inline typedef identities preserve branch and composite distinctions.</summary>
    [TestMethod]
    public void ExpressionsAndAliases_KeepStructuralIdentity()
    {
        var expression = new ConditionalExpr(new Literal(1), new Literal(7), new Literal(9));
        var same = new ConditionalExpr(new Literal(1), new Literal(7), new Literal(9));
        Assert.AreEqual(7, expression.Value);
        Assert.IsTrue(expression.Equals(same));
        Assert.AreEqual(expression.GetHashCode(), same.GetHashCode());
        Assert.IsFalse(expression.Equals(new Literal(7)));
        Assert.IsFalse(expression.Equals(new ConditionalExpr(new Literal(0), new Literal(7), new Literal(9))));
        Assert.IsFalse(expression.Equals(new ConditionalExpr(new Literal(1), new Literal(8), new Literal(9))));
        Assert.IsFalse(expression.Equals(new ConditionalExpr(new Literal(1), new Literal(7), new Literal(8))));
        StringAssert.Contains(expression.ToString(), "7");
        var composite = new Struct(new Identifier("S"), ImmutableList<Field>.Empty, false);
        var alias = new Typedef(new Identifier("Alias"), composite);
        var copy = new Typedef(new Identifier("Alias"), new Struct(new Identifier("S"), ImmutableList<Field>.Empty, false));
        Assert.IsTrue(alias.Equals(copy));
        Assert.AreEqual(alias.GetHashCode(), copy.GetHashCode());
        Assert.IsFalse(alias.Equals(new Typedef(new Identifier("Alias"), new Identifier("struct"))));
        Assert.IsFalse(new Typedef(new Identifier("Alias"), new Identifier("struct")).Equals(alias));
        Assert.IsFalse(alias.Equals(new Typedef(new Identifier("Alias"), new Struct(new Identifier("S"), ImmutableList<Field>.Empty, true))));
        Assert.IsFalse(alias.Equals(null));
        Assert.IsFalse(alias.Equals(new Typedef(new Identifier("Other"), composite)));
        Assert.IsFalse(alias.Equals(new Typedef(new Identifier("Alias"), new Identifier("uint8"))));
        Assert.IsFalse(alias.Equals(new Typedef(new Identifier("Alias"), composite) { ArrayShape = new Expr[] { new Literal(2), }, }));
        StringAssert.Contains(alias.ToString(), "Alias");
    }

    /// <summary>Primitive arrays support both list views with shared indexed storage and explicit fixed-size failures.</summary>
    [TestMethod]
    public void PrimitiveArrays_KeepCollectionAndConversionBehavior()
    {
        var values = new PrimitiveArray<ushort>(new ushort[] { 1, 2, });
        IList plain = values;
        IList<object?> generic = values;
        Assert.IsTrue(plain.IsReadOnly && plain.IsFixedSize);
        Assert.IsFalse(((ICollection)values).IsSynchronized);
        Assert.IsNotNull(((ICollection)values).SyncRoot);
        plain[0] = "7";
        Assert.AreEqual((ushort)7, plain[0]);
        Assert.AreEqual((ushort)7, values.Memory.Span[0]);
        Assert.IsTrue(plain.Contains(7));
        Assert.AreEqual(1, plain.IndexOf(2));
        Assert.AreEqual(-1, values.IndexOf(null));
        Assert.AreEqual(-1, values.IndexOf(new object()));
        Assert.AreEqual(-1, values.IndexOf(ulong.MaxValue));
        Assert.AreEqual(-1, values.IndexOf(DateTime.UnixEpoch));
        Assert.Throws<ArgumentNullException>(() => values[0] = null);
        object[] copy = new object[3];
        ((ICollection)values).CopyTo(copy, 1);
        CollectionAssert.AreEqual(new object?[] { null, (ushort)7, (ushort)2, }, copy);
        CollectionAssert.AreEqual(new object[] { (ushort)7, (ushort)2, }, ((IEnumerable)values).Cast<object>().ToArray());
        Assert.Throws<NotSupportedException>(() => generic.Insert(0, 1));
        Assert.Throws<NotSupportedException>(() => generic.Clear());
        Assert.Throws<NotSupportedException>(() => generic.Remove(1));
        Assert.Throws<NotSupportedException>(() => plain.Add(1));
        Assert.Throws<NotSupportedException>(() => plain.Insert(0, 1));
        Assert.Throws<NotSupportedException>(() => plain.Remove(1));
        Assert.Throws<NotSupportedException>(() => plain.RemoveAt(0));
        Assert.Throws<NotSupportedException>(() => plain.Clear());
    }

    /// <summary>Core wide scalar codecs accept exact integral/string inputs and reject overflow or unsupported objects.</summary>
    [TestMethod]
    public void WideValues_KeepCheckedConversions()
    {
        var layout = new CStruct("struct Root { uint8 value; };");
        foreach (object value in new object[] { (Int128)7, (UInt128)7, new BigInteger(7), "7", (short)7, })
        {
            Assert.AreEqual((Int128)7, layout.ReadValue<Int128>(layout.Serialize("int128", value).AsSpan(), "int128"));
            Assert.AreEqual((UInt128)7, layout.ReadValue<UInt128>(layout.Serialize("uint128", value).AsSpan(), "uint128"));
        }

        foreach (object value in new object[] { (Half)1.5, 1.5F, 1.5D, "1.5", 2, })
        {
            byte[] encoded = layout.Serialize("float16", value);
            Assert.IsTrue(layout.ReadValue<Half>(encoded.AsSpan(), "float16") > (Half)1);
        }

        Assert.Throws<CStructWriteException>(() => layout.Serialize("int128", new object()));
        Assert.Throws<CStructWriteException>(() => layout.Serialize("uint128", new object()));
        Assert.Throws<CStructWriteException>(() => layout.Serialize("uint128", (Int128)(-1)));
        Assert.Throws<CStructWriteException>(() => layout.Serialize("int128", UInt128.MaxValue));
    }

    /// <summary>Typed array plans and bulk stream reads agree on less common integer and floating-point element types.</summary>
    [TestMethod]
    public void TypedArrays_ReuseBulkCodecs()
    {
        var layout = new CStruct("struct Root { int8 Signed[2]; bool Flags[2]; uint16 Words[2]; int64 Wide[2]; float32 Single[2]; float64 Double[2]; uint24 Packed[2]; };");
        byte[] bytes = new byte[layout.GetStructSizeInBytes("Root")];
        var value = layout.ReadValue<ArrayRecord>(bytes.AsSpan(), "Root");
        Assert.AreEqual(2, value!.Signed.Length);
        Assert.AreEqual(2, value.Flags.Length);
        Assert.AreEqual(2, value.Words.Length);
        Assert.AreEqual(2, value.Wide.Length);
        Assert.AreEqual(2, value.Single.Length);
        Assert.AreEqual(2, value.Double.Length);
        Assert.AreEqual(2, value.Packed.Length);
        using var stream = new MemoryStream(bytes);
        dynamic parsed = layout.Parse(stream, "Root");
        Assert.AreEqual(2, ((IList<object?>)parsed.Packed).Count);
    }

    /// <summary>Typed consumer shape exercises exact array plans without custom conversion code.</summary>
    public sealed class ArrayRecord
    {
        public sbyte[] Signed { get; set; } = [];

        public bool[] Flags { get; set; } = [];

        public ushort[] Words { get; set; } = [];

        public long[] Wide { get; set; } = [];

        public float[] Single { get; set; } = [];

        public double[] Double { get; set; } = [];

        public uint[] Packed { get; set; } = [];
    }
}
