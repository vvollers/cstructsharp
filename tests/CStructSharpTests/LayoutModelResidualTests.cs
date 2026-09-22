namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks compiled layout call diagnostics, root capture plans and storage extents at their boundaries.</summary>
[TestClass]
public class LayoutModelResidualTests
{
    /// <summary>Supported layout functions diagnose the required number of type/name arguments before indexing them.</summary>
    /// <param name="expression">A call with an invalid argument count.</param>
    /// <param name="reason">The precise arity diagnostic.</param>
    [TestMethod]
    [DataRow("sizeof()", "sizeof expects one type name: values")]
    [DataRow("sizeof(uint8, uint16)", "sizeof expects one type name: values")]
    [DataRow("offsetof()", "offsetof expects a type name and a field name: values")]
    [DataRow("offsetof(uint8)", "offsetof expects a type name and a field name: values")]
    [DataRow("offsetof(uint8, x, y)", "offsetof expects a type name and a field name: values")]
    public void LayoutCalls_ReportWrongArgumentCounts(string expression, string reason)
    {
        // The compiler must report the function contract rather than an incidental index or conversion error.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct($"struct root {{ uint8 values[{expression}]; }};"));
        StringAssert.Contains(failure.Message, reason);
    }

    /// <summary>Compiling sizeof of the containing type detects recursion at that type's declaration.</summary>
    [TestMethod]
    public void RecursiveSizeof_PreservesTheTypeDeclarationOffset()
    {
        const string definition = "struct root { uint8 values[sizeof(root)]; };";

        // This recursion occurs during size folding rather than through a by-value member.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct(definition));
        StringAssert.Contains(failure.Message, "By-value recursive struct declarations are not supported: root");
        Assert.AreEqual(definition.IndexOf("root", StringComparison.Ordinal), failure.SourceOffset);
    }

    /// <summary>A runtime count reached through a define still captures its input field before evaluating the array.</summary>
    [TestMethod]
    public void RuntimeDefine_CapturesItsUnderlyingField()
    {
        var layout = new CStruct("#define WIDTH count\nstruct root { uint8 count; uint8 values[WIDTH]; };");
        Assert.IsTrue(layout.CompiledModel.Fields.Values.Single(field => field.Declaration.Name.Name == "count").CapturesLayoutVariable);
        dynamic value = layout.Parse(new byte[] { 2, 17, 29, }, "root");
        Assert.AreEqual(2, ((IList<object?>)value.values).Count);
    }

    /// <summary>Multiple text-shaped typedef roots preserve the fallback that captures possible indirect targets.</summary>
    [TestMethod]
    public void ReferencedTextRoots_EnableCaptureForEveryRootAndMember()
    {
        var layout = new CStruct("typedef char left[4]; typedef char right[4]; typedef uint8 orphan; struct root { uint8 unused; uint8 values[left + right]; };");
        foreach (var root in layout.CompiledModel.RootFields.Values)
        {
            Assert.IsTrue(root.CapturesLayoutVariable, root.Declaration.Name.Name);
        }

        Assert.IsTrue(layout.CompiledModel.Fields.Values.Single(field => field.Declaration.Name.Name == "unused").CapturesLayoutVariable);
    }

    /// <summary>Union tail alignment rounds a three-byte largest member only in aligned placement.</summary>
    /// <param name="aligned">Whether alignment padding is enabled.</param>
    /// <param name="size">Expected union extent in bytes.</param>
    [TestMethod]
    [DataRow(false, 3)]
    [DataRow(true, 4)]
    public void UnionExtent_IncludesOnlyRequiredTailPadding(bool aligned, int size)
    {
        var layout = new CStruct("union choice { uint8 bytes[3]; uint16 word; }; struct root { choice value; uint8 tail; };", aligned: aligned);
        Assert.AreEqual(size, layout.GetStructSizeInBytes("choice"));
        Assert.AreEqual((long)size, layout.ResolveAddress(new byte[8], "root.tail"));
    }

    /// <summary>A zero-width enum separator is valid and reserves no value of its own.</summary>
    [TestMethod]
    public void EnumSeparator_UsesItsUnderlyingStorageType()
    {
        var layout = new CStruct("enum bits : uint16 { A = 1 }; struct root { bits before:1; bits :0; bits after:1; };");
        Assert.AreEqual(3, layout.GetStructSizeInBytes("root"));
        Assert.AreEqual(2L, layout.ResolveAddress(new byte[3], "root.after"));
    }

    /// <summary>Offset zero is a valid first-field assertion and adding another field cannot wrap a fixed Int32 extent.</summary>
    [TestMethod]
    public void FixedPlacement_AcceptsZeroAndRejectsExtentOverflow()
    {
        var exact = new CStruct("struct root { uint8 value @(0); };");
        Assert.AreEqual(0L, exact.ResolveAddress(new byte[1], "root.value"));

        // The first array is representable as metadata; the following byte exceeds the supported fixed extent.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { uint8 values[2147483647]; uint8 tail; };"));
        Assert.IsInstanceOfType<OverflowException>(failure.InnerException);
    }
}
