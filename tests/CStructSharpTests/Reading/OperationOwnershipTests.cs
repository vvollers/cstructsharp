namespace CStructSharp.Tests;

using System.Collections;
using System.Reflection;
using System.Runtime.CompilerServices;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>Verifies that one operation owns immutable choices before it invokes caller-controlled code.</summary>
[TestClass]
public class OperationOwnershipTests
{
    /// <summary>
    ///     Caller-controlled variable enumeration deliberately changes the supplied options after the operation begins.
    /// </summary>
    /// <remarks>
    ///     Reads must still use their initial limit snapshot and return the one-element array containing 0x2A. The
    ///     debug case allows its extra rereads; callbacks must not change policy halfway through an operation.
    /// </remarks>
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
                dynamic parsed = cstruct.Parse(
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
                (dynamic parsed, IReadOnlyList<DebugData> debug) = cstruct.ParseWithDebug(
                    stream,
                    "root",
                    MutateDuringEnumeration(options, nameof(ReadOptions.MaxTotalBytesRead), 0L),
                    options);
                Assert.IsNotEmpty(debug);
                Assert.AreEqual((byte)0x2A, (byte)parsed.values[0]);
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
                    cstruct.GetArrayLength(
                        stream,
                        "root.values",
                        MutateDuringEnumeration(options, nameof(ReadOptions.MaxTotalBytesRead), 0L),
                        options));
            },
            maxBytes: 1);
    }

    /// <summary>
    ///     The array limit initially permits two bytes.
    /// </summary>
    /// <remarks>
    ///     A payload callback attempts to change that limit while the writer obtains values, but the operation must
    ///     still produce 11 22 using its starting snapshot. This keeps caller code from altering an in-progress write
    ///     policy.
    /// </remarks>
    [TestMethod]
    public void WriteOperation_SnapshotsOptionsBeforeCallerCallbacks()
    {
        var cstruct = new CStruct("struct root { byte values[2]; };");
        var options = new WriteOptions { MaxArrayElements = 2, };
        var payload = new MutatingPayload(
            () => SetInitProperty(options, nameof(WriteOptions.MaxArrayElements), 0),
            [0x11, 0x22,]);

        using var stream = new MemoryStream();
        cstruct.Write(
            stream,
            "root",
            payload,
            MutateDuringEnumeration(options, nameof(WriteOptions.MaxArrayElements), 0),
            options);

        CollectionAssert.AreEqual(new byte[] { 0x11, 0x22, }, stream.ToArray());
    }

    /// <summary>
    ///     The update needs one count byte to locate values[0].
    /// </summary>
    /// <remarks>
    ///     Variable enumeration attempts to change that read allowance to zero, but the initial snapshot must remain
    ///     authoritative. The update must produce 01 5A and restore position zero.
    /// </remarks>
    [TestMethod]
    public void UpdateOperation_SnapshotsTraversalOptionsBeforeVariableEnumeration()
    {
        var cstruct = new CStruct("struct root { byte count; byte values[count]; };");
        var options = new UpdateOptions { MaxTraversalBytesRead = 1, };
        using var stream = new MemoryStream([0x01, 0x2A,]);

        cstruct.Update(
            stream,
            "root.values[0]",
            (byte)0x5A,
            MutateDuringEnumeration(options, nameof(UpdateOptions.MaxTraversalBytesRead), 0L),
            options);

        CollectionAssert.AreEqual(new byte[] { 0x01, 0x5A, }, stream.ToArray());
        Assert.AreEqual(0L, stream.Position);
    }

    /// <summary>Runs a read operation with a fresh options record limited to the given bytes.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="maxBytes">The byte limit.</param>
    private static void AssertReadSnapshot(Action<ReadOptions> operation, long maxBytes)
    {
        operation(new ReadOptions { MaxTotalBytesRead = maxBytes, });
    }

    /// <summary>Variables that change an options property while the operation enumerates them.</summary>
    /// <param name="target">The options record to change.</param>
    /// <param name="propertyName">The property to change.</param>
    /// <param name="value">The value to set.</param>
    /// <returns>The variables.</returns>
    private static IReadOnlyDictionary<string, int> MutateDuringEnumeration(
        object target,
        string propertyName,
        object value)
    {
        return new CallbackDictionary(
            () => SetInitProperty(target, propertyName, value));
    }

    /// <summary>Sets an init-only property through reflection, as a misbehaving caller could.</summary>
    /// <param name="target">The object.</param>
    /// <param name="propertyName">The property.</param>
    /// <param name="value">The value.</param>
    private static void SetInitProperty(object target, string propertyName, object value)
    {
        PropertyInfo property = target.GetType().GetProperty(propertyName) ??
                                throw new InvalidOperationException("Missing option property: " + propertyName);
        property.SetValue(target, value);
    }

    /// <summary>A read-only variable dictionary that runs a callback whenever it is enumerated.</summary>
    private sealed class CallbackDictionary : IReadOnlyDictionary<string, int>
    {
        private readonly Action callback;
        private readonly IReadOnlyDictionary<string, int> values =
            new Dictionary<string, int> { ["UNUSED"] = 1, };

        /// <summary>Creates the dictionary.</summary>
        /// <param name="callback">The code to run on enumeration.</param>
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

        /// <inheritdoc/>
        public bool ContainsKey(string key)
        {
            return this.values.ContainsKey(key);
        }

        /// <summary>Runs the callback, then enumerates the variables.</summary>
        /// <returns>The enumerator.</returns>
        public IEnumerator<KeyValuePair<string, int>> GetEnumerator()
        {
            this.callback();
            return this.values.GetEnumerator();
        }

        /// <inheritdoc/>
        public bool TryGetValue(string key, out int value)
        {
            return this.values.TryGetValue(key, out value);
        }

        /// <inheritdoc/>
        IEnumerator IEnumerable.GetEnumerator()
        {
            return this.GetEnumerator();
        }
    }

    /// <summary>A mapped payload whose mapper runs caller code while a write is in flight.</summary>
    internal sealed class MutatingPayload : ICStructMapped<MutatingPayload>
    {
        private readonly Action callback;
        private readonly byte[] values;

        /// <summary>Creates the payload.</summary>
        /// <param name="callback">The code the mapper runs while the write is in flight.</param>
        /// <param name="values">The values to write.</param>
        public MutatingPayload(Action callback, byte[] values)
        {
            this.callback = callback;
            this.values = values;
        }

        /// <summary>Builds the class from a parsed record.</summary>
        /// <param name="source">The parsed record.</param>
        /// <returns>The mapped value.</returns>
        public static MutatingPayload ReadFrom(StructValue source)
        {
            return new MutatingPayload(() => { }, source.Get<byte[]>("values"));
        }

        /// <summary>The mapper runs caller code while the write is in flight; the options must already be captured.</summary>
        public static void WriteTo(MutatingPayload value, StructValue target)
        {
            value.callback();
            target["values"] = value.values;
        }

        /// <summary>Registers the mapping when the test assembly loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<MutatingPayload>();
        }
    }
}
