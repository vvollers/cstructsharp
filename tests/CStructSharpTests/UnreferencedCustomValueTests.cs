namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Codecs;

/// <summary>Checks address traversal does not convert an unreferenced custom value into a layout count.</summary>
[TestClass]
public class UnreferencedCustomValueTests
{
    /// <summary>Reading a preceding codec value does not authorize an unused custom integer conversion.</summary>
    [TestMethod]
    public void UnreferencedValue_DoesNotInvokeIntegerConversion()
    {
        var number = new CountingNumber();
        var codec = new NumberCodec(number);
        var layout = new CStruct("struct root { marker ignored; uint8 tail; };", compilationOptions: new CStructCompilationOptions { Codecs = [codec,], });
        using var source = new MemoryStream(new byte[] { 17, 99, });
        Assert.AreEqual((byte)99, layout.ReadValue<byte>(source, "root.tail"));
        Assert.IsTrue(codec.ReadCalls > 0);
        Assert.AreEqual(0, number.IntegerConversions);
    }

    /// <summary>Returns a caller-owned convertible value while consuming one byte.</summary>
    private sealed class NumberCodec : ICustomCodec
    {
        private readonly CountingNumber number;

        /// <summary>Creates a codec that returns the supplied conversion observer.</summary>
        /// <param name="number">The value returned by a successful read.</param>
        public NumberCodec(CountingNumber number) => this.number = number;

        public string Name => "marker";

        public int? FixedSize => 1;

        public int Alignment => 1;

        /// <summary>Gets the number of decoder invocations.</summary>
        public int ReadCalls { get; private set; }

        /// <summary>Consumes one byte and returns the observer without converting it.</summary>
        /// <param name="source">The available encoded bytes.</param>
        /// <param name="value">The observer on success, otherwise null.</param>
        /// <param name="bytesConsumed">One on success, otherwise zero.</param>
        /// <returns>Done when a byte is present, otherwise NeedMoreData.</returns>
        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
        {
            this.ReadCalls++;
            bytesConsumed = source.IsEmpty ? 0 : 1;
            value = source.IsEmpty ? null : this.number;
            return source.IsEmpty ? OperationStatus.NeedMoreData : OperationStatus.Done;
        }

        /// <summary>Rejects writes because this fixture observes read-side conversion only.</summary>
        /// <param name="destination">The unused output window.</param>
        /// <param name="value">The unused value.</param>
        /// <param name="bytesWritten">No value is assigned because the operation throws.</param>
        /// <returns>Never returns.</returns>
        /// <exception cref="NotSupportedException">This test codec supports reads only.</exception>
        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten) => throw new NotSupportedException();
    }

    /// <summary>Wraps the integer seventeen and records only explicit Int32 conversion requests.</summary>
    private sealed class CountingNumber : IConvertible
    {
        private readonly IConvertible value = 17;

        /// <summary>Gets the number of calls to the custom Int32 conversion.</summary>
        public int IntegerConversions { get; private set; }

        /// <inheritdoc/>
        public TypeCode GetTypeCode() => this.value.GetTypeCode();

        /// <inheritdoc/>
        public bool ToBoolean(IFormatProvider? provider) => this.value.ToBoolean(provider);

        /// <inheritdoc/>
        public byte ToByte(IFormatProvider? provider) => this.value.ToByte(provider);

        /// <inheritdoc/>
        public char ToChar(IFormatProvider? provider) => this.value.ToChar(provider);

        /// <inheritdoc/>
        public DateTime ToDateTime(IFormatProvider? provider) => this.value.ToDateTime(provider);

        /// <inheritdoc/>
        public decimal ToDecimal(IFormatProvider? provider) => this.value.ToDecimal(provider);

        /// <inheritdoc/>
        public double ToDouble(IFormatProvider? provider) => this.value.ToDouble(provider);

        /// <inheritdoc/>
        public short ToInt16(IFormatProvider? provider) => this.value.ToInt16(provider);

        /// <summary>Records this conversion and returns the wrapped integer.</summary>
        /// <param name="provider">The caller's conversion provider.</param>
        /// <returns>Seventeen.</returns>
        public int ToInt32(IFormatProvider? provider)
        {
            this.IntegerConversions++;
            return this.value.ToInt32(provider);
        }

        /// <inheritdoc/>
        public long ToInt64(IFormatProvider? provider) => this.value.ToInt64(provider);

        /// <inheritdoc/>
        public sbyte ToSByte(IFormatProvider? provider) => this.value.ToSByte(provider);

        /// <inheritdoc/>
        public float ToSingle(IFormatProvider? provider) => this.value.ToSingle(provider);

        /// <inheritdoc/>
        public string ToString(IFormatProvider? provider) => this.value.ToString(provider);

        /// <inheritdoc/>
        public object ToType(Type conversionType, IFormatProvider? provider) => this.value.ToType(conversionType, provider);

        /// <inheritdoc/>
        public ushort ToUInt16(IFormatProvider? provider) => this.value.ToUInt16(provider);

        /// <inheritdoc/>
        public uint ToUInt32(IFormatProvider? provider) => this.value.ToUInt32(provider);

        /// <inheritdoc/>
        public ulong ToUInt64(IFormatProvider? provider) => this.value.ToUInt64(provider);
    }
}
