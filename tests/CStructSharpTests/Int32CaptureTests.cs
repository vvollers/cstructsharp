namespace CStructSharpTests;

using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Runtime.ExceptionServices;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;

/// <summary>
///     Layout-variable capture must decide "becomes an Int32 variable" versus "is removed" exactly as
///     <see cref="Convert.ToInt32(object)"/> did, but without raising first-chance exceptions on the hot path.
/// </summary>
[TestClass]
public class Int32CaptureTests
{
    /// <summary>Every primitive the codecs produce, at and around the Int32 boundaries, plus NaN/infinity and non-convertibles.</summary>
    [TestMethod]
    public void TryConvert_MatchesConvertToInt32_OnEdgeValueMatrix()
    {
        object?[] values =
        [
            (byte)0, (byte)255, (sbyte)-128, (sbyte)127, (short)-32768, (short)32767, (ushort)65535, 'A', '\0', true, false,
            0, int.MinValue, int.MaxValue,
            0u, (uint)int.MaxValue, (uint)int.MaxValue + 1, uint.MaxValue,
            0L, (long)int.MinValue, (long)int.MinValue - 1, (long)int.MaxValue, (long)int.MaxValue + 1, long.MinValue, long.MaxValue,
            0UL, (ulong)int.MaxValue, (ulong)int.MaxValue + 1, ulong.MaxValue,
            0.0f, 0.5f, 1.5f, 2.5f, -0.5f, -1.5f, 2147483520f, 2147483648f, -2147483648f, -2147483904f, float.NaN, float.PositiveInfinity, float.NegativeInfinity, float.Epsilon,
            0.0, 0.5, 1.5, 2.5, -0.5, -1.5, -2.5, 2147483647.0, 2147483647.4999998, 2147483647.5, 2147483648.0, -2147483648.0, -2147483648.5, -2147483648.5000002, -2147483649.0, double.NaN, double.PositiveInfinity, double.NegativeInfinity, double.Epsilon, 1e300,
            0m, 0.5m, 1.5m, 2.5m, -0.5m, -2.5m, 2147483647m, 2147483647.5m, 2147483647.4m, -2147483648m, -2147483648.5m, -2147483649m, decimal.MaxValue, decimal.MinValue,
            "12", "notanumber", string.Empty, new BigInteger(5), Guid.Empty, new byte[] { 1, 2 }, new object(), null, DayOfWeek.Friday, (DayOfWeek)int.MaxValue,
        ];

        foreach (object? value in values)
        {
            bool expectedSuccess;
            int expected = 0;
            try
            {
                expected = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                expectedSuccess = true;
            }
            catch (Exception exception) when (exception is OverflowException or InvalidCastException or FormatException)
            {
                expectedSuccess = false;
            }

            bool actualSuccess = Int32Capture.TryConvert(value, out int actual);
            string description = value is null ? "null" : $"{value.GetType().Name}:{value}";
            Assert.AreEqual(expectedSuccess, actualSuccess, "Accept/reject decision differs for " + description);
            if (expectedSuccess)
            {
                Assert.AreEqual(expected, actual, "Converted value differs for " + description);
            }
        }
    }

    /// <summary>Parsing wide integers whose values exceed Int32 raises no first-chance exceptions at all.</summary>
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

    /// <summary>Serializing a POCO whose members are not Int32-convertible (byte arrays, nested objects) raises no exceptions either.</summary>
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

    /// <summary>An out-of-range scalar still shadows an earlier definition of the same name (removal semantics retained).</summary>
    [TestMethod]
    public void OutOfRangeScalar_StillShadowsEarlierVariable()
    {
        var layout = new CStruct("#define count 2\nstruct root { uint32 count; uint8 values[count]; };");
        byte[] bytes = [0xFF, 0xFF, 0xFF, 0xFF, 1, 2, 3];

        // The out-of-range value removes the #define, so the array length has no usable variable any more.
        CStructReadException failure = Assert.ThrowsExactly<CStructReadException>(() => layout.Parse(bytes, "root"));
        StringAssert.Contains(failure.Message, "count");

        byte[] inRange = [3, 0, 0, 0, 1, 2, 3];
        dynamic parsed = layout.Parse(inRange, "root");
        Assert.AreEqual(3, ((IList<object?>)parsed.values).Count);
    }

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

    private static IList<object?> Items(object parsed)
    {
        return (IList<object?>)((IDictionary<string, object?>)parsed)["items"]!;
    }

    public sealed class SamplePoco
    {
        public uint Id { get; set; }

        public ushort Count { get; set; }

        public byte[] Samples { get; set; } = [];
    }
}
