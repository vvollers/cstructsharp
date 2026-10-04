namespace CStructSharp.Generated.Parity;

using System;
using System.Buffers;
using System.Collections.Generic;
using CStructSharp.Codecs;

/// <summary>Small generated writers covering owned rows and borrowed destinations that overlap their input.</summary>
public static partial class MatrixRowWriteLayouts
{
    /// <summary>A little-endian matrix between scalar fields, with no padding.</summary>
    [CStructLayout("struct root { uint8 tag; uint16< matrix[2][3]; uint8 tail; };", Root = "root")]
    public static partial class Header
    {
    }

    /// <summary>A single byte row whose own storage can also be supplied as the output span.</summary>
    [CStructLayout("struct root { uint8 matrix[1][32]; };", Root = "root")]
    public static partial class Alias
    {
    }

    /// <summary>An anonymous composite promotes its matrix directly into the generated root.</summary>
    [CStructLayout("struct root { struct { uint16> matrix[2][2]; }; };", Root = "root")]
    public static partial class Promoted
    {
    }

    /// <summary>A nested composite combines three dimensions, big-endian numbers, booleans and empty leaf rows.</summary>
    [CStructLayout("struct child { uint32> cube[2][2][3]; bool flags[2][3]; uint16 empty[2][0]; }; struct root { child content; uint8 tail; };", Root = "root")]
    public static partial class Nested
    {
    }

    /// <summary>Custom array writes can cancel the operation before the following numeric matrix without a new checkpoint.</summary>
    [CStructLayout("struct root { tap events[1][2]; uint16< matrix[2][3]; };", Root = "root", Codecs = new[] { "tap:1:1" })]
    public static partial class Callback
    {
        /// <summary>Supplies the custom codec whose writes record observable caller actions.</summary>
        /// <returns>The single codec declared in this layout.</returns>
        private static partial IReadOnlyList<ICustomCodec> CreateCodecs() => [new TapCodec()];

        /// <summary>Invokes the supplied action once for each encoded byte.</summary>
        private sealed class TapCodec : ICustomCodec
        {
            /// <inheritdoc/>
            public string Name => "tap";

            /// <inheritdoc/>
            public int? FixedSize => 1;

            /// <inheritdoc/>
            public int Alignment => 1;

            /// <inheritdoc/>
            public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
            {
                value = source.IsEmpty ? null : source[0];
                bytesConsumed = source.IsEmpty ? 0 : 1;
                return source.IsEmpty ? OperationStatus.NeedMoreData : OperationStatus.Done;
            }

            /// <inheritdoc/>
            /// <remarks>The callback runs only after capacity is available, once per successfully encoded element.</remarks>
            public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
            {
                bytesWritten = 0;
                if (destination.IsEmpty)
                {
                    return OperationStatus.DestinationTooSmall;
                }

                ((Action)value)();
                destination[0] = 7;
                bytesWritten = 1;
                return OperationStatus.Done;
            }
        }
    }
}
