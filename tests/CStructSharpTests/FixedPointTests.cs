namespace CStructSharpTests;

using CStructSharp;

/// <summary>Checks fixed-point exactness, storage byte order and writer rejection.</summary>
[TestClass]
public class FixedPointTests
{
    /// <summary>An integer writer input does not make fixed-point storage a legal array count.</summary>
    [TestMethod]
    public void IntegerInput_StillMasksFixedPointLayoutVariables()
    {
        var layout = new CStruct("struct root { fixed16_16 count; uint8 values[count]; };", aligned: false);
        Assert.Throws<CStructException>(() => layout.Serialize("root", new { count = 1, values = new byte[] { 42 } }));
    }

    /// <summary>A decimal fraction must not disappear through a preliminary Double conversion.</summary>
    [TestMethod]
    public void DecimalInputs_RejectHiddenRounding()
    {
        foreach (string type in new[] { "fixed16_16", "ufixed16_16", "fixed2_30", "ufixed8_8" })
        {
            var parser = new CStruct($"struct root {{ {type} value; }};", aligned: false);
            byte[] bytes = parser.Serialize("root", new { value = 1m });
            CollectionAssert.AreEqual(parser.Serialize("root", new { value = 1.0 }), bytes);
            using var stream = new MemoryStream(bytes);
            Assert.Throws<CStructWriteException>(() => parser.UpdateStream(stream, "root.value", 1.0000000000000000000000000001m));
            CollectionAssert.AreEqual(bytes, stream.ToArray());
        }
    }

    /// <summary>Every supported scale round-trips exact binary fractions in both byte orders.</summary>
    [TestMethod]
    public void ExactValues_RoundTrip()
    {
        foreach ((string type, double value, int size) in new[]
                 {
                     ("fixed16_16", -1.5, 4), ("ufixed16_16", 65535.5, 4),
                     ("fixed2_30", -1.25, 4), ("ufixed8_8", 255.5, 2),
                 })
        {
            byte[]? previous = null;
            foreach (string suffix in new[] { "<", ">" })
            {
                var parser = new CStruct($"struct root {{ {type}{suffix} value; uint8 tail; }};", aligned: false);
                byte[] bytes = parser.Serialize("root", new { value, tail = 99 });
                Assert.AreEqual(size + 1, bytes.Length);
                using var stream = new MemoryStream(bytes);
                Assert.AreEqual(value, parser.ReadValue<double>(stream, "root.value"));
                stream.Position = 0;
                Assert.AreEqual((long)size, parser.ResolveAddress(stream, "root.tail"));
                if (previous is not null)
                {
                    CollectionAssert.AreEqual(previous.Take(size).Reverse().ToArray(), bytes[..size]);
                }

                previous = bytes;
                foreach (double invalid in new[] { double.NaN, double.PositiveInfinity, 0.1, double.MaxValue })
                {
                    stream.Position = 0;
                    Assert.Throws<CStructWriteException>(() => parser.UpdateStream(stream, "root.value", invalid));
                    CollectionAssert.AreEqual(bytes, stream.ToArray());
                }
            }
        }
    }
}
