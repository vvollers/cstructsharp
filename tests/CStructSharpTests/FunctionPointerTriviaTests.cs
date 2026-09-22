namespace CStructSharp.Tests;

using CStructSharp.Parsing;
using CStructSharp.Syntax;

/// <summary>Checks whitespace and comments at the boundaries of opaque function-pointer declarations.</summary>
[TestClass]
public class FunctionPointerTriviaTests
{
    /// <summary>Trivia around the pointer marker and after its parameter list does not change the stored pointer.</summary>
    /// <param name="declaration">A supported function-pointer field with boundary trivia.</param>
    [TestMethod]
    [DataRow("void ( /* marker */ *callback)(void);")]
    [DataRow("void (*callback)(void) /* trailing */ ;")]
    public void FunctionPointer_PreservesItsShapeAcrossTrivia(string declaration)
    {
        IReadOnlyList<Field> fields = LayoutParser.ParseFieldGroup(declaration);
        Assert.HasCount(1, fields);
        Assert.AreEqual("callback", fields[0].Name.Name);
        Assert.AreEqual("void", fields[0].Type.Name);
        Assert.AreEqual(1, fields[0].PointerDepth);
        var layout = new CStruct("struct root { " + declaration + " };", pointerSize: 8);
        Assert.AreEqual(8, layout.GetStructSizeInBytes("root"));
    }
}
