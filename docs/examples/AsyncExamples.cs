namespace CStructSharp.Docs.Examples;

using System.Buffers;
using System.Collections.Generic;
using System.IO.Pipelines;
using global::CStructSharp;
using global::CStructSharp.Diagnostics;
using global::CStructSharp.Values;

/// <summary>The executable samples of the async guide (<c>docs/guides/async-and-pipelines.md</c>) and its recipes.</summary>
internal static partial class Program
{
    #region recipe-async-stream
    private static async Task AsyncStream()
    {
        // A file holds a six-byte header and a payload. Opened for asynchronous I/O, the header is read with
        // ParseAsync: the bytes arrive through ReadAsync while the thread is free, then the ordinary reader decodes them.
        var layout = new CStruct("struct header { uint16 kind; uint32 length; };");
        string path = Path.Combine(Path.GetTempPath(), $"cstructsharp-{Guid.NewGuid():N}.bin");
        await File.WriteAllBytesAsync(path, [0x02, 0x00, 0x06, 0x00, 0x00, 0x00, 0xAA, 0xBB, 0xCC, 0xDD, 0xEE, 0xFF]);
        try
        {
            await using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, useAsync: true);

            // A token bounds the wait: a stalled disk or network ends in OperationCanceledException, not a read failure.
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            StructValue header = await layout.ParseAsync(file, "header", cancellationToken: timeout.Token);
            Equal((ushort)2, header.Get<ushort>("kind"));
            Equal(6u, header.Get<uint>("length"));
            Equal(6L, file.Position);

            // The payload's length came from the header; the rest of the file is read the usual way.
            byte[] payload = new byte[header.Get<uint>("length")];
            await file.ReadExactlyAsync(payload, timeout.Token);
            Equal("AABBCCDDEEFF", Convert.ToHexString(payload));

            // Three bytes at the end are not a header: the non-throwing form reports the failure the throwing form
            // would raise, and a seekable stream is back where it started.
            file.Position = 9;
            ReadAttempt<StructValue> attempt = await layout.TryReadValueAsync<StructValue>(file, "header", cancellationToken: timeout.Token);
            True(!attempt.Succeeded, "three bytes are not a header");
            True(attempt.Failure is CStructReadException, "the failure is the read exception");
            Equal(9L, file.Position);

            // A cancelled token stops the read before any byte is taken.
            using var cancelled = new CancellationTokenSource();
            cancelled.Cancel();
            file.Position = 0;
            try
            {
                await layout.ParseAsync(file, "header", cancellationToken: cancelled.Token);
                True(false, "unreachable");
            }
            catch (OperationCanceledException)
            {
                Equal(0L, file.Position);
            }
        }
        finally
        {
            File.Delete(path);
        }
    }
    #endregion

    #region recipe-pipe-reader
    private static async Task PipeReaderFraming()
    {
        // Four-byte frames arrive on a pipe in chunks that do not line up with the frame boundaries - exactly what a
        // socket delivers. A PipeReader over a NetworkStream (PipeReader.Create(stream)) is used the same way.
        var layout = new CStruct("struct frame { uint16 id; uint16 value; };");
        int size = layout.GetStructSizeInBytes("frame");
        var pipe = new Pipe();
        Task producer = Task.Run(async () =>
        {
            byte[] frames = [1, 0, 10, 0, 2, 0, 20, 0, 3, 0, 30, 0, 4, 0, 40, 0, 5, 0, 50, 0];
            foreach (int[] chunk in new[] { new[] { 0, 6 }, new[] { 6, 7 }, new[] { 13, 7 } })
            {
                await pipe.Writer.WriteAsync(frames.AsMemory(chunk[0], chunk[1]));
            }

            await pipe.Writer.CompleteAsync();
        });

        var ids = new List<ushort>();
        while (true)
        {
            var result = await pipe.Reader.ReadAsync();
            ReadOnlySequence<byte> buffer = result.Buffer;

            // Parse the whole frames the buffer holds - ParseMany reads a ReadOnlySequence<byte> directly, one frame per
            // step - and hand the partial frame at the end back to the pipe: consumed up to the last whole frame,
            // examined to the end, so the next ReadAsync waits for more bytes instead of returning the same ones.
            long whole = buffer.Length / size * size;
            foreach (StructValue frame in layout.ParseMany(buffer.Slice(0, whole), "frame"))
            {
                ids.Add(frame.Get<ushort>("id"));
            }

            pipe.Reader.AdvanceTo(buffer.GetPosition(whole), buffer.End);
            if (result.IsCompleted)
            {
                True(buffer.Length == whole, "the producer ended on a frame boundary");
                break;
            }
        }

        await producer;
        await pipe.Reader.CompleteAsync();
        Equal("1,2,3,4,5", string.Join(",", ids));

        // A count-prefixed message has no fixed size: read the count first (a whole header, or wait), work out the
        // message's length from it, and parse the message only when every byte of it is there.
        var messages = new CStruct("struct message { uint8 count; uint8 payload[count]; };");
        var chunks = new Pipe();
        await chunks.Writer.WriteAsync(new byte[] { 2, 0xAA, 0xBB, 3, 0xCC });
        await chunks.Writer.WriteAsync(new byte[] { 0xDD, 0xEE, 0 });
        await chunks.Writer.CompleteAsync();
        var lengths = new List<int>();
        while (true)
        {
            var result = await chunks.Reader.ReadAsync();
            ReadOnlySequence<byte> buffer = result.Buffer;
            while (messages.TryReadValue(buffer, "message.count", out byte count) && buffer.Length >= 1 + count)
            {
                StructValue message = messages.Parse(buffer.Slice(0, 1 + count), "message");
                lengths.Add(message.Get<byte[]>("payload").Length);
                buffer = buffer.Slice(1 + count);
            }

            chunks.Reader.AdvanceTo(buffer.Start, buffer.End);
            if (result.IsCompleted)
            {
                break;
            }
        }

        await chunks.Reader.CompleteAsync();
        Equal("2,3,0", string.Join(",", lengths));
    }
    #endregion
}
