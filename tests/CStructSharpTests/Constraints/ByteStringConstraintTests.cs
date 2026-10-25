namespace CStructSharp.Tests;

using System.Text;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     Byte-string comparisons in constraints (<c>oem == "NTFS    "</c>) on the runtime layout: equal, different and short input,
///     <c>!=</c>, both quoting forms, <c>char</c> and <c>uint8</c> arrays, <c>TrimFixedText</c>, and the layout errors.
/// </summary>
[TestClass]
public class ByteStringConstraintTests
{
    private const string Boot = "struct boot { uint8 oem[8] @expect(oem == \"NTFS    \", \"ntfs.oem\"); uint32 sectors; };";

    /// <summary>Latin-1 bytes of a text followed by more bytes.</summary>
    /// <param name="text">The text, one byte per character.</param>
    /// <param name="tail">The bytes after it.</param>
    /// <returns>The concatenation.</returns>
    private static byte[] Bytes(string text, params byte[] tail) => [.. Encoding.Latin1.GetBytes(text), .. tail];

    /// <summary>Equal bytes pass, any different byte fails with the label and the unsupported/malformed kind of the suffix.</summary>
    [TestMethod]
    public void Uint8Array_ComparesAllBytes()
    {
        var layout = new CStruct(Boot);
        layout.Parse(Bytes("NTFS    ", 1, 0, 0, 0), "boot");

        foreach (string wrong in new[] { "NTFS   X", "XTFS    ", "ntfs    ", "NTFS\0\0\0\0" })
        {
            var failure = Assert.Throws<CStructConstraintException>(() => layout.Parse(Bytes(wrong, 1, 0, 0, 0), "boot"));
            Assert.AreEqual("ntfs.oem", failure.Label, wrong);
            Assert.AreEqual(ConstraintKind.Malformed, failure.Kind);
        }
    }

    /// <summary><c>!=</c> is the negation; the <c>b"..."</c> form takes hex escapes and the two forms mean the same bytes.</summary>
    [TestMethod]
    public void NotEqual_AndEscapes()
    {
        var elf = new CStruct("struct h { uint8 ident[4] @magic(ident == b\"\\x7fELF\", \"elf\"); };");
        elf.Parse(new byte[] { 0x7F, 0x45, 0x4C, 0x46 }, "h");
        Assert.AreEqual("elf", Assert.Throws<CStructConstraintException>(() => elf.Parse(new byte[] { 0x7F, 0x45, 0x4C, 0x47 }, "h")).Label);

        var notNtfs = new CStruct("struct h { uint8 oem[4] @expect(oem != \"NTFS\", \"not.ntfs\"); };");
        notNtfs.Parse(Bytes("EXFA"), "h");
        Assert.AreEqual("not.ntfs", Assert.Throws<CStructConstraintException>(() => notNtfs.Parse(Bytes("NTFS"), "h")).Label);

        var both = new CStruct("struct h { uint8 a[2] @expect(a == \"\\x41\\x42\"); uint8 b[2] @expect(b == b\"AB\"); };");
        both.Parse(Bytes("ABAB"), "h");
    }

    /// <summary>A <c>char</c> array compares its raw bytes, so <c>TrimFixedText</c> does not change the answer.</summary>
    [TestMethod]
    public void CharArray_IgnoresTrimFixedText()
    {
        var layout = new CStruct("struct h { char name[8] @expect(name == \"AB\\0\\0\\0\\0\\0\\0\"); };");
        byte[] data = Bytes("AB\0\0\0\0\0\0");
        layout.Parse(data, "h");
        layout.Parse(data, "h", null, new ReadOptions { TrimFixedText = true });
        Assert.Throws<CStructConstraintException>(() => layout.Parse(Bytes("AB\0\0\0\0\0X"), "h", null, new ReadOptions { TrimFixedText = true }));
        Assert.Throws<CStructConstraintException>(() => layout.Parse(Bytes("ABC\0\0\0\0\0"), "h", null, new ReadOptions { TrimFixedText = true }));
    }

    /// <summary>Several comparisons on one field, and a comparison mixed with ordinary operators.</summary>
    [TestMethod]
    public void Alternatives_AndMixedConditions()
    {
        var layout = new CStruct("struct h { uint8 kind; uint8 fs[3] @expect(fs == \"FAT\" || (fs == \"EXT\" && kind == 2), \"fs\"); };");
        layout.Parse(Bytes("\u0001FAT"), "h");
        layout.Parse(Bytes("\u0002EXT"), "h");
        Assert.Throws<CStructConstraintException>(() => layout.Parse(Bytes("\u0001EXT"), "h"));
    }

    /// <summary>A magic with a byte string keeps its first-check order and a short source still reports the short read.</summary>
    [TestMethod]
    public void MagicByteString_IsCheckedFirst()
    {
        var layout = new CStruct("struct h { uint32 count @expect(count > 0, \"count\"); uint8 sig[4] @magic(sig == \"PK\\x03\\x04\", \"zip\"); };");
        Assert.AreEqual("zip", Assert.Throws<CStructConstraintException>(() => layout.Parse(Bytes("\0\0\0\0XXXX"), "h")).Label);
        Assert.AreEqual("count", Assert.Throws<CStructConstraintException>(() => layout.Parse(Bytes("\0\0\0\0PK\u0003\u0004"), "h")).Label);
        layout.Parse(Bytes("\u0001\0\0\0PK\u0003\u0004"), "h");
        Assert.Throws<CStructReadException>(() => layout.Parse(Bytes("\u0001\0\0\0PK"), "h"));
    }

    /// <summary>Collected violations record a failed comparison and go on.</summary>
    [TestMethod]
    public void CollectedViolations_IncludeTheComparison()
    {
        var found = new List<CStructConstraintException>();
        new CStruct(Boot).Parse(Bytes("XXXXXXXX", 1, 0, 0, 0), "boot", null, new ReadOptions { ConstraintViolations = found });
        Assert.AreEqual("ntfs.oem", Assert.ContainsSingle(found).Label);
    }

    /// <summary>The definition keeps the text, and a skipping read evaluates nothing.</summary>
    [TestMethod]
    public void Definition_AndSkipPolicy()
    {
        var layout = new CStruct(Boot);
        StringAssert.Contains(layout.ToDefinition(), "oem == \"NTFS    \"");
        layout.Parse(Bytes("XXXXXXXX", 1, 0, 0, 0), "boot", null, new ReadOptions { Constraints = ConstraintPolicy.Skip });
    }

    /// <summary>A byte string anywhere but beside a fixed-size uint8 or char array of the same length is a layout error.</summary>
    [TestMethod]
    public void Misuse_IsALayoutError()
    {
        string[] bad =
        [
            "struct h { uint8 a[4] @expect(a == \"AB\"); };",                  // length differs
            "struct h { uint8 a[4]; uint8 b @expect(a == \"ABCD\"); };",       // another field
            "struct h { uint16 a[2] @expect(a == \"ABCD\"); };",               // not bytes
            "struct h { uint8 a @expect(a == \"A\"); };",                       // not an array
            "struct h { uint8 n; uint8 a[n] @expect(a == \"AB\"); };",          // not fixed
            "struct h { uint8 a[2][2] @expect(a == \"ABCD\"); };",              // two dimensions
            "struct h { uint8 a[2] @expect(a < \"AB\"); };",                    // wrong operator
            "struct h { uint8 a[2] @expect(a + 1 == \"AB\"); };",               // not a bare name
            "struct h { uint8 a[2] @expect(\"AB\"); };",                         // no field
            "struct h { uint8 a[2] @expect(a == \"\"); };",                      // empty
            "struct h { uint8 a[2] @expect(a == \"A\\q\"); };",                  // unknown escape
            "struct h { uint8 a[2] @expect(a == b\"\u00e9\u00e9\"); };",         // not ASCII
            "struct h { uint8 a[\"AB\"]; };",                                    // not in a count
            "struct h { uint8 a[2] @expect(a == \"AB); };",                      // unterminated
        ];
        foreach (string layout in bad)
        {
            Assert.Throws<CStructLayoutException>(() => new CStruct(layout), layout);
        }
    }
}
