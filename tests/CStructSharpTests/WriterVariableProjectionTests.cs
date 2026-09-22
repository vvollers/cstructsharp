namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Codecs;
using CStructSharp.Syntax;
using CStructSharp.Values;
using CStructSharp.Writing;

/// <summary>
///     Checks scalar layout-variable projection directly and through synthetic-root writes, including unexpected
///     failures raised by caller-provided conversion methods.
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

        // Convert.ToInt32(object) throws InvalidCastException for a value that is not IConvertible at all - this
        // is the case a caller-supplied POCO/dynamic value can genuinely hit, unlike CStructReader.cs's own
        // capture sites, which only ever call Convert.ToInt32 on a value already known to be IConvertible.
        WriterVariableProjection.UpdateVariablesFromValue(state, "field", new object());

        Assert.IsFalse(state.Variables.ContainsKey("field"));
    }

    /// <summary>
    ///     Regression coverage for the architecture improvement plan's fix that narrowed a bare <c>catch</c> to the specific exception types a value genuinely unable to become an
    ///     Int32 literal can throw. A value whose own IConvertible implementation throws something else entirely
    ///     (simulating a bug in caller code, not an expected "this value doesn't fit" shape) must propagate rather
    ///     than being silently swallowed and treated the same as an ordinary conversion failure.
    /// </summary>
    [TestMethod]
    public void UpdateVariablesFromValue_UnexpectedExceptionType_Propagates()
    {
        CStructElementWriterState state = CreateState();

        Assert.Throws<InvalidOperationException>(
            () => WriterVariableProjection.UpdateVariablesFromValue(state, "field", new ThrowsUnexpectedException()));
    }

    /// <summary>A synthetic custom root must preserve unexpected failures while projecting the supplied value into layout variables.</summary>
    [TestMethod]
    public void SyntheticCustomRoot_PreservesUnexpectedProjectionFailures()
    {
        var layout = new CStruct("struct unused { uint8 value; };", compilationOptions: new CStructCompilationOptions { Codecs = [new ProjectionCodec(),], });

        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(
            () => layout.Serialize("projection_value", new ThrowsUnexpectedException()));

        Assert.AreEqual("Not an overflow, cast, or format failure.", failure.Message);
    }

    /// <summary>Writes one marker byte without converting its input, leaving scalar projection to the library.</summary>
    private sealed class ProjectionCodec : ICustomCodec
    {
        public string Name => "projection_value";

        public int? FixedSize => 1;

        public int Alignment => 1;

        /// <summary>Rejects decoding because this fixture exercises only writing and subsequent value projection.</summary>
        /// <param name="source">Unused input.</param>
        /// <param name="value">Always null.</param>
        /// <param name="bytesConsumed">Always zero.</param>
        /// <returns>InvalidData without consuming input.</returns>
        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
        {
            value = null;
            bytesConsumed = 0;
            return OperationStatus.InvalidData;
        }

        /// <summary>Writes a marker without inspecting the supplied object's conversion methods.</summary>
        /// <param name="destination">Output receiving one zero byte when space is available.</param>
        /// <param name="value">Unused caller value.</param>
        /// <param name="bytesWritten">One on success; otherwise zero.</param>
        /// <returns>Done on success, or DestinationTooSmall without changing output.</returns>
        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            bytesWritten = 0;
            if (destination.IsEmpty)
            {
                return OperationStatus.DestinationTooSmall;
            }

            destination[0] = 0;
            bytesWritten = 1;
            return OperationStatus.Done;
        }
    }

    /// <summary>Raises an unexpected Int32 conversion failure while rejecting every unrelated conversion.</summary>
    private sealed class ThrowsUnexpectedException : IConvertible
    {
        public TypeCode GetTypeCode() => throw new NotSupportedException();

        public bool ToBoolean(IFormatProvider? provider) => throw new NotSupportedException();

        public byte ToByte(IFormatProvider? provider) => throw new NotSupportedException();

        public char ToChar(IFormatProvider? provider) => throw new NotSupportedException();

        public DateTime ToDateTime(IFormatProvider? provider) => throw new NotSupportedException();

        public decimal ToDecimal(IFormatProvider? provider) => throw new NotSupportedException();

        public double ToDouble(IFormatProvider? provider) => throw new NotSupportedException();

        public short ToInt16(IFormatProvider? provider) => throw new NotSupportedException();

        public int ToInt32(IFormatProvider? provider) => throw new InvalidOperationException("Not an overflow, cast, or format failure.");

        public long ToInt64(IFormatProvider? provider) => throw new NotSupportedException();

        public sbyte ToSByte(IFormatProvider? provider) => throw new NotSupportedException();

        public float ToSingle(IFormatProvider? provider) => throw new NotSupportedException();

        public string ToString(IFormatProvider? provider) => throw new NotSupportedException();

        public object ToType(Type conversionType, IFormatProvider? provider) => throw new NotSupportedException();

        public ushort ToUInt16(IFormatProvider? provider) => throw new NotSupportedException();

        public uint ToUInt32(IFormatProvider? provider) => throw new NotSupportedException();

        public ulong ToUInt64(IFormatProvider? provider) => throw new NotSupportedException();
    }
}
