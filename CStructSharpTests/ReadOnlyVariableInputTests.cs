namespace CStructSharpTests;

using System.Collections.ObjectModel;
using System.Dynamic;
using CStructSharp;

/// <summary>Verifies that every variable-bearing operation accepts an immutable dictionary view.</summary>
[TestClass]
public class ReadOnlyVariableInputTests
{
    /// <summary>Applies one read-only override consistently without mutating the caller-owned dictionary.</summary>
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
            dynamic parsed = cstruct.ParseStream(stream, "root", variables);
            Assert.AreEqual(2, ((IList<object>)parsed.values).Count);
        }

        using (var stream = new MemoryStream(original))
        {
            dynamic parsed = cstruct.ParseStream(stream, "root", variables, new ReadOptions());
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
            (List<DebugData> debug, dynamic parsed) =
                cstruct.ParseStreamWithDebug(stream, "root", variables);
            Assert.IsNotEmpty(debug);
            Assert.IsNotNull(parsed);
        }

        using (var stream = new MemoryStream(original))
        {
            (List<DebugData> debug, dynamic parsed) =
                cstruct.ParseStreamWithDebug(stream, "root", variables, new ReadOptions());
            Assert.IsNotEmpty(debug);
            Assert.IsNotNull(parsed);
        }

        using (var stream = new MemoryStream(original))
        {
            Assert.AreEqual(
                2,
                cstruct.GetDynamicArrayLength(stream, "root.values", variables, new ReadOptions()));
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
            cstruct.WriteStream(stream, "root", data, variables, new WriteOptions());
            CollectionAssert.AreEqual(original, stream.ToArray());
        }

        using (var stream = new MemoryStream(original))
        {
            cstruct.UpdateStream(
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
}
