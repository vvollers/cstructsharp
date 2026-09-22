namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks errors when definition metadata is selected instead of a readable binary value.</summary>
[TestClass]
public class DefinitionReadBoundaryTests
{
    /// <summary>An integer definition supplies expression metadata but no standalone parsed value.</summary>
    /// <param name="naturalValue">Whether the caller requests a natural value rather than a parsed root object.</param>
    [TestMethod]
    [DataRow(false)]
    [DataRow(true)]
    public void DefinitionRoot_ExplainsTheMissingBinaryValue(bool naturalValue)
    {
        var layout = new CStruct("#define COUNT 1\nstruct root { uint8 value; };");
        using var input = new MemoryStream(new byte[] { 42, });

        // Evaluating a definition does not create a result member or consume source bytes.
        CStructPathException failure = Assert.Throws<CStructPathException>(() =>
        {
            if (naturalValue)
            {
                _ = layout.ReadValue(input, "COUNT");
            }
            else
            {
                _ = layout.Parse(input, "COUNT");
            }
        });
        string reason = naturalValue
                            ? "The selected layout element does not produce a readable value"
                            : "The selected path does not resolve to a composite object";
        StringAssert.StartsWith(failure.Message, reason);
        Assert.AreEqual(0L, input.Position);
    }

    /// <summary>An unresolved definition selected for reading identifies its evaluation context.</summary>
    [TestMethod]
    public void UnresolvedDefinition_IdentifiesTheReadContext()
    {
        var layout = new CStruct("#define COUNT missing\nstruct root { uint8 value; };");
        using var input = new MemoryStream();

        // The unused definition can compile, but selecting it must identify the failed definition.
        CStructReadException failure = Assert.Throws<CStructReadException>(() => layout.ReadValue(input, "COUNT"));
        StringAssert.StartsWith(failure.Message, "Cannot evaluate definition COUNT:");
        StringAssert.Contains(failure.Message, "missing");
    }
}
