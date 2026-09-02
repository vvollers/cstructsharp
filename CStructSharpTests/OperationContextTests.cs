namespace CStructSharpTests;

using System.Collections;
using System.Reflection;
using CStructSharp;

/// <summary>Verifies that one operation owns immutable choices before it invokes caller-controlled code.</summary>
[TestClass]
public class OperationContextTests
{
    /// <summary>Snapshots each read-like operation's limits before enumerating caller-owned variables.</summary>
    [TestMethod]
    public void ReadLikeOperations_SnapshotOptionsBeforeVariableEnumeration()
    {
        const string layout = "struct root { byte count; byte values[count]; };";
        var cstruct = new CStruct(layout);
        byte[] bytes = [0x01, 0x2A,];

        AssertReadSnapshot(
            options =>
            {
                using var stream = new MemoryStream(bytes);
                dynamic parsed = cstruct.ParseStream(
                    stream,
                    "root",
                    MutateDuringEnumeration(options, nameof(ReadOptions.MaxTotalBytesRead), 0L),
                    options);
                Assert.AreEqual((byte)0x2A, (byte)parsed.values[0]);
            },
            maxBytes: 2);

        AssertReadSnapshot(
            options =>
            {
                using var stream = new MemoryStream(bytes);
                (List<DebugData> debug, dynamic parsed) = cstruct.ParseStreamWithDebug(
                    stream,
                    "root",
                    MutateDuringEnumeration(options, nameof(ReadOptions.MaxTotalBytesRead), 0L),
                    options);
                Assert.IsNotEmpty(debug);
                Assert.AreEqual((byte)0x2A, (byte)parsed.root.values[0]);
            },
            maxBytes: 4);

        AssertReadSnapshot(
            options =>
            {
                using var stream = new MemoryStream(bytes);
                Assert.AreEqual(
                    (byte)0x2A,
                    cstruct.ReadValue<byte>(
                        stream,
                        "root.values[0]",
                        MutateDuringEnumeration(options, nameof(ReadOptions.MaxTotalBytesRead), 0L),
                        options));
            },
            maxBytes: 2);

        AssertReadSnapshot(
            options =>
            {
                using var stream = new MemoryStream(bytes);
                Assert.AreEqual(
                    1L,
                    cstruct.ResolveAddress(
                        stream,
                        "root.values[0]",
                        MutateDuringEnumeration(options, nameof(ReadOptions.MaxTotalBytesRead), 0L),
                        options));
            },
            maxBytes: 1);

        AssertReadSnapshot(
            options =>
            {
                using var stream = new MemoryStream(bytes);
                Assert.AreEqual(
                    1,
                    cstruct.GetDynamicArrayLength(
                        stream,
                        "root.values",
                        MutateDuringEnumeration(options, nameof(ReadOptions.MaxTotalBytesRead), 0L),
                        options));
            },
            maxBytes: 1);
    }

    /// <summary>Snapshots write limits before variable enumeration and before reading caller-owned payload members.</summary>
    [TestMethod]
    public void WriteOperation_SnapshotsOptionsBeforeCallerCallbacks()
    {
        var cstruct = new CStruct("struct root { byte values[2]; };");
        var options = new WriteOptions { MaxArrayElements = 2, };
        var payload = new MutatingPayload(
            () => SetInitProperty(options, nameof(WriteOptions.MaxArrayElements), 0),
            [0x11, 0x22,]);

        using var stream = new MemoryStream();
        cstruct.WriteStream(
            stream,
            "root",
            payload,
            MutateDuringEnumeration(options, nameof(WriteOptions.MaxArrayElements), 0),
            options);

        CollectionAssert.AreEqual(new byte[] { 0x11, 0x22, }, stream.ToArray());
    }

    /// <summary>Snapshots update traversal limits before caller variables can change the supplied option value.</summary>
    [TestMethod]
    public void UpdateOperation_SnapshotsTraversalOptionsBeforeVariableEnumeration()
    {
        var cstruct = new CStruct("struct root { byte count; byte values[count]; };");
        var options = new UpdateOptions { MaxTraversalBytesRead = 1, };
        using var stream = new MemoryStream([0x01, 0x2A,]);

        cstruct.UpdateStream(
            stream,
            "root.values[0]",
            (byte)0x5A,
            MutateDuringEnumeration(options, nameof(UpdateOptions.MaxTraversalBytesRead), 0L),
            options);

        CollectionAssert.AreEqual(new byte[] { 0x01, 0x5A, }, stream.ToArray());
        Assert.AreEqual(0L, stream.Position);
    }

    private static void AssertReadSnapshot(Action<ReadOptions> operation, long maxBytes)
    {
        operation(new ReadOptions { MaxTotalBytesRead = maxBytes, });
    }

    private static IReadOnlyDictionary<string, int> MutateDuringEnumeration(
        object target,
        string propertyName,
        object value)
    {
        return new CallbackDictionary(
            () => SetInitProperty(target, propertyName, value));
    }

    private static void SetInitProperty(object target, string propertyName, object value)
    {
        PropertyInfo property = target.GetType().GetProperty(propertyName) ??
                                throw new InvalidOperationException("Missing option property: " + propertyName);
        property.SetValue(target, value);
    }

    private sealed class CallbackDictionary : IReadOnlyDictionary<string, int>
    {
        private readonly Action callback;
        private readonly IReadOnlyDictionary<string, int> values =
            new Dictionary<string, int> { ["UNUSED"] = 1, };

        public CallbackDictionary(Action callback)
        {
            this.callback = callback;
        }

        public int Count => this.values.Count;

        public IEnumerable<string> Keys
        {
            get
            {
                this.callback();
                return this.values.Keys;
            }
        }

        public IEnumerable<int> Values => this.values.Values;

        public int this[string key] => this.values[key];

        public bool ContainsKey(string key)
        {
            return this.values.ContainsKey(key);
        }

        public IEnumerator<KeyValuePair<string, int>> GetEnumerator()
        {
            this.callback();
            return this.values.GetEnumerator();
        }

        public bool TryGetValue(string key, out int value)
        {
            return this.values.TryGetValue(key, out value);
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }

    private sealed class MutatingPayload
    {
        private readonly Action callback;
        private readonly byte[] values;

        public MutatingPayload(Action callback, byte[] values)
        {
            this.callback = callback;
            this.values = values;
        }

        public byte[] Values
        {
            get
            {
                this.callback();
                return this.values;
            }
        }
    }
}
