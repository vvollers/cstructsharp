namespace CStructSharp.Tests;

using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;

/// <summary>
///     Verifies the compiled variable-capture plan for nested expressions, the fields an expression may name, and the
///     runtime fields a deferred definition captures while writing.
/// </summary>
[TestClass]
public class LayoutCaptureBoundaryTests
{
    /// <summary>A deferred internal definition override captures an otherwise unreferenced field while reading.</summary>
    [TestMethod]
    public void DeferredDefinition_ReadsItsRuntimeCountField()
    {
        var layout = new CStruct("#define COUNT 1\nstruct root { uint8 count; uint8 values[COUNT]; };");
        var variables = new Dictionary<string, Expr> { ["COUNT"] = new Identifier("count"), };
        using var input = new MemoryStream(new byte[] { 2, 11, 12, });

        dynamic parsed = layout.Parse(input, "root", variables);
        Assert.AreEqual((byte)2, parsed.count);
        CollectionAssert.AreEqual(new object?[] { (byte)11, (byte)12, }, ((IList<object?>)parsed.values).ToArray());
        Assert.AreEqual(3L, input.Position);
    }

    /// <summary>Both conditional arms and unary operands contribute dependencies before an input selects one arm.</summary>
    [TestMethod]
    public void ConditionalAndUnaryCounts_CaptureEveryReferencedField()
    {
        var layout = new CStruct("struct root { uint8 choose; uint8 a; uint8 z; uint8 unused; uint8 values[choose ? !a : z]; };");
        foreach (string name in new[] { "choose", "a", "z" })
        {
            Assert.IsTrue(Field(layout, name).CapturesLayoutVariable, name);
        }

        Assert.IsFalse(Field(layout, "unused").CapturesLayoutVariable);
        CollectionAssert.AreEqual(new[] { "a", "choose", "z" }, Field(layout, "values").Array.Dependencies.ToArray());
        dynamic first = layout.Parse(new byte[] { 1, 0, 2, 9, 7 }, "root");
        dynamic second = layout.Parse(new byte[] { 0, 0, 2, 9, 7, 8 }, "root");
        Assert.AreEqual(1, ((IList<object?>)first.values).Count);
        Assert.AreEqual(2, ((IList<object?>)second.values).Count);
    }

    /// <summary>Expressions in an inline typedef still retain their runtime count fields.</summary>
    [TestMethod]
    public void InlineTypedef_CapturesItsCountField()
    {
        var layout = new CStruct("typedef struct { uint8 count; uint8 values[count]; } packet;");
        Assert.IsTrue(Field(layout, "count").CapturesLayoutVariable);
        dynamic parsed = layout.Parse(new byte[] { 2, 7, 8 }, "packet");
        Assert.AreEqual(2, ((IList<object?>)parsed.values).Count);
    }

    /// <summary>Text fields are not integers: comparing two of them in a condition fails layout construction.</summary>
    [TestMethod]
    public void TextComparison_FailsConstruction()
    {
        CStructLayoutException failure = Assert.ThrowsExactly<CStructLayoutException>(() => new CStruct("struct root { char left[4]; char right[4]; uint8 unused; if (left == right) { uint8 same; } };"));
        StringAssert.Contains(failure.Message, "Field 'left' is text");
    }

    /// <summary>A reference to an enum or pointer value captures that field and no unrelated one.</summary>
    /// <param name="definition">A layout containing a non-text reference and an unrelated field.</param>
    [TestMethod]
    [DataRow("enum kind : uint8 { A = 1 }; struct root { kind value; uint8 unused; uint8 items[value]; };")]
    [DataRow("struct root { uint8 *value; uint8 unused; uint8 items[value]; };")]
    public void NonTextReferences_DoNotCaptureUnrelatedValues(string definition)
    {
        var layout = new CStruct(definition);
        Assert.IsTrue(Field(layout, "value").CapturesLayoutVariable);
        Assert.IsFalse(Field(layout, "unused").CapturesLayoutVariable);
    }

    /// <summary>A struct or union is not an integer: naming one in a condition fails layout construction.</summary>
    /// <param name="definition">A layout whose condition names a composite field.</param>
    /// <param name="reason">What the diagnostic says the field is.</param>
    [TestMethod]
    [DataRow("struct child { uint8 n; }; struct root { child value; uint8 unused; if (value) { uint8 item; } };", "a struct")]
    [DataRow("union child { uint8 n; uint16 m; }; struct root { child value; uint8 unused; if (value) { uint8 item; } };", "a union")]
    public void CompositeReference_FailsConstruction(string definition, string reason)
    {
        CStructLayoutException failure = Assert.ThrowsExactly<CStructLayoutException>(() => new CStruct(definition));
        StringAssert.Contains(failure.Message, "Field 'value' is " + reason);
    }

    /// <summary>A deferred definition override can use a field that the original layout did not reference.</summary>
    [TestMethod]
    public void DeferredDefinition_CapturesItsRuntimeField()
    {
        var layout = new CStruct("#define COUNT 1\nstruct root { uint8 count; uint8 values[COUNT]; };");
        var data = new Dictionary<string, object?>
        {
            ["count"] = (byte)2,
            ["values"] = new byte[] { 11, 12, },
        };
        var variables = new Dictionary<string, Expr> { ["COUNT"] = new Identifier("count"), };

        CollectionAssert.AreEqual(new byte[] { 2, 11, 12, }, layout.Serialize("root", data, variables));
    }

    /// <summary>Returns the uniquely named field whose capture and dependency metadata the test examines.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="name">The unique declaration name.</param>
    /// <returns>The compiled field.</returns>
    private static CompiledField Field(CStruct layout, string name)
    {
        // Each fixture deliberately uses unique member names so this lookup cannot select an unrelated field.
        return layout.CompiledModel.AllFields().Single(field => field.Declaration.Name.Name == name);
    }
}
