namespace CStructSharp.Tests;

using System.Buffers;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using CStructSharp;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Syntax;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
///     Pins that a field's value is published as a layout variable exactly when an expression can name it - so skipping
///     the capture for every other field, even a custom value no expression reads, never changes an observable result -
///     and that every operation takes caller variables through a read-only view it does not change.
/// </summary>
[TestClass]
public class LayoutVariableCaptureTests
{
    /// <summary>Every runtime expression site - array count, condition, switch selector - still sees the field it names; compile-time sites (case labels, bit widths, alignment and offset assertions, enum values, defines) cannot name fields at all.</summary>
    [TestMethod]
    public void EveryExpressionSite_ReadsItsReferencedField()
    {
        var cstruct = new CStruct("""
            enum kind : uint8 { small = 1 };
            struct root {
                uint8 base;
                uint8 lo;
                uint8 n;
                uint8 items[n];
                uint8 flag;
                if (flag == 1) { uint8 yes; } else { uint8 no; }
                uint8 sel;
                switch (sel) { case 1: { uint8 a; } default: { uint8 b; } }
                uint8 mark @10;
                uint8 bits : 3;
                uint8 tail;
            };
            """);
        byte[] bytes = [2, 1, 3, 0xA, 0xB, 0xC, 1, 0x11, 1, 0x22, 0x33, 0x05, 0x44];
        dynamic parsed = cstruct.Parse(bytes, "root");
        Assert.AreEqual(3, ((IList<object?>)parsed.items).Count);
        Assert.AreEqual((byte)0x11, parsed.yes);
        Assert.AreEqual((byte)0x22, parsed.a);
        Assert.AreEqual((byte)0x33, parsed.mark);
        Assert.AreEqual((byte)5, parsed.bits);
        Assert.AreEqual((byte)0x44, parsed.tail);
        Assert.AreEqual(3, cstruct.GetArrayLength(new MemoryStream(bytes), "root.items"));
        Assert.AreEqual(10L, cstruct.ResolveAddress(new MemoryStream(bytes), "root.mark"));
        foreach (string referenced in new[] { "n", "flag", "sel" })
        {
            Assert.IsTrue(CapturesVariable(cstruct, referenced), referenced);
        }

        foreach (string unreferenced in new[] { "base", "lo", "items", "yes", "no", "a", "b", "mark", "bits", "tail" })
        {
            Assert.IsFalse(CapturesVariable(cstruct, unreferenced), unreferenced);
        }
    }

    /// <summary>Only the fields an expression names are captured.</summary>
    [TestMethod]
    public void CompiledFields_CaptureOnlyWhenReferenced()
    {
        var plain = new CStruct("struct root { uint8 n; uint8 items[n]; uint16 unused; char name[4]; };");
        Assert.IsTrue(CapturesVariable(plain, "n"));
        Assert.IsFalse(CapturesVariable(plain, "unused"));
        Assert.IsFalse(CapturesVariable(plain, "items"));
        Assert.IsFalse(CapturesVariable(plain, "name"));
    }

    /// <summary>
    ///     Supplied variables cannot name a field: integers are literals, and an expression naming a field is rejected
    ///     when the operation starts, so the capture-all fallback is a safety net rather than a reachable path. The
    ///     flag still survives the copies operations make of their variables.
    /// </summary>
    [TestMethod]
    public void SuppliedVariables_CannotReferenceFields_AndTheFallbackFlagPropagates()
    {
        var cstruct = new CStruct("struct root { uint8 unused; uint8 n; uint8 items[n]; };");
        LayoutVariableResolver resolver = cstruct.Compilation.LayoutVariableResolver;
        Assert.IsFalse(LayoutVariableInput.FromIntegers(new Dictionary<string, int> { ["n"] = 1 }).Resolve(resolver) is LayoutVariables { CaptureAll: true });
        Assert.ThrowsExactly<CStructLayoutException>(() => LayoutVariableInput.FromExpressions(
            new Dictionary<string, Expr> { ["n"] = new BinaryOp(BinaryOperatorType.Add, new Identifier("unused"), new Literal(1)) }).Resolve(resolver));

        var flagged = new LayoutVariables { CaptureAll = true };
        Assert.IsTrue(new LayoutVariables(flagged).CaptureAll);
        Assert.IsFalse(new LayoutVariables(new Dictionary<string, Expr>()).CaptureAll);
    }

    /// <summary>
    ///     The read-only dictionary overrides COUNT from 1 to 2, so every operation must treat values as two uint16
    ///     elements.
    /// </summary>
    /// <remarks>
    ///     Reads, queries, serialization, and updates must agree while leaving the caller's dictionary unchanged.
    ///     External variables are input context, not scratch storage for the parser.
    /// </remarks>
    [TestMethod]
    public void PublicOperations_AcceptReadOnlyVariablesAndPreserveCallerState()
    {
        const string layout = "#define COUNT 1\nstruct root { byte prefix; uint16 values[COUNT]; };";
        var cstruct = new CStruct(layout);
        const int count = 2;
        var source = new Dictionary<string, int> { ["COUNT"] = count, };
        IReadOnlyDictionary<string, int> variables =
            new ReadOnlyDictionary<string, int>(source);
        byte[] original = [0xA5, 0x34, 0x12, 0x78, 0x56,];

        using (var stream = new MemoryStream(original))
        {
            dynamic parsed = cstruct.Parse(stream, "root", variables);
            Assert.AreEqual(2, ((IList<object>)parsed.values).Count);
        }

        using (var stream = new MemoryStream(original))
        {
            dynamic parsed = cstruct.Parse(stream, "root", variables, new ReadOptions());
            Assert.AreEqual(0x5678, Convert.ToInt32(((IList<object>)parsed.values)[1]));
        }

        using (var stream = new MemoryStream(original))
        {
            Assert.AreEqual(
                (ushort)0x5678,
                cstruct.ReadValue<ushort>(
                    stream,
                    "root.values[1]",
                    variables,
                    new ReadOptions()));
        }

        using (var stream = new MemoryStream(original))
        {
            Assert.IsTrue(
                cstruct.TryReadValue(
                    stream,
                    "root.values[1]",
                    out ushort value,
                    variables,
                    new ReadOptions()));
            Assert.AreEqual((ushort)0x5678, value);
        }

        using (var stream = new MemoryStream(original))
        {
            (dynamic parsed, IReadOnlyList<DebugData> debug) =
                cstruct.ParseWithDebug(stream, "root", variables);
            Assert.IsNotEmpty(debug);
            Assert.IsNotNull(parsed);
        }

        using (var stream = new MemoryStream(original))
        {
            (dynamic parsed, IReadOnlyList<DebugData> debug) =
                cstruct.ParseWithDebug(stream, "root", variables, new ReadOptions());
            Assert.IsNotEmpty(debug);
            Assert.IsNotNull(parsed);
        }

        using (var stream = new MemoryStream(original))
        {
            Assert.AreEqual(
                2,
                cstruct.GetArrayLength(stream, "root.values", variables, new ReadOptions()));
            Assert.AreEqual(0L, stream.Position);
        }

        using (var stream = new MemoryStream(original))
        {
            Assert.AreEqual(
                3L,
                cstruct.ResolveAddress(stream, "root.values[1]", variables, new ReadOptions()));
            Assert.AreEqual(0L, stream.Position);
        }

        var data = new Dictionary<string, object>
        {
            ["prefix"] = (byte)0xA5,
            ["values"] = new object[] { (ushort)0x1234, (ushort)0x5678, },
        };
        CollectionAssert.AreEqual(
            original,
            cstruct.Serialize("root", data, variables, new WriteOptions()));

        using (var stream = new MemoryStream())
        {
            cstruct.Write(stream, "root", data, variables, new WriteOptions());
            CollectionAssert.AreEqual(original, stream.ToArray());
        }

        using (var stream = new MemoryStream(original))
        {
            cstruct.Update(
                stream,
                "root.values[1]",
                (ushort)0xBEEF,
                variables,
                new UpdateOptions());
            CollectionAssert.AreEqual(
                new byte[] { 0xA5, 0x34, 0x12, 0xEF, 0xBE, },
                stream.ToArray());
            Assert.AreEqual(0L, stream.Position);
        }

        Assert.AreEqual(1, source.Count);
        Assert.AreEqual(count, source["COUNT"]);
    }

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

    /// <summary>Returns whether the uniquely named field records its value as a layout variable.</summary>
    private static bool CapturesVariable(CStruct cstruct, string fieldName)
    {
        CompiledField field = cstruct.CompiledModel.AllFields().Single(candidate => candidate.Declaration.Name.Name == fieldName);
        return field.CapturesLayoutVariable;
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
