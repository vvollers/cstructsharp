namespace CStructSharp.Tests;

using System.Buffers.Binary;
using System.Globalization;

/// <summary>
///     Verifies that converting a caller value to a numeric field uses the invariant culture, so the bytes written
///     never depend on the culture of the thread that writes them.
/// </summary>
[TestClass]
public class CultureInvariantConversionTests
{
    /// <summary>A decimal string is read with a period as the decimal separator even under a comma-decimal culture.</summary>
    [TestMethod]
    public void DecimalStrings_UseTheInvariantSeparator_UnderAGermanCulture()
    {
        var layout = new CStruct("struct r { float64 d; float32 f; float64> big; uint16 n; };");
        var values = new Dictionary<string, object?> { ["d"] = "1.5", ["f"] = "2.25", ["big"] = "-0.5", ["n"] = "1000", };

        byte[] bytes = WithCulture("de-DE", () => layout.Serialize("r", values));

        Assert.AreEqual(1.5, BitConverter.ToDouble(bytes, 0));
        Assert.AreEqual(2.25f, BitConverter.ToSingle(bytes, 8));
        Assert.AreEqual(-0.5, BinaryPrimitives.ReadDoubleBigEndian(bytes.AsSpan(12, 8)));
        Assert.AreEqual((ushort)1000, BitConverter.ToUInt16(bytes, 20));
    }

    /// <summary>An update writes the same bytes as a serialization under any culture.</summary>
    [TestMethod]
    public void Update_UsesTheInvariantCulture()
    {
        var layout = new CStruct("struct r { float64 d; };");
        byte[] bytes = new byte[8];

        WithCulture("de-DE", () =>
        {
            layout.Update(bytes.AsSpan(), "r.d", "3.75");
            return 0;
        });

        Assert.AreEqual(3.75, BitConverter.ToDouble(bytes, 0));
    }

    /// <summary>Runs <paramref name="action"/> with the current culture set to <paramref name="culture"/>.</summary>
    /// <typeparam name="T">The result type.</typeparam>
    /// <param name="culture">The culture name.</param>
    /// <param name="action">The work to run.</param>
    /// <returns>The work's result.</returns>
    private static T WithCulture<T>(string culture, Func<T> action)
    {
        CultureInfo previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = new CultureInfo(culture);
        try
        {
            return action();
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
