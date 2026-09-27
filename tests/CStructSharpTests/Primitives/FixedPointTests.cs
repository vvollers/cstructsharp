namespace CStructSharp.Tests;

using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>Checks fixed-point exactness, storage byte order and writer rejection.</summary>
[TestClass]
public class FixedPointTests
{
    /// <summary>Fixed-point storage is never an array count, whatever a writer is given for it.</summary>
    [TestMethod]
    public void FixedPointCount_FailsConstruction()
    {
        CStructLayoutException failure = Assert.ThrowsExactly<CStructLayoutException>(() => new CStruct("struct root { fixed16_16 count; uint8 values[count]; };"));
        StringAssert.Contains(failure.Message, "Field 'count' is a fixed-point value");
    }

    /// <summary>A decimal fraction must not disappear through a preliminary Double conversion.</summary>
    [TestMethod]
    public void DecimalInputs_RejectHiddenRounding()
    {
        foreach (string type in new[] { "fixed16_16", "ufixed16_16", "fixed2_30", "ufixed8_8" })
        {
            var parser = new CStruct($"struct root {{ {type} value; }};", aligned: false);
            byte[] bytes = parser.Serialize("root", new Dictionary<string, object?> { ["value"] = 1m });
            CollectionAssert.AreEqual(parser.Serialize("root", new Dictionary<string, object?> { ["value"] = 1.0 }), bytes);
            using var stream = new MemoryStream(bytes);
            Assert.Throws<CStructWriteException>(() => parser.Update(stream, "root.value", 1.0000000000000000000000000001m));
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
                byte[] bytes = parser.Serialize("root", new Dictionary<string, object?> { ["value"] = value, ["tail"] = 99 });
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
                    Assert.Throws<CStructWriteException>(() => parser.Update(stream, "root.value", invalid));
                    CollectionAssert.AreEqual(bytes, stream.ToArray());
                }
            }
        }
    }
}
