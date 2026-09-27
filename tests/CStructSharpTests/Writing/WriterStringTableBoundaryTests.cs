namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks string-table diagnostics and reuse of already materialized row lists.</summary>
[TestClass]
public class WriterStringTableBoundaryTests
{
    /// <summary>An existing row list can be written without copying its collection storage.</summary>
    [TestMethod]
    public void StringRows_ReuseTheMaterializedList()
    {
        var layout = new CStruct("struct root { uint8 count; uint8 padding[count]; char rows[2][3]; };");
        var rows = new CopyCountingList { "abc", "def", };
        var data = new Dictionary<string, object?>
        {
            ["count"] = (byte)0,
            ["padding"] = Array.Empty<byte>(),
            ["rows"] = rows,
        };

        CollectionAssert.AreEqual(new byte[] { 0, 97, 98, 99, 100, 101, 102, }, layout.Serialize("root", data));
        Assert.AreEqual(0, rows.CopyCount);
        CollectionAssert.AreEqual(new object[] { "abc", "def", }, rows);
    }

    /// <summary>An overlong string row reports its known length and the declared character capacity.</summary>
    /// <param name="type">Narrow or wide character storage for each row.</param>
    [TestMethod]
    [DataRow("char")]
    [DataRow("wchar")]
    public void OverlongStringRow_ReportsTheExactLength(string type)
    {
        var layout = new CStruct("struct root { " + type + " rows[2][3]; };");
        var data = new Dictionary<string, object?> { ["rows"] = new[] { "abcd", "x", }, };

        // A string already has a known length; do not replace its diagnostic with bounded-enumeration wording.
        CStructWriteException failure = Assert.Throws<CStructWriteException>(() => layout.Serialize("root", data));
        StringAssert.StartsWith(failure.Message, "String is too long for rows: 4 > 3");
    }

    /// <summary>Records collection copies while retaining normal list indexing and enumeration.</summary>
    private sealed class CopyCountingList : List<object>, ICollection<object>
    {
        /// <summary>Gets the number of calls that copy this collection into another array.</summary>
        public int CopyCount { get; private set; }

        /// <summary>Copies all items and records that the caller requested duplicate storage.</summary>
        /// <param name="array">The destination array.</param>
        /// <param name="arrayIndex">The first destination element index.</param>
        void ICollection<object>.CopyTo(object[] array, int arrayIndex)
        {
            this.CopyCount++;
            this.CopyTo(array, arrayIndex);
        }
    }
}
