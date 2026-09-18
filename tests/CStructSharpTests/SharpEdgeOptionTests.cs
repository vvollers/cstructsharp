namespace CStructSharp.Tests;

using System.Collections.Generic;
using System.Text;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     The opt-in policies for two documented sharp edges: NUL padding in fixed text
///     (<see cref="ReadOptions.TrimFixedText"/>) and silently ignored extra members on write
///     (<see cref="WriteOptions.UnknownMembers"/>). Both default to the historical behaviour.
/// </summary>
[TestClass]
public class SharpEdgeOptionTests
{
    private static readonly ReadOptions Trim = new() { TrimFixedText = true, };

    /// <summary>By default a char[N] keeps its padding; with TrimFixedText only the trailing NULs go.</summary>
    [TestMethod]
    public void TrimFixedText_CharArray_DropsTrailingNulsOnly()
    {
        var layout = new CStruct("struct root { char name[6]; uint8 tail; };");
        byte[] bytes = [(byte)'a', (byte)'b', 0, (byte)'c', 0, 0, 7];

        Assert.AreEqual("ab\0c\0\0", layout.Parse(bytes, "root").Get<string>("name"));
        StructValue trimmed = layout.Parse(bytes, "root", options: Trim);
        Assert.AreEqual("ab\0c", trimmed.Get<string>("name"));
        Assert.AreEqual((byte)7, trimmed.Get<byte>("tail"));
        Assert.AreEqual("ab\0c", layout.ReadValue(bytes, "root.name", options: Trim));
        Assert.AreEqual("ab\0c", layout.ReadValue<Root>(bytes, "root", options: Trim).Name);
        Assert.AreEqual("ab\0c", layout.ParseWithDebug(bytes, "root", options: Trim).Value.Get<string>("name"));
    }

    /// <summary>Wide characters, bounded encoded buffers, and string tables trim the same way; embedded NULs stay.</summary>
    [TestMethod]
    public void TrimFixedText_WideAndBoundedAndTables()
    {
        var wide = new CStruct("struct root { wchar name[3]; };");
        byte[] wideBytes = Encoding.Unicode.GetBytes("é\0\0");
        Assert.AreEqual("é\0\0", wide.Parse(wideBytes, "root").Get<string>("name"));
        Assert.AreEqual("é", wide.Parse(wideBytes, "root", options: Trim).Get<string>("name"));

        var bounded = new CStruct("struct root { utf8 name[5]; uint8 tail; };");
        byte[] boundedBytes = [0xC3, 0xA9, 0, 0, 0, 9];
        Assert.AreEqual("é\0\0\0", bounded.Parse(boundedBytes, "root").Get<string>("name"));
        Assert.AreEqual("é", bounded.Parse(boundedBytes, "root", options: Trim).Get<string>("name"));

        var table = new CStruct("struct root { char names[2][3]; };");
        byte[] tableBytes = [(byte)'a', 0, 0, (byte)'b', (byte)'c', 0];
        var rows = (IList<object?>)table.Parse(tableBytes, "root", options: Trim)["names"]!;
        Assert.AreEqual("a", rows[0]);
        Assert.AreEqual("bc", rows[1]);

        // An all-NUL buffer trims to the empty string, and a full buffer is untouched.
        var full = new CStruct("struct root { char a[2]; char b[2]; };");
        StructValue parsed = full.Parse([0, 0, (byte)'x', (byte)'y'], "root", options: Trim);
        Assert.AreEqual(string.Empty, parsed.Get<string>("a"));
        Assert.AreEqual("xy", parsed.Get<string>("b"));
    }

    /// <summary>Trimming is a read choice only: a trimmed value written back is zero-padded to the declared capacity again.</summary>
    [TestMethod]
    public void TrimFixedText_RoundTripsThroughSerialize()
    {
        var layout = new CStruct("struct root { char name[4]; uint8 tail; };");
        byte[] bytes = [(byte)'a', (byte)'b', 0, 0, 7];
        StructValue parsed = layout.Parse(bytes, "root", options: Trim);
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", parsed));
    }

    /// <summary>The default ignores extra members; Reject names the member and the declared ones, and writes nothing.</summary>
    [TestMethod]
    public void UnknownMembers_Reject_FailsBeforeWriting()
    {
        var layout = new CStruct("struct root { uint16 kind; uint8 tail; };");
        var value = new Dictionary<string, object?> { ["kind"] = 1, ["tail"] = 2, ["bogus"] = 3, };
        CollectionAssert.AreEqual(new byte[] { 1, 0, 2 }, layout.Serialize("root", value));

        var reject = new WriteOptions { UnknownMembers = UnknownMemberPolicy.Reject, };
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => layout.Serialize("root", value, options: reject));
        StringAssert.Contains(failure.Message, "'bogus' is not a member of 'root'");
        StringAssert.Contains(failure.Message, "kind, tail");

        using var stream = new MemoryStream();
        Assert.Throws<CStructWriteException>(() => layout.Write(stream, "root", value, options: reject));
        Assert.AreEqual(0L, stream.Length);

        var updateReject = new UpdateOptions { UnknownMembers = UnknownMemberPolicy.Reject, };
        using var existing = new MemoryStream([9, 9, 9]);
        Assert.Throws<CStructWriteException>(() => layout.Update(existing, "root", value, options: updateReject));
        CollectionAssert.AreEqual(new byte[] { 9, 9, 9 }, existing.ToArray());

        value.Remove("bogus");
        CollectionAssert.AreEqual(new byte[] { 1, 0, 2 }, layout.Serialize("root", value, options: reject));
    }

    /// <summary>POCOs are checked with the same case-insensitive member matching that binding uses; nested composites are checked too.</summary>
    [TestMethod]
    public void UnknownMembers_Reject_ChecksPocosAndNestedStructs()
    {
        var layout = new CStruct("struct inner { uint8 a; }; struct root { uint16 kind; inner nested; };");
        var reject = new WriteOptions { UnknownMembers = UnknownMemberPolicy.Reject, };

        CollectionAssert.AreEqual(new byte[] { 1, 0, 5 }, layout.Serialize("root", new RootPoco { Kind = 1, Nested = new InnerPoco { A = 5 } }, options: reject));

        CStructWriteException extra = Assert.Throws<CStructWriteException>(() => layout.Serialize("root", new RootPocoWithExtra { Kind = 1, Nested = new InnerPoco { A = 5 }, Extra = 2 }, options: reject));
        StringAssert.Contains(extra.Message, "'Extra' is not a member of 'root'");

        var nestedExtra = new Dictionary<string, object?> { ["kind"] = 1, ["nested"] = new Dictionary<string, object?> { ["a"] = 5, ["b"] = 6 } };
        CStructWriteException nested = Assert.Throws<CStructWriteException>(() => layout.Serialize("root", nestedExtra, options: reject));
        StringAssert.Contains(nested.Message, "'b' is not a member of 'inner'");
        StringAssert.Contains(nested.Message, "field 'nested'");

        // A parsed value round-trips under Reject: it carries exactly the declared members, promoted ones included.
        var promoted = new CStruct("struct root { uint8 kind; struct { uint8 x; uint8 y; }; };");
        StructValue parsed = promoted.Parse([1, 2, 3], "root");
        CollectionAssert.AreEqual(new byte[] { 1, 2, 3 }, promoted.Serialize("root", parsed, options: reject));
    }

    private sealed class Root
    {
        public string Name { get; set; } = string.Empty;

        public byte Tail { get; set; }
    }

    private sealed class InnerPoco
    {
        public byte A { get; set; }
    }

    private class RootPoco
    {
        public ushort Kind { get; set; }

        public InnerPoco Nested { get; set; } = new();
    }

    private sealed class RootPocoWithExtra : RootPoco
    {
        public int Extra { get; set; }
    }
}
