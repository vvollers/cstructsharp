namespace CStructSharp.Tests;

using System.Globalization;
using CStructSharp.Diagnostics;

/// <summary>Checks that integer write failures reject narrowing and identify the affected field and type.</summary>
[TestClass]
public class IntegerWriteDiagnosticTests
{
    /// <summary>Every fixed-width integer rejects values just outside either endpoint without silently narrowing them.</summary>
    /// <param name="type">The layout spelling under test.</param>
    /// <param name="minimum">The smallest representable integer, in invariant decimal notation.</param>
    /// <param name="maximum">The largest representable integer, in invariant decimal notation.</param>
    [TestMethod]
    [DataRow("uint8", "0", "255")]
    [DataRow("int8", "-128", "127")]
    [DataRow("uint16", "0", "65535")]
    [DataRow("int16", "-32768", "32767")]
    [DataRow("uint24", "0", "16777215")]
    [DataRow("int24", "-8388608", "8388607")]
    [DataRow("uint32", "0", "4294967295")]
    [DataRow("int32", "-2147483648", "2147483647")]
    [DataRow("uint48", "0", "281474976710655")]
    [DataRow("int48", "-140737488355328", "140737488355327")]
    [DataRow("uint64", "0", "18446744073709551615")]
    [DataRow("int64", "-9223372036854775808", "9223372036854775807")]
    public void OutOfRangeWrite_RejectsNarrowingAndReportsTheField(string type, string minimum, string maximum)
    {
        var layout = new CStruct($"struct root {{ {type} value; }};");
        decimal low = decimal.Parse(minimum, CultureInfo.InvariantCulture);
        decimal high = decimal.Parse(maximum, CultureInfo.InvariantCulture);
        foreach (decimal value in new[] { low - 1, high + 1 })
        {
            // The public writer must reject the supplied number, not silently truncate an intermediate conversion.
            CStructWriteException failure = Assert.Throws<CStructWriteException>(
                () => layout.Serialize("root", new Dictionary<string, object?> { ["value"] = value }));
            Assert.AreEqual(CStructErrorCode.WriteFailed, failure.Code);
            StringAssert.Contains(failure.Message, $"field 'value' ({type})");
            bool narrowSigned = type is "int24" or "int48";
            bool narrowUnsigned = type is "uint24" or "uint48";
            if (narrowSigned || (narrowUnsigned && value >= 0))
            {
                // The narrow codecs have their own range error after the CLR integer conversion succeeds.
                StringAssert.Contains(failure.Message, $"outside the {type} range");
            }
            else
            {
                StringAssert.Contains(failure.Message, value.ToString(CultureInfo.InvariantCulture));
                StringAssert.Contains(failure.Message, $"{type} accepts {minimum} to {maximum}");
            }
        }

        // A non-numeric value takes the conversion-failure path for every codec, including 24- and 48-bit types.
        CStructWriteException invalidType = Assert.Throws<CStructWriteException>(
            () => layout.Serialize("root", new Dictionary<string, object?> { ["value"] = new object() }));
        StringAssert.Contains(invalidType.Message, $"Value Object cannot be written as {type}");
    }
}
