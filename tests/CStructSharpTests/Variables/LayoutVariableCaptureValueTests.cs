namespace CStructSharp.Tests;

using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
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
        SlotValue captured = LayoutVariableCapture.ToSlotValue(Field("p"), new Pointer(42, null, 1));
        Assert.AreEqual(SlotState.Literal, captured.State);
        Assert.AreEqual((Int128)42, captured.Value);
    }

    /// <summary>A text field makes the name unusable, whatever its text says.</summary>
    [TestMethod]
    public void TextField_MakesTheNameUnusable()
    {
        SlotValue captured = LayoutVariableCapture.ToSlotValue(Field("text"), "12");
        Assert.AreEqual(SlotState.Unusable, captured.State);
        Assert.AreEqual("text", ((NotANumberVariable)captured.Payload!).Reason);
    }

    /// <summary>An integer field's value, or a caller's numeric text written to it, becomes a literal.</summary>
    [TestMethod]
    public void IntegerField_StoresALiteral()
    {
        Assert.AreEqual((Int128)99, LayoutVariableCapture.ToSlotValue(Field("n"), 99).Value);
        SlotValue text = LayoutVariableCapture.ToSlotValue(Field("n"), "12");
        Assert.AreEqual(SlotState.Literal, text.State);
        Assert.AreEqual((Int128)12, text.Value);
    }

    /// <summary>A value with no integer meaning leaves the name undefined, so a stale variable is removed, instead of throwing.</summary>
    [TestMethod]
    public void ValueWithoutIntegerMeaning_RemovesStaleVariable()
    {
        Assert.AreEqual(SlotState.Undefined, LayoutVariableCapture.ToSlotValue(Field("n"), new object()).State);
    }

    /// <summary>
    ///     Only the exception types a value that does not fit can throw are treated as "no integer"; an unexpected
    ///     failure in a caller's own conversion method propagates.
    /// </summary>
    [TestMethod]
    public void UnexpectedConversionFailure_Propagates()
    {
        Assert.Throws<InvalidOperationException>(() => LayoutVariableCapture.ToSlotValue(Field("n"), new ThrowsUnexpectedException()));
    }

    /// <summary>The not-a-number state compares by what the field holds and names the field when an expression uses it.</summary>
    [TestMethod]
    public void NotANumberVariable_ComparesByReasonAndNamesTheField()
    {
        var text = new NotANumberVariable("text");
        Assert.IsTrue(text.Equals(new NotANumberVariable("text")));
        Assert.AreEqual(new NotANumberVariable("text").GetHashCode(), text.GetHashCode());
        Assert.IsFalse(text.Equals(new NotANumberVariable("an array")));
        Assert.IsFalse(text.Equals(new Literal(1)));
        Assert.AreEqual("NotANumber: text", text.ToString());
        StringAssert.StartsWith(text.CreateFailure("tag").Message, "'tag' is text, but layout expressions can only use integer fields");
    }

    /// <summary>The generated-code helpers fail with the runtime's texts: a uint128 member outside Int128, and a shared non-integer member.</summary>
    [TestMethod]
    public void GeneratedHelpers_MatchTheRuntimeDiagnostics()
    {
        Assert.AreEqual((Int128)7, CStructSharp.Generated.Expressions.FromUInt128((UInt128)7, "n"));
        string runtime = ((WideValueVariable)LayoutVariableCapture.ToSlotValue(Field("n"), UInt128.MaxValue).Payload!).CreateFailure("n").Message;
        Assert.AreEqual(
            runtime,
            Assert.Throws<InvalidOperationException>(() => CStructSharp.Generated.Expressions.FromUInt128(UInt128.MaxValue, "n")).Message);
        StringAssert.StartsWith(runtime, "'n' is 340282366920938463463374607431768211455, which is outside the 128-bit range");
        Assert.AreEqual(
            new NotANumberVariable("text").CreateFailure("tag").Message,
            Assert.Throws<InvalidOperationException>(() => CStructSharp.Generated.Expressions.NotAnInteger("tag", "text")).Message);
    }

    /// <summary>Returns a compiled field of the fixture layout.</summary>
    /// <param name="name">The field name.</param>
    /// <returns>The field.</returns>
    private static CompiledField Field(string name) => Layout.CompiledModel.AllFields().Single(field => field.Name == name);

    /// <summary>Raises an unexpected Int64 conversion failure while rejecting every unrelated conversion.</summary>
    private sealed class ThrowsUnexpectedException : IConvertible
    {
        /// <inheritdoc/>
        public TypeCode GetTypeCode() => TypeCode.Object;

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
        public int ToInt32(IFormatProvider? provider) => throw new NotSupportedException();

        /// <inheritdoc/>
        public long ToInt64(IFormatProvider? provider) => throw new InvalidOperationException("Not an overflow, cast, or format failure.");

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
