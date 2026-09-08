namespace CStructSharp.Tests;

using CStructSharp.Structure;

/// <summary>
///     Exercises <see cref="WriterVariableProjection"/> directly, independent of a real write operation. Only
///     reachable indirectly through the public API before this type was extracted from the God-Object <c>CStruct</c>
///     partial class.
/// </summary>
[TestClass]
public class WriterVariableProjectionTests
{
    private static CStructElementWriterState CreateState()
    {
        using var stream = new MemoryStream();
        return new CStructElementWriterState(stream, [], aligned: false, new WriteOptions());
    }

    /// <summary>A pointer value is projected as its numeric address, not the pointer wrapper.</summary>
    [TestMethod]
    public void UpdateVariablesFromValue_PointerValue_StoresItsAddress()
    {
        CStructElementWriterState state = CreateState();

        WriterVariableProjection.UpdateVariablesFromValue(state, "field", new Pointer(42, null, 1));

        var literal = (Literal)state.Variables["field"];
        Assert.AreEqual(42, literal.ExactValue);
    }

    /// <summary>A string value is projected as an identifier, matching parser expression semantics.</summary>
    [TestMethod]
    public void UpdateVariablesFromValue_StringValue_StoresAsIdentifier()
    {
        CStructElementWriterState state = CreateState();

        WriterVariableProjection.UpdateVariablesFromValue(state, "field", "text");

        var identifier = (Identifier)state.Variables["field"];
        Assert.AreEqual("text", identifier.Name);
    }

    /// <summary>An ordinary scalar is projected as a literal for later array-count and expression use.</summary>
    [TestMethod]
    public void UpdateVariablesFromValue_ScalarValue_StoresAsLiteral()
    {
        CStructElementWriterState state = CreateState();

        WriterVariableProjection.UpdateVariablesFromValue(state, "field", 99);

        var literal = (Literal)state.Variables["field"];
        Assert.AreEqual(99, literal.ExactValue);
    }

    /// <summary>A value that cannot become an Int32 literal removes any stale variable instead of throwing.</summary>
    [TestMethod]
    public void UpdateVariablesFromValue_ValueCannotConvert_RemovesStaleVariable()
    {
        CStructElementWriterState state = CreateState();
        state.Variables["field"] = new Literal(1);

        WriterVariableProjection.UpdateVariablesFromValue(state, "field", new object());

        Assert.IsFalse(state.Variables.ContainsKey("field"));
    }
}
