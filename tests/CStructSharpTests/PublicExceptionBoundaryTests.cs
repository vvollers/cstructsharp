namespace CStructSharp.Tests;

using System.Buffers;
using System.Dynamic;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;

/// <summary>Defines the public CLR failure categories shared by metadata, read, path, write, and update operations.</summary>
[TestClass]
public class PublicExceptionBoundaryTests
{
    private const string Layout = """
                                  typedef byte alias;
                                  struct child { byte value; };
                                  struct root {
                                      byte scalar;
                                      byte values[2];
                                      child nested;
                                      child * link;
                                  };
                                  """;

    /// <summary>
    ///     An unknown field type is invalid layout text and must raise CStructLayoutException.
    /// </summary>
    /// <remarks>
    ///     Null input, pointer width three, or zero compilation limits are invalid API arguments and must retain
    ///     argument exceptions. Distinguishing these causes helps callers fix the definition versus fixing how they
    ///     called the library.
    /// </remarks>
    [TestMethod]
    public void Compilation_SeparatesLayoutFailuresFromArgumentFailures()
    {
        Assert.Throws<CStructLayoutException>(() => new CStruct("struct root { missing value; };"));
        Assert.Throws<ArgumentNullException>(() => new CStruct(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CStruct(Layout, pointerSize: 3));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new CStruct(
                Layout,
                compilationOptions: new CStructCompilationOptions { MaxDefinitionLength = 0, }));
    }

    /// <summary>
    ///     Metadata queries receive empty, missing, or wrong-kind declaration names.
    /// </summary>
    /// <remarks>
    ///     These semantic selection errors must raise CStructPathException, while a null argument remains
    ///     ArgumentNullException. A name can exist and still be unsuitable when an API specifically requires a struct.
    /// </remarks>
    [TestMethod]
    public void MetadataQueries_UsePathFailuresForInvalidSelectors()
    {
        var cstruct = CreateLayout();

        Assert.Throws<ArgumentNullException>(() => cstruct.GetStruct(null!));
        Assert.Throws<CStructPathException>(() => cstruct.GetStruct(string.Empty));
        Assert.Throws<CStructPathException>(() => cstruct.GetStruct("missing"));
        Assert.Throws<CStructPathException>(() => cstruct.GetStruct("alias"));
        Assert.Throws<CStructPathException>(() => cstruct.GetStructSizeInBytes("missing"));
        Assert.Throws<CStructPathException>(() => cstruct.GetStructAlignmentInBytes("alias"));
    }

    /// <summary>
    ///     Empty paths, missing fields, and traversing through an ordinary scalar must fail consistently across
    ///     reading, debug, address, and length APIs.
    /// </summary>
    /// <remarks>
    ///     TryReadValue must return false and restore position for these expected failures. A selector error must not
    ///     be confused with damaged binary data.
    /// </remarks>
    [TestMethod]
    public void ReadLikeOperations_UsePathFailuresForInvalidSelectors()
    {
        var cstruct = CreateLayout();

        foreach (string path in new[] { string.Empty, "missing", "root.missing", "root.scalar.value", })
        {
            Assert.Throws<CStructPathException>(
                () => cstruct.Parse(new MemoryStream(new byte[8]), path),
                "parse/" + path);
            Assert.Throws<CStructPathException>(
                () => cstruct.ParseWithDebug(new MemoryStream(new byte[8]), path),
                "debug/" + path);
            Assert.Throws<CStructPathException>(
                () => cstruct.ResolveAddress(new MemoryStream(new byte[8]), path),
                "address/" + path);
            Assert.Throws<CStructPathException>(
                () => cstruct.GetArrayLength(new MemoryStream(new byte[8]), path),
                "length/" + path);
            Assert.Throws<CStructPathException>(
                () => cstruct.ReadValue(new MemoryStream(new byte[8]), path),
                "read-value/" + path);
            using var probe = new MemoryStream(new byte[8]);
            Assert.IsFalse(cstruct.TryReadValue<int>(probe, path, out _), "try-read-value/" + path);
            Assert.AreEqual(0L, probe.Position, "try-read-value-position/" + path);
        }

        Assert.Throws<CStructPathException>(
            () => cstruct.GetArrayLength(new MemoryStream(new byte[8]), "root.scalar"));
    }

    /// <summary>
    ///     Invalid output selectors must raise a path error before a direct write creates bytes or an update changes
    ///     existing storage.
    /// </summary>
    /// <remarks>
    ///     Failed updates must also restore their original position. Valid replacement data cannot compensate for
    ///     selecting a nonexistent or invalid target.
    /// </remarks>
    [TestMethod]
    public void WriteLikeOperations_UsePathFailuresWithoutMutation()
    {
        var cstruct = CreateLayout();
        ExpandoObject value = CreateValue();

        foreach (string path in new[] { string.Empty, "missing", "root.missing", })
        {
            using var direct = new MemoryStream();
            Assert.Throws<CStructPathException>(() => cstruct.Write(direct, path, value), "write/" + path);
            Assert.AreEqual(0L, direct.Length, "write-length/" + path);

            byte[] original = new byte[8];
            using var update = new MemoryStream((byte[])original.Clone()) { Position = 3, };
            Assert.Throws<CStructPathException>(() => cstruct.Update(update, path, (byte)1), "update/" + path);
            CollectionAssert.AreEqual(original, update.ToArray(), "update-bytes/" + path);
            Assert.AreEqual(3L, update.Position, "update-position/" + path);
        }

        using var pointerUpdate = new MemoryStream(new byte[8]) { Position = 2, };
        Assert.Throws<CStructPathException>(
            () => cstruct.Update(
                pointerUpdate,
                "root.link.value.value",
                (byte)1,
                options: new UpdateOptions { DereferencePointers = false, }));
        Assert.AreEqual(2L, pointerUpdate.Position);
    }

    /// <summary>
    ///     Null streams, unsupported read/write/seek capabilities, and invalid option values violate the API's calling
    ///     contract.
    /// </summary>
    /// <remarks>
    ///     They must raise ordinary argument exceptions across all operation families. These failures should not be
    ///     presented as corrupt payloads or layout interpretation errors.
    /// </remarks>
    [TestMethod]
    public void Operations_RetainArgumentFailuresForArgumentsOptionsAndCapabilities()
    {
        var cstruct = CreateLayout();
        ExpandoObject value = CreateValue();

        Assert.Throws<ArgumentNullException>(() => cstruct.Parse((Stream)null!, "root"));
        Assert.Throws<ArgumentNullException>(() => cstruct.ParseWithDebug((Stream)null!, "root"));
        Assert.Throws<ArgumentNullException>(() => cstruct.ResolveAddress((Stream)null!, "root.scalar"));
        Assert.Throws<ArgumentNullException>(() => cstruct.GetArrayLength((Stream)null!, "root.values"));
        Assert.Throws<ArgumentNullException>(() => cstruct.ReadValue((Stream)null!, "root.scalar"));
        Assert.Throws<ArgumentNullException>(() => cstruct.TryReadValue<int>((Stream)null!, "root.scalar", out _));
        Assert.Throws<ArgumentNullException>(() => cstruct.Write(null!, "root", value));
        Assert.Throws<ArgumentNullException>(() => cstruct.Update((Stream)null!, "root.scalar", (byte)1));
        Assert.Throws<ArgumentNullException>(
            () => cstruct.Serialize((IBufferWriter<byte>)null!, "root", value));
        Assert.Throws<ArgumentException>(() => cstruct.Parse(new WriteOnlySeekableStream(), "root"));
        Assert.Throws<ArgumentException>(() => cstruct.ParseWithDebug(new NonSeekableReadStream(), "root"));
        Assert.Throws<ArgumentException>(() => cstruct.ResolveAddress(new NonSeekableReadStream(), "root.scalar"));
        Assert.Throws<ArgumentException>(() => cstruct.GetArrayLength(new NonSeekableReadStream(), "root.values"));
        Assert.Throws<ArgumentException>(() => cstruct.ReadValue(new NonSeekableReadStream(), "root.scalar"));
        Assert.Throws<ArgumentException>(
            () => cstruct.TryReadValue<int>(new NonSeekableReadStream(), "root.scalar", out _));
        Assert.Throws<ArgumentException>(() => cstruct.Write(new NonSeekableReadStream(), "root", value));
        Assert.Throws<ArgumentException>(() => cstruct.Update(new NonSeekableReadStream(), "root.scalar", (byte)1));
        Assert.Throws<ArgumentException>(
            () => cstruct.Update(
                new CapabilityStream(canRead: false, canSeek: true, canWrite: true),
                "root.scalar",
                (byte)1));
        Assert.Throws<ArgumentException>(
            () => cstruct.Update(
                new CapabilityStream(canRead: true, canSeek: false, canWrite: true),
                "root.scalar",
                (byte)1));
        Assert.Throws<ArgumentException>(
            () => cstruct.Update(
                new CapabilityStream(canRead: true, canSeek: true, canWrite: false),
                "root.scalar",
                (byte)1));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => cstruct.Parse(
                new MemoryStream(new byte[8]),
                "root",
                new Dictionary<string, Expr>(),
                new ReadOptions { MaxArrayElements = -1, }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => cstruct.ReadValue(
                new MemoryStream(new byte[8]),
                "root",
                options: new ReadOptions { MaxArrayElements = -1, }));
        Assert.Throws<ArgumentOutOfRangeException>(
            () => cstruct.Write(
                new MemoryStream(),
                "root",
                value,
                options: new WriteOptions { MaxArrayElements = -1, }));
    }

    /// <summary>
    ///     Fake streams fail during reading, writing, or seeking.
    /// </summary>
    /// <remarks>
    ///     Public APIs must wrap IOException in the appropriate read or write exception while retaining the original
    ///     instance and any available offset. This allows applications to diagnose storage failures separately from
    ///     invalid binary values.
    /// </remarks>
    [TestMethod]
    public void PhysicalStreamFailures_AreWrappedWithTheirOriginalCause()
    {
        var cstruct = CreateLayout();
        ExpandoObject value = CreateValue();

        var readCause = new IOException("injected read");
        var read = Assert.Throws<CStructReadException>(
            () => cstruct.Parse(new FaultingStream(readException: readCause), "root"));
        Assert.AreSame(readCause, read.InnerException);
        Assert.AreEqual(0L, read.Offset);

        var debugCause = new IOException("injected debug read");
        var debug = Assert.Throws<CStructReadException>(
            () => cstruct.ParseWithDebug(new FaultingStream(readException: debugCause), "root"));
        Assert.AreSame(debugCause, debug.InnerException);

        var valueCause = new IOException("injected value read");
        var valueRead = Assert.Throws<CStructReadException>(
            () => cstruct.ReadValue(new FaultingStream(readException: valueCause), "root.scalar"));
        Assert.AreSame(valueCause, valueRead.InnerException);
        Assert.AreEqual(0L, valueRead.Offset);

        var writeCause = new IOException("injected write");
        var write = Assert.Throws<CStructWriteException>(
            () => cstruct.Write(new FaultingStream(writeException: writeCause), "root", value));
        Assert.AreSame(writeCause, write.InnerException);
        Assert.AreEqual(0L, write.Offset);

        var seekCause = new IOException("injected position");
        CStructReadException seek = Assert.Throws<CStructReadException>(
            () => cstruct.ResolveAddress(
                new FaultingStream(positionException: seekCause),
                "root.scalar"));
        Assert.AreSame(seekCause, seek.InnerException);
        Assert.IsNull(seek.Offset);
    }

    /// <summary>
    ///     Missing paths, incomplete data, conversion failures, and exhausted budgets must expose the expected stable
    ///     code and useful path/offset context.
    /// </summary>
    /// <remarks>
    ///     Limits also have dedicated exception subtypes. Callers can classify failures programmatically instead of
    ///     interpreting human-readable message text.
    /// </remarks>
    [TestMethod]
    public void DomainFailures_ExposeStableCodesAndLimitSubtypes()
    {
        var cstruct = CreateLayout();
        ExpandoObject value = CreateValue();

        CStructPathException path = Assert.Throws<CStructPathException>(
            () => cstruct.ResolveAddress(new MemoryStream(new byte[8]), "root.missing"));
        AssertCode(path, CStructErrorCode.InvalidPath);
        Assert.AreEqual("root.missing", path.Path);
        Assert.AreEqual(5L, path.Offset);
        CStructPathException indexedPath = Assert.Throws<CStructPathException>(
            () => cstruct.ResolveAddress(new MemoryStream(new byte[8]), "root.values[2]"));
        Assert.AreEqual("root.values[2]", indexedPath.Path);
        CStructReadException selectedRead = Assert.Throws<CStructReadException>(
            () => cstruct.Parse(new MemoryStream(new byte[3]), "root.nested"));
        Assert.AreEqual("root.nested", selectedRead.Path);
        Assert.AreEqual(3L, selectedRead.Offset);
        CStructReadException selectedValue = Assert.Throws<CStructReadException>(
            () => cstruct.ReadValue(new MemoryStream(new byte[3]), "root.nested"));
        Assert.AreEqual("root.nested", selectedValue.Path);
        Assert.AreEqual(3L, selectedValue.Offset);
        CStructReadException conversion = Assert.Throws<CStructReadException>(
            () => cstruct.ReadValue<DateTime>(new MemoryStream(new byte[8]), "root.scalar"));
        Assert.AreEqual(CStructErrorCode.ReadFailed, conversion.Code);
        Assert.AreEqual("root.scalar", conversion.Path);
        var stringLayout = new CStruct("struct string_root { cstring value; };", pointerSize: 1);
        CStructReadException lengthRead = Assert.Throws<CStructReadException>(
            () => stringLayout.GetArrayLength(new MemoryStream(), "string_root.value"));
        Assert.AreEqual("string_root.value", lengthRead.Path);
        Assert.AreEqual(0L, lengthRead.Offset);
        AssertCode(
            Assert.Throws<CStructReadException>(
                () => cstruct.Parse(new MemoryStream(), "root")),
            CStructErrorCode.ReadFailed);
        AssertCode(
            Assert.Throws<CStructReadLimitException>(
                () => cstruct.Parse(
                    new MemoryStream(new byte[8]),
                    "root",
                    new Dictionary<string, Expr>(),
                    new ReadOptions { MaxTotalBytesRead = 0, })),
            CStructErrorCode.ReadLimitExceeded);
        CStructWriteException write = Assert.Throws<CStructWriteException>(() => cstruct.Serialize("root", null!));
        AssertCode(write, CStructErrorCode.WriteFailed);
        Assert.AreEqual("root", write.Path);
        using (var updateStream = new MemoryStream(new byte[8]) { Position = 4, })
        {
            CStructWriteException update = Assert.Throws<CStructWriteException>(
                () => cstruct.Update(updateStream, "root.scalar", "not-a-byte"));
            Assert.AreEqual("root.scalar", update.Path);
            Assert.AreEqual(4L, update.Offset);
            Assert.AreEqual(4L, updateStream.Position);
        }

        using (var missingStream = new MemoryStream(new byte[8]))
        {
            CStructPathException missing = Assert.Throws<CStructPathException>(
                () => cstruct.Update(missingStream, "missing", (byte)1));
            Assert.AreEqual("missing", missing.Path);
            Assert.AreEqual(0L, missing.Offset);
        }

        AssertCode(
            Assert.Throws<CStructWriteLimitException>(
                () => cstruct.Serialize(
                    "root",
                    value,
                    options: new WriteOptions { MaxTotalBytesWritten = 0, })),
            CStructErrorCode.WriteLimitExceeded);
    }

    /// <summary>
    ///     Injected cancellation and unexpected implementation exceptions must propagate without being disguised as
    ///     ordinary bad-input errors.
    /// </summary>
    /// <remarks>
    ///     A secondary position-reporting failure must also not replace an existing path failure. This keeps
    ///     application cancellation and possible defects visible to the caller.
    /// </remarks>
    [TestMethod]
    public void UnexpectedFailures_AreNotRelabeled()
    {
        var cstruct = CreateLayout();
        ExpandoObject value = CreateValue();
        var defect = new InvalidOperationException("injected defect");
        var cancellation = new OperationCanceledException("injected cancellation");

        Assert.AreSame(
            defect,
            Assert.Throws<InvalidOperationException>(
                () => cstruct.Parse(new FaultingStream(readException: defect), "root")));
        Assert.AreSame(
            defect,
            Assert.Throws<InvalidOperationException>(
                () => cstruct.ReadValue(new FaultingStream(readException: defect), "root.scalar")));
        Assert.AreSame(
            cancellation,
            Assert.Throws<OperationCanceledException>(
                () => cstruct.Write(new FaultingStream(writeException: cancellation), "root", value)));

        CStructPathException primary = Assert.Throws<CStructPathException>(
            () => cstruct.Write(
                new FaultingStream(positionException: defect),
                "missing",
                value));
        Assert.AreEqual("missing", primary.Path);
        Assert.IsNull(primary.Offset);
    }

    /// <summary>
    ///     Null records, nonnumeric text for a byte, and other incompatible replacement shapes must raise
    ///     CStructWriteException.
    /// </summary>
    /// <remarks>
    ///     The API call itself is valid, but the supplied value cannot be encoded by the layout. That differs from an
    ///     invalid path or a missing stream argument.
    /// </remarks>
    [TestMethod]
    public void InvalidPayloads_UseWriteFailures()
    {
        var cstruct = CreateLayout();

        Assert.Throws<CStructWriteException>(() => cstruct.Serialize("root", null!));
        Assert.Throws<CStructWriteException>(() => cstruct.Serialize("root", new { scalar = "not-a-byte", }));
        Assert.Throws<CStructWriteException>(
            () => cstruct.Serialize(
                "root",
                new
                {
                    scalar = (byte)1,
                    values = new byte[] { 1, },
                    nested = new { value = (byte)2, },
                    link = (object?)null,
                }));
    }

    private static CStruct CreateLayout()
    {
        return new CStruct(Layout, pointerSize: 1);
    }

    private static void AssertCode(CStructException exception, CStructErrorCode expected)
    {
        Assert.AreEqual(expected, exception.Code);
    }

    private static ExpandoObject CreateValue()
    {
        dynamic nested = new ExpandoObject();
        nested.value = (byte)4;
        dynamic value = new ExpandoObject();
        value.scalar = (byte)1;
        value.values = new byte[] { 2, 3, };
        value.nested = nested;
        value.link = null;
        return value;
    }

    private sealed class NonSeekableReadStream : Stream
    {
        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            throw new NotSupportedException();
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }
    }

    private sealed class WriteOnlySeekableStream : Stream
    {
        private readonly MemoryStream inner = new();

        public override bool CanRead => false;

        public override bool CanSeek => true;

        public override bool CanWrite => true;

        public override long Length => this.inner.Length;

        public override long Position
        {
            get => this.inner.Position;
            set => this.inner.Position = value;
        }

        public override void Flush()
        {
            this.inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return this.inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            this.inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            this.inner.Write(buffer, offset, count);
        }
    }

    private sealed class CapabilityStream(bool canRead, bool canSeek, bool canWrite) : Stream
    {
        public override bool CanRead => canRead;

        public override bool CanSeek => canSeek;

        public override bool CanWrite => canWrite;

        public override long Length => 0;

        public override long Position { get; set; }

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            return 0;
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            this.Position = offset;
            return this.Position;
        }

        public override void SetLength(long value)
        {
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
        }
    }

    private sealed class FaultingStream(
        Exception? readException = null,
        Exception? writeException = null,
        Exception? positionException = null) : Stream
    {
        private readonly MemoryStream inner = new(new byte[64]);

        public override bool CanRead => true;

        public override bool CanSeek => true;

        public override bool CanWrite => true;

        public override long Length => this.inner.Length;

        public override long Position
        {
            get => positionException is null ? this.inner.Position : throw positionException;
            set => this.inner.Position = value;
        }

        public override void Flush()
        {
            this.inner.Flush();
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            throw readException ?? new IOException("Unexpected read.");
        }

        public override int Read(Span<byte> buffer)
        {
            throw readException ?? new IOException("Unexpected read.");
        }

        public override int ReadByte()
        {
            throw readException ?? new IOException("Unexpected read.");
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            return this.inner.Seek(offset, origin);
        }

        public override void SetLength(long value)
        {
            this.inner.SetLength(value);
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw writeException ?? new IOException("Unexpected write.");
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            throw writeException ?? new IOException("Unexpected write.");
        }

        public override void WriteByte(byte value)
        {
            throw writeException ?? new IOException("Unexpected write.");
        }
    }
}
