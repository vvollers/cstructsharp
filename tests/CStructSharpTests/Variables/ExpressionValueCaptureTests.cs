namespace CStructSharp.Tests;

using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Values;

/// <summary>
///     Layout-variable capture must decide "becomes a variable in the 128-bit expression domain" versus "is removed"
///     from the value's exact mathematical meaning, without raising first-chance exceptions on the hot path.
/// </summary>
[TestClass]
public class ExpressionValueCaptureTests
{
    /// <summary>2^127, the first integer above the domain.</summary>
    private static readonly BigInteger DomainLimit = BigInteger.One << 127;

    /// <summary>An enum backed by an unsigned 64-bit integer, whose members above 2^63 must convert exactly.</summary>
    private enum UnsignedWide : ulong
    {
    }

    /// <summary>
    ///     Every primitive the codecs produce, at and around the Int32, Int64 and Int128 boundaries, plus NaN/infinity
    ///     and non-convertibles, against an independent exact oracle.
    /// </summary>
    [TestMethod]
    public void TryConvert_MatchesTheExactValue_OnEdgeValueMatrix()
    {
        object?[] values =
        [
            (byte)0, (byte)255, (sbyte)-128, (sbyte)127, (short)-32768, (short)32767, (ushort)65535, 'A', '\0', true, false,
            0, int.MinValue, int.MaxValue,
            0u, (uint)int.MaxValue, (uint)int.MaxValue + 1, uint.MaxValue,
            0L, (long)int.MinValue, (long)int.MinValue - 1, (long)int.MaxValue, (long)int.MaxValue + 1, long.MinValue, long.MaxValue,
            0UL, (ulong)int.MaxValue, (ulong)int.MaxValue + 1, (ulong)long.MaxValue + 1, ulong.MaxValue,
            Int128.MinValue, Int128.MaxValue, (UInt128)Int128.MaxValue, (UInt128)Int128.MaxValue + 1, UInt128.MaxValue,
            new BigInteger(5), (BigInteger)Int128.MinValue, (BigInteger)Int128.MinValue - 1, DomainLimit - 1, DomainLimit,
            0.0f, 0.5f, 1.5f, 2.5f, -0.5f, -1.5f, 2147483520f, 2147483648f, -2147483648f, -2147483904f, float.MaxValue, float.MinValue, float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.Epsilon,
            0.0, 0.5, 1.5, 2.5, -0.5, -1.5, -2.5, 2147483647.5, 2147483648.0, -2147483648.5, 9.2233720368547758E+18, 1.8446744073709552E+19,
            Math.ScaleB(1.0, 127), -Math.ScaleB(1.0, 127), Math.BitDecrement(Math.ScaleB(1.0, 127)), double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.Epsilon, 1e300,
            0m, 0.5m, 1.5m, 2.5m, -0.5m, -2.5m, 2147483647.5m, -2147483648.5m, decimal.MaxValue, decimal.MinValue,
            "12", "-12", "18446744073709551615", "170141183460469231731687303715884105727", "170141183460469231731687303715884105728", "notanumber", string.Empty,
            Guid.Empty, new byte[] { 1, 2 }, new object(), null, DayOfWeek.Friday, (DayOfWeek)int.MaxValue, (UnsignedWide)ulong.MaxValue,
        ];

        foreach (object? value in values)
        {
            BigInteger? expected = ExactValue(value);
            bool expectedSuccess = expected is { } exact && exact >= -DomainLimit && exact < DomainLimit;
            bool actualSuccess = ExpressionValueCapture.TryConvert(value, out Int128 actual);
            string description = value is null ? "null" : $"{value.GetType().Name}:{value}";
            Assert.AreEqual(expectedSuccess, actualSuccess, "Accept/reject decision differs for " + description);
            Assert.AreEqual(expectedSuccess ? expected!.Value : BigInteger.Zero, (BigInteger)actual, "Converted value differs for " + description);
        }
    }

    /// <summary>Parsing wide integers (and a floating-point field) raises no first-chance exceptions at all.</summary>
    [TestMethod]
    public void ParsingOutOfRangeScalars_RaisesNoFirstChanceExceptions()
    {
        var layout = new CStruct("struct rec { uint32 wide; int64 huge; float64 real; uint64 top; }; struct root { rec items[256]; };");
        byte[] bytes = new byte[256 * 28];
        for (int i = 0; i < 256; i++)
        {
            int offset = i * 28;
            BitConverter.TryWriteBytes(bytes.AsSpan(offset), 0xFFFF_FF00u + (uint)i);
            BitConverter.TryWriteBytes(bytes.AsSpan(offset + 4), long.MaxValue - i);
            BitConverter.TryWriteBytes(bytes.AsSpan(offset + 12), 1e18 + i);
            BitConverter.TryWriteBytes(bytes.AsSpan(offset + 20), ulong.MaxValue - (ulong)i);
        }

        object warm = layout.Parse(bytes, "root");
        Assert.AreEqual(256, Items(warm).Count);

        // The dynamic binder used by test-side member access can raise its own internal first-chance exceptions, so
        // only the library calls sit inside the counted region; inspection happens afterwards through plain casts.
        int exceptions = 0;
        object parsed;
        byte[] written;
        EventHandler<FirstChanceExceptionEventArgs> handler = CountOnThisThread(() => exceptions++);
        AppDomain.CurrentDomain.FirstChanceException += handler;
        try
        {
            parsed = layout.Parse(bytes, "root");
            written = layout.Serialize("root", parsed);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= handler;
        }

        Assert.AreEqual(0, exceptions, "layout-variable capture must not use exceptions for out-of-range values");

        var last = (IDictionary<string, object?>)Items(parsed)[255]!;
        Assert.AreEqual(0xFFFF_FF00u + 255, (uint)last["wide"]!);
        CollectionAssert.AreEqual(bytes, written);
    }

    /// <summary>Serializing a mapped class whose members are not integers (byte arrays, nested objects) raises no exceptions either.</summary>
    [TestMethod]
    public void SerializingNonConvertibleMembers_RaisesNoFirstChanceExceptions()
    {
        var layout = new CStruct("struct root { uint32 id; uint16 count; uint8 samples[16]; };");
        var poco = new SamplePoco { Id = 0xFFFF_FFF0u, Count = 16, Samples = Enumerable.Range(0, 16).Select(value => (byte)value).ToArray() };
        byte[] warm = layout.Serialize("root", poco);
        Assert.AreEqual(22, warm.Length);

        int exceptions = 0;
        EventHandler<FirstChanceExceptionEventArgs> handler = CountOnThisThread(() => exceptions++);
        AppDomain.CurrentDomain.FirstChanceException += handler;
        try
        {
            byte[] written = layout.Serialize("root", poco);
            CollectionAssert.AreEqual(warm, written);
        }
        finally
        {
            AppDomain.CurrentDomain.FirstChanceException -= handler;
        }

        Assert.AreEqual(0, exceptions);
    }

    /// <summary>A field value shadows an earlier definition of the same name, with its exact value, however large.</summary>
    [TestMethod]
    public void WideScalar_ShadowsEarlierVariableWithItsExactValue()
    {
        var layout = new CStruct("#define count 2\nstruct root { uint32 count; uint8 values[count]; };");
        byte[] bytes = [0xFF, 0xFF, 0xFF, 0xFF, 1, 2, 3];

        // The field's 4294967295 replaces the #define; the array length then exceeds the element limit, naming it.
        CStructReadLimitException failure = Assert.ThrowsExactly<CStructReadLimitException>(() => layout.Parse(bytes, "root"));
        StringAssert.StartsWith(failure.Message, "Array length 4294967295 exceeds MaxArrayElements");

        byte[] inRange = [3, 0, 0, 0, 1, 2, 3];
        dynamic parsed = layout.Parse(inRange, "root");
        Assert.AreEqual(3, ((IList<object?>)parsed.values).Count);
    }

    /// <summary>
    ///     The oracle: the integer a value means, computed independently of the library - integers exactly, fractions
    ///     rounded half-to-even as <see cref="Convert"/> rounds, text as an invariant integer, an enum through its
    ///     underlying value - or <see langword="null"/> when the value has no integer meaning.
    /// </summary>
    /// <param name="value">The value.</param>
    /// <returns>The exact integer, or <see langword="null"/>.</returns>
    private static BigInteger? ExactValue(object? value)
    {
        return value switch
        {
            null => BigInteger.Zero,
            bool flag => flag ? BigInteger.One : BigInteger.Zero,
            char character => new BigInteger(character),
            sbyte or byte or short or ushort or int or uint or long => new BigInteger(Convert.ToInt64(value, CultureInfo.InvariantCulture)),
            ulong unsigned => new BigInteger(unsigned),
            Int128 wide => (BigInteger)wide,
            UInt128 unsignedWide => (BigInteger)unsignedWide,
            BigInteger big => big,
            float single => Rounded(single),
            double real => Rounded(real),
            decimal fixedPoint => new BigInteger(Math.Round(fixedPoint, MidpointRounding.ToEven)),
            string text => BigInteger.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out BigInteger parsed) ? parsed : null,
            UnsignedWide member => new BigInteger((ulong)member),
            Enum member => new BigInteger(Convert.ToInt64(member, CultureInfo.InvariantCulture)),
            _ => null,
        };
    }

    /// <summary>A finite floating-point value rounded half-to-even, or <see langword="null"/> for NaN and infinity.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The rounded integer, or <see langword="null"/>.</returns>
    private static BigInteger? Rounded(double value)
        => double.IsFinite(value) ? new BigInteger(Math.Round(value, MidpointRounding.ToEven)) : null;

    /// <summary>Other tests may throw concurrently; only exceptions raised on the calling thread are counted.</summary>
    private static EventHandler<FirstChanceExceptionEventArgs> CountOnThisThread(Action increment)
    {
        int thread = Environment.CurrentManagedThreadId;
        return (_, _) =>
        {
            if (Environment.CurrentManagedThreadId == thread)
            {
                increment();
            }
        };
    }

    /// <summary>The parsed record's <c>items</c> list.</summary>
    /// <param name="parsed">A parsed record.</param>
    /// <returns>The list.</returns>
    private static IList<object?> Items(object parsed)
    {
        return (IList<object?>)((IDictionary<string, object?>)parsed)["items"]!;
    }

    /// <summary>A mapped class whose count property sizes its samples array.</summary>
    public sealed class SamplePoco : ICStructMapped<SamplePoco>
    {
        /// <summary>Gets or sets the <c>uint32 id</c> field, which may exceed <see cref="int.MaxValue"/>.</summary>
        public uint Id { get; set; }

        /// <summary>Gets or sets the <c>uint16 count</c> field.</summary>
        public ushort Count { get; set; }

        /// <summary>Gets or sets the <c>uint8 samples[16]</c> array, which no integer conversion accepts.</summary>
        public byte[] Samples { get; set; } = [];

        /// <summary>Builds the class from a parsed record.</summary>
        /// <param name="source">The parsed record.</param>
        /// <returns>The mapped value.</returns>
        public static SamplePoco ReadFrom(StructValue source)
        {
            return new SamplePoco { Id = source.Get<uint>("id"), Count = source.Get<ushort>("count"), Samples = source.Get<byte[]>("samples"), };
        }

        /// <summary>Copies the class into a record to write.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The record to fill.</param>
        public static void WriteTo(SamplePoco value, StructValue target)
        {
            target["id"] = value.Id;
            target["count"] = value.Count;
            target["samples"] = value.Samples;
        }

        /// <summary>Registers the mapping when the test assembly loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<SamplePoco>();
        }
    }
}
