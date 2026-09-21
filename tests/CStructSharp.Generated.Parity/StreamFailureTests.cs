namespace CStructSharp.Generated.Parity;

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CStructSharp.Values;

/// <summary>Compares runtime and generated failure-state contracts after a stream supplies bytes and then fails.</summary>
[TestClass]
public class StreamFailureTests
{
    /// <summary>Every buffered operation restores its seekable origin and preserves the original I/O or cancellation failure.</summary>
    /// <param name="cancel">Whether acquisition fails with cancellation instead of an I/O error.</param>
    /// <param name="failRestore">Whether restoring the origin also fails; the original failure must still escape.</param>
    [TestMethod]
    [DataRow(false, false)]
    [DataRow(true, false)]
    [DataRow(false, true)]
    [DataRow(true, true)]
    public async Task PartialAcquisitionFailure_PreservesOriginAndException(bool cancel, bool failRestore)
    {
        var layout = new CStruct(StreamFailureLayout.Definition);
        foreach (string operation in new[] { "parse", "debug", "value", "typed", "value-debug", "try", "address", "length", "update", "generated", "generated-async", })
        {
            using var stream = new PartialFailureStream(cancel, failRestore);
            Exception? actual = null;
            try
            {
                switch (operation)
                {
                    case "parse": await layout.ParseAsync(stream); break;
                    case "debug": await layout.ParseWithDebugAsync(stream); break;
                    case "value": await layout.ReadValueAsync(stream, "header.kind"); break;
                    case "typed": await layout.ReadValueAsync<ushort>(stream, "header.kind"); break;
                    case "value-debug": await layout.ReadValueWithDebugAsync(stream, "header.kind"); break;
                    case "try": await layout.TryReadValueAsync<StructValue>(stream, "header"); break;
                    case "address": await layout.ResolveAddressAsync(stream, "header.kind"); break;
                    case "length": await layout.GetArrayLengthAsync(stream, "header.payload"); break;
                    case "update": await layout.UpdateAsync(stream, "header.kind", (ushort)3); break;
                    case "generated": StreamFailureLayout.Parse(stream); break;
                    case "generated-async": await StreamFailureLayout.ParseAsync(stream); break;
                    default: Assert.Fail("Unknown operation"); break;
                }
            }
            catch (Exception failure)
            {
                actual = failure;
            }

            Assert.AreSame(stream.Failure, actual, operation + ": preserve original failure, including cancellation");
            Assert.AreEqual(failRestore ? 3L : 2L, stream.Position, operation);
            Assert.IsTrue(stream.RestoreAttempted, operation + ": attempt restoration even when acquisition fails");
            Assert.AreEqual(0, stream.Writes, operation + ": acquisition failure must not write");
        }
    }

    /// <summary>A readable and writable seekable stream that returns one byte, then throws a stable exception instance.</summary>
    private sealed class PartialFailureStream : Stream
    {
        private readonly bool failRestore;
        private long position = 2;
        private int reads;

        /// <summary>Creates an origin-two source and chooses the acquisition and restoration failures.</summary>
        /// <param name="cancel">Use a cancellation exception for the read failure.</param>
        /// <param name="failRestore">Reject position restoration after reading.</param>
        public PartialFailureStream(bool cancel, bool failRestore)
        {
            this.failRestore = failRestore;
            this.Failure = cancel ? new OperationCanceledException("Source cancelled after one byte") : new IOException("Source failed after one byte");
        }

        /// <summary>Gets the exact failure thrown by the source.</summary>
        public Exception Failure { get; }

        /// <summary>Gets whether the library attempted to restore the origin.</summary>
        public bool RestoreAttempted { get; private set; }

        /// <summary>Gets the number of attempted writes.</summary>
        public int Writes { get; private set; }

        /// <inheritdoc/>
        public override bool CanRead => true;

        /// <inheritdoc/>
        public override bool CanWrite => true;

        /// <inheritdoc/>
        public override bool CanSeek => true;

        /// <inheritdoc/>
        public override long Length => 8;

        /// <summary>Gets the cursor or restores it, optionally failing to model a broken underlying source.</summary>
        public override long Position
        {
            get => this.position;
            set
            {
                this.RestoreAttempted = true;
                if (this.failRestore)
                {
                    throw new IOException("Restoration failed independently");
                }

                this.position = value;
            }
        }

        /// <summary>Supplies one byte once, then fails on the next read.</summary>
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (this.reads++ != 0)
            {
                throw this.Failure;
            }

            buffer[offset] = 2;
            this.position++;
            return 1;
        }

        /// <summary>Uses the same partial-read failure sequence for asynchronous acquisition.</summary>
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            byte[] one = new byte[1];
            int count = this.Read(one, 0, 1);
            one.CopyTo(buffer);
            return ValueTask.FromResult(count);
        }

        /// <summary>Rejects seeking by offset; the operation restores Position directly.</summary>
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <summary>Rejects resizing the synthetic source.</summary>
        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>Records a write that should never follow failed acquisition.</summary>
        public override void Write(byte[] buffer, int offset, int count) => this.Writes++;

        /// <summary>Does nothing because this source has no pending writes.</summary>
        public override void Flush()
        {
        }
    }
}
