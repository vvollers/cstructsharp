namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Codecs;
using CStructSharp.Diagnostics;

/// <summary>Checks composite-array span preflight arithmetic without allocating a large input or result.</summary>
[TestClass]
public class CompositeArrayExtentBoundaryTests
{
    /// <summary>A four-element array with a four-gibibyte extent falls back to the normal short-input diagnostic.</summary>
    [TestMethod]
    public void LargeCompositeArrayExtent_DoesNotWrapItsSpanLength()
    {
        var codec = new LargeFixedCodec();
        var layout = new CStruct("struct element { huge_block _; }; struct root { uint8 count; element values[count]; };", compilationOptions: new CStructCompilationOptions { Codecs = [codec,], });
        byte[] input = [4,];

        // Only the count byte and four list slots exist; the codec reports truncated borrowed input without renting storage.
        CStructReadException failure = Assert.Throws<CStructReadException>(() => layout.Parse(input.AsSpan(), "root"));
        StringAssert.Contains(failure.Message, "huge_block");
        Assert.AreEqual(1, codec.ReadCalls);
    }

    /// <summary>Declares a large fixed storage extent while handling truncated spans without allocation.</summary>
    private sealed class LargeFixedCodec : ICustomCodec
    {
        public string Name => "huge_block";

        public int? FixedSize => 1 << 30;

        public int Alignment => 1;

        /// <summary>Gets the number of attempts to decode the borrowed input.</summary>
        public int ReadCalls { get; private set; }

        /// <summary>Consumes a complete fixed block or reports that the supplied input is too short.</summary>
        /// <param name="source">The borrowed input bytes.</param>
        /// <param name="value">A zero marker on success, otherwise null.</param>
        /// <param name="bytesRead">The declared extent on success, otherwise zero.</param>
        /// <returns>Done only when the whole fixed block is present.</returns>
        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesRead)
        {
            this.ReadCalls++;
            bool complete = source.Length >= this.FixedSize!.Value;
            value = complete ? 0 : null;
            bytesRead = complete ? this.FixedSize.Value : 0;
            return complete ? OperationStatus.Done : OperationStatus.NeedMoreData;
        }

        /// <summary>Encodes the fixed zero block only when the destination has room for its complete extent.</summary>
        /// <param name="destination">The output window.</param>
        /// <param name="value">Unused marker value.</param>
        /// <param name="bytesWritten">The complete extent on success, otherwise zero.</param>
        /// <returns>Done or DestinationTooSmall according to the available storage.</returns>
        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            bytesWritten = 0;
            if (destination.Length < this.FixedSize!.Value)
            {
                return OperationStatus.DestinationTooSmall;
            }

            destination[..this.FixedSize.Value].Clear();
            bytesWritten = this.FixedSize.Value;
            return OperationStatus.Done;
        }
    }
}
