namespace CStructSharp.Tests;

using CStructSharp.Compilation;
using CStructSharp.Expressions;
using CStructSharp.Syntax;
using CStructSharp.Values;

/// <summary>
///     Checks the single capture rule directly: what each kind of field value becomes as a layout variable, and that an
///     unexpected failure in a caller's conversion method propagates.
/// </summary>
[TestClass]
public class LayoutVariableCaptureValueTests
{
    private static readonly CStruct Layout = new("struct root { uint8 *p; char text[4]; uint8 n; };", pointerSize: 1);

    /// <summary>A pointer field's variable is its stored address, not the pointer wrapper.</summary>
    [TestMethod]
    public void PointerField_StoresItsAddress()
    {
        var variables = new Dictionary<string, Expr>();
        LayoutVariableCapture.Capture(variables, "p", Field("p"), new Pointer(42, null, 1));
        Assert.AreEqual(42, ((Literal)variables["p"]).ExactValue);
    }

    /// <summary>A text field makes the name unusable, whatever its text says.</summary>
    [TestMethod]
    public void TextField_MakesTheNameUnusable()
    {
        var variables = new Dictionary<string, Expr> { ["text"] = new Literal(1), };
        LayoutVariableCapture.Capture(variables, "text", Field("text"), "12");
        Assert.AreEqual("text", ((NotANumberVariable)variables["text"]).Reason);
    }

    /// <summary>An integer field's value, or a caller's numeric text written to it, becomes a literal.</summary>
    [TestMethod]
    public void IntegerField_StoresALiteral()
    {
        var variables = new Dictionary<string, Expr>();
        LayoutVariableCapture.Capture(variables, "n", Field("n"), 99);
        Assert.AreEqual(99, ((Literal)variables["n"]).ExactValue);
        LayoutVariableCapture.Capture(variables, "n", Field("n"), "12");
        Assert.AreEqual(12, ((Literal)variables["n"]).ExactValue);
    }

    /// <summary>A value with no integer meaning removes a stale variable instead of throwing.</summary>
    [TestMethod]
    public void ValueWithoutIntegerMeaning_RemovesStaleVariable()
    {
        var variables = new Dictionary<string, Expr> { ["n"] = new Literal(1), };
        LayoutVariableCapture.Capture(variables, "n", Field("n"), new object());
        Assert.IsFalse(variables.ContainsKey("n"));
    }

    /// <summary>
    ///     Only the exception types a value that does not fit can throw are treated as "no integer"; an unexpected
    ///     failure in a caller's own conversion method propagates.
    /// </summary>
    [TestMethod]
    public void UnexpectedConversionFailure_Propagates()
    {
        var variables = new Dictionary<string, Expr>();
        Assert.Throws<InvalidOperationException>(() => LayoutVariableCapture.Capture(variables, "n", Field("n"), new ThrowsUnexpectedException()));
    }

    /// <summary>Returns a compiled field of the fixture layout.</summary>
    /// <param name="name">The field name.</param>
    /// <returns>The field.</returns>
    private static CompiledField Field(string name) => Layout.CompiledModel.AllFields().Single(field => field.Name == name);

    /// <summary>Raises an unexpected Int32 conversion failure while rejecting every unrelated conversion.</summary>
    private sealed class ThrowsUnexpectedException : IConvertible
    {
        /// <inheritdoc/>
        public TypeCode GetTypeCode() => throw new NotSupportedException();

        /// <inheritdoc/>
        public bool ToBoolean(IFormatProvider? provider) => throw new NotSupportedException();

        /// <inheritdoc/>
        public byte ToByte(IFormatProvider? provider) => throw new NotSupportedException();

        /// <inheritdoc/>
        public char ToChar(IFormatProvider? provider) => throw new NotSupportedException();

        /// <inheritdoc/>
        public DateTime ToDateTime(IFormatProvider? provider) => throw new NotSupportedException();

        /// <inheritdoc/>
        public decimal ToDecimal(IFormatProvider? provider) => throw new NotSupportedException();

        /// <inheritdoc/>
        public double ToDouble(IFormatProvider? provider) => throw new NotSupportedException();

        /// <inheritdoc/>
        public short ToInt16(IFormatProvider? provider) => throw new NotSupportedException();

        /// <inheritdoc/>
        public int ToInt32(IFormatProvider? provider) => throw new InvalidOperationException("Not an overflow, cast, or format failure.");

        /// <inheritdoc/>
        public long ToInt64(IFormatProvider? provider) => throw new NotSupportedException();

        /// <inheritdoc/>
        public sbyte ToSByte(IFormatProvider? provider) => throw new NotSupportedException();

        /// <inheritdoc/>
        public float ToSingle(IFormatProvider? provider) => throw new NotSupportedException();

        /// <inheritdoc/>
        public string ToString(IFormatProvider? provider) => throw new NotSupportedException();

        /// <inheritdoc/>
        public object ToType(Type conversionType, IFormatProvider? provider) => throw new NotSupportedException();

        /// <inheritdoc/>
        public ushort ToUInt16(IFormatProvider? provider) => throw new NotSupportedException();

        /// <inheritdoc/>
        public uint ToUInt32(IFormatProvider? provider) => throw new NotSupportedException();

        /// <inheritdoc/>
        public ulong ToUInt64(IFormatProvider? provider) => throw new NotSupportedException();
    }
}
