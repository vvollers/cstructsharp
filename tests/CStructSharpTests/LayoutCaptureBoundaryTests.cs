namespace CStructSharp.Tests;

using CStructSharp.Compilation;

/// <summary>Verifies the compiled variable-capture plan for nested expressions and indirect text references.</summary>
[TestClass]
public class LayoutCaptureBoundaryTests
{
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

    /// <summary>Two text references cannot cancel the fallback that captures potential indirect targets.</summary>
    [TestMethod]
    public void MultipleTextReferences_KeepTheCaptureAllFallback()
    {
        var layout = new CStruct("struct root { char left[4]; char right[4]; uint8 unused; if (left == right) { uint8 same; } };");
        Assert.IsTrue(Field(layout, "left").CapturesLayoutVariable);
        Assert.IsTrue(Field(layout, "right").CapturesLayoutVariable);
        Assert.IsTrue(Field(layout, "unused").CapturesLayoutVariable);
        Assert.IsTrue(Field(layout, "same").CapturesLayoutVariable);
    }

    /// <summary>References to enum, pointer and composite values do not by themselves enable text indirection.</summary>
    /// <param name="definition">A layout containing a non-text reference and an unrelated field.</param>
    [TestMethod]
    [DataRow("enum kind : uint8 { A = 1 }; struct root { kind value; uint8 unused; uint8 items[value]; };")]
    [DataRow("struct root { uint8 *value; uint8 unused; uint8 items[value]; };")]
    [DataRow("struct child { uint8 n; }; struct root { child value; uint8 unused; if (value) { uint8 item; } };")]
    [DataRow("union child { uint8 n; uint16 m; }; struct root { child value; uint8 unused; if (value) { uint8 item; } };")]
    public void NonTextReferences_DoNotCaptureUnrelatedValues(string definition)
    {
        var layout = new CStruct(definition);
        Assert.IsTrue(Field(layout, "value").CapturesLayoutVariable);
        Assert.IsFalse(Field(layout, "unused").CapturesLayoutVariable);
    }

    /// <summary>Returns the uniquely named field whose capture and dependency metadata the test examines.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="name">The unique declaration name.</param>
    /// <returns>The compiled field.</returns>
    private static CompiledField Field(CStruct layout, string name)
    {
        // Each fixture deliberately uses unique member names so this lookup cannot select an unrelated field.
        return layout.CompiledModel.Fields.Values.Single(field => field.Declaration.Name.Name == name);
    }
}
