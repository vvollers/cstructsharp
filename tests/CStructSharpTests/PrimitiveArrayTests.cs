namespace CStructSharp.Tests;

using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>Pins the typed parsed-array contract (E2.3 stage 2): a <see cref="PrimitiveArray{T}"/> is the documented <c>IList&lt;object?&gt;</c> plus a typed span.</summary>
[TestClass]
public class PrimitiveArrayTests
{
    /// <summary>Every fixed-width numeric element type parses to its typed array with the same boxed element types as before, in both byte orders.</summary>
    [TestMethod]
    [DataRow("uint8", typeof(byte))]
    [DataRow("int8", typeof(sbyte))]
    [DataRow("bool", typeof(bool))]
    [DataRow("uint16", typeof(ushort))]
    [DataRow("int16", typeof(short))]
    [DataRow("uint24", typeof(uint))]
    [DataRow("int24", typeof(int))]
    [DataRow("uint32", typeof(uint))]
    [DataRow("int32", typeof(int))]
    [DataRow("uint64", typeof(ulong))]
    [DataRow("int64", typeof(long))]
    [DataRow("float32", typeof(float))]
    [DataRow("float64", typeof(double))]
    public void NumericArrays_ParseToTypedArrays_InBothByteOrders(string type, Type elementType)
    {
        byte[] bytes = new byte[8 * 70_003];
        new Random(type.Length).NextBytes(bytes);
        foreach (string suffix in elementType == typeof(byte) || elementType == typeof(sbyte) || elementType == typeof(bool) ? new[] { string.Empty } : new[] { "<", ">" })
        {
            var layout = new CStruct($"struct root {{ {type}{suffix} values[70003]; }};");
            IDictionary<string, object?> parsed = layout.Parse(bytes, "root");
            object? values = parsed["values"];
            Assert.AreEqual(typeof(PrimitiveArray<>).MakeGenericType(elementType), values!.GetType(), type + suffix);
            var list = (IList<object?>)values;
            Assert.AreEqual(70_003, list.Count);
            Assert.AreEqual(elementType, list[0]!.GetType());

            // The per-element path (debug parse) is the reference for every value.
            (List<DebugData> _, dynamic reference) = layout.ParseStreamWithDebug(new MemoryStream(bytes, writable: false), "root");
            CollectionAssert.AreEqual(((IList<object?>)reference.root.values).ToArray(), list.ToArray(), type + suffix);
        }
    }

    /// <summary>The typed view, indexer replacement, fixed-size semantics and both list interfaces behave like a .NET array.</summary>
    [TestMethod]
    public void PrimitiveArray_ExposesSpan_AndBehavesLikeAFixedSizeArray()
    {
        var layout = new CStruct("struct root { uint16 values[4]; };");
        dynamic parsed = layout.Parse([1, 0, 2, 0, 3, 0, 4, 0], "root");
        var values = (PrimitiveArray<ushort>)parsed.values;
        CollectionAssert.AreEqual(new ushort[] { 1, 2, 3, 4 }, values.Span.ToArray());
        CollectionAssert.AreEqual(new ushort[] { 1, 2, 3, 4 }, values.ToArray());
        Assert.AreEqual(4, values.Count);
        Assert.AreEqual((ushort)3, values[2]);
        Assert.AreEqual(2, values.IndexOf((ushort)3));
        Assert.AreEqual(2, values.IndexOf(3));
        Assert.IsTrue(values.Contains(4L));
        Assert.IsFalse(values.Contains("x"));
        Assert.AreEqual("UInt16[4]", values.ToString());

        IList<object?> list = values;
        list[0] = 9;
        Assert.AreEqual((ushort)9, values.Span[0]);
        Assert.ThrowsExactly<OverflowException>(() => list[0] = 70_000);
        Assert.ThrowsExactly<NotSupportedException>(() => list.Add(1));
        Assert.ThrowsExactly<NotSupportedException>(() => list.RemoveAt(0));
        Assert.IsTrue(list.IsReadOnly);

        IList plain = values;
        Assert.IsTrue(plain.IsFixedSize);
        Assert.AreEqual((ushort)9, plain[0]);
        object?[] copy = new object?[4];
        list.CopyTo(copy, 0);
        CollectionAssert.AreEqual(new object[] { (ushort)9, (ushort)2, (ushort)3, (ushort)4 }, copy);

        // A typed array writes back through Serialize like any other array value.
        byte[] written = layout.Serialize("root", (object)parsed);
        CollectionAssert.AreEqual(new byte[] { 9, 0, 2, 0, 3, 0, 4, 0 }, written);
    }

    /// <summary>Arrays that are not plain one-dimensional numeric arrays keep their previous list shape.</summary>
    [TestMethod]
    public void OtherArrays_KeepTheirListShape()
    {
        var layout = new CStruct("struct leaf { uint8 v; }; struct root { uint8 grid[2][2]; char name[4]; leaf items[2]; uint8 last; };");
        IDictionary<string, object?> parsed = layout.Parse([1, 2, 3, 4, (byte)'a', (byte)'b', 0, 0, 7, 8, 9], "root");
        Assert.IsInstanceOfType<List<object?>>(parsed["grid"]);
        Assert.IsInstanceOfType<string>(parsed["name"]);
        Assert.IsInstanceOfType<List<object?>>(parsed["items"]);
    }

    /// <summary>A read-budget failure inside a typed array reports the same position as the boxed bulk reader (per 64 KiB block), for memory and stream sources alike.</summary>
    [TestMethod]
    public void ReadBudget_FailsAtTheSameBlockBoundary()
    {
        var typed = new CStruct("struct root { uint32 values[40000]; };");
        var boxed = new CStruct("struct root { uint32 values[200][200]; };");
        byte[] bytes = new byte[160_000];
        var options = new ReadOptions { MaxTotalBytesRead = 100_000 };
        foreach ((string name, Func<CStruct, CStructReadLimitException> parse) in new (string, Func<CStruct, CStructReadLimitException>)[]
        {
            ("span", layout => Assert.Throws<CStructReadLimitException>(() => layout.Parse(bytes, "root", options: options))),
            ("memory stream", layout => Assert.Throws<CStructReadLimitException>(() => layout.ParseStream(new MemoryStream(bytes, writable: false), "root", options: options))),
            ("chunked stream", layout => Assert.Throws<CStructReadLimitException>(() => layout.ParseStream(new ChunkedMemoryStream(bytes, 7, false), "root", options: options))),
        })
        {
            Assert.AreEqual(parse(boxed).Offset, parse(typed).Offset, name);
        }
    }
}
