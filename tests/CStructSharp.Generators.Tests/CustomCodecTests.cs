namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     Custom codecs through the generator: the attribute declares each codec's placement facts, the class supplies
///     the instances from <c>CreateCodecs()</c>, and the generated reader hands each value to the codec exactly as
///     the runtime's memory path does - the same values, the same failures, the same offsets.
/// </summary>
[TestClass]
public class CustomCodecTests
{
    // A 16-bit length prefix followed by that many ASCII bytes (variable length), and a fixed three-byte colour.
    private const string Consumer = """"
        using System;
        using System.Buffers;
        using System.Collections.Generic;
        using System.Text;
        using CStructSharp;
        using CStructSharp.Codecs;

        namespace Demo;

        public sealed class Blob : ICustomCodec
        {
            public string Name => "blob";
            public int? FixedSize => null;
            public int Alignment => 1;

            public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
            {
                value = null;
                bytesConsumed = 0;
                if (source.Length < 2) return OperationStatus.NeedMoreData;
                int length = source[0] | (source[1] << 8);
                if (length == 0xFFFF) return OperationStatus.InvalidData;
                if (length == 0xFFFE) throw new InvalidOperationException("boom");
                if (length == 0xFFFD) { bytesConsumed = source.Length + 1; value = ""; return OperationStatus.Done; }
                if (source.Length < 2 + length) return OperationStatus.NeedMoreData;
                value = Encoding.ASCII.GetString(source.Slice(2, length));
                bytesConsumed = 2 + length;
                return OperationStatus.Done;
            }

            public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
            {
                byte[] payload = Encoding.ASCII.GetBytes((string)value);
                bytesWritten = 0;
                if (destination.Length < 2 + payload.Length) return OperationStatus.DestinationTooSmall;
                destination[0] = (byte)payload.Length;
                destination[1] = (byte)(payload.Length >> 8);
                payload.CopyTo(destination[2..]);
                bytesWritten = 2 + payload.Length;
                return OperationStatus.Done;
            }
        }

        public sealed class Rgb : ICustomCodec
        {
            public string Name => "rgb";
            public int? FixedSize => 3;
            public int Alignment => 1;

            public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
            {
                value = null;
                bytesConsumed = 0;
                if (source.Length < 3) return OperationStatus.NeedMoreData;
                value = (source[0] << 16) | (source[1] << 8) | source[2];
                bytesConsumed = 3;
                return OperationStatus.Done;
            }

            public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
            {
                bytesWritten = 0;
                if (destination.Length < 3) return OperationStatus.DestinationTooSmall;
                int packed = (int)value;
                destination[0] = (byte)(packed >> 16);
                destination[1] = (byte)(packed >> 8);
                destination[2] = (byte)packed;
                bytesWritten = 3;
                return OperationStatus.Done;
            }
        }

        [CStructLayout("""
            struct root { uint8 head; blob name; rgb colour; rgb *ptr; blob items[2]; uint8 tail; };
            """, Codecs = new[] { "blob", "rgb:3:1" }, PointerSize = 1, Aligned = false)]
        public static partial class Packet
        {
            private static partial IReadOnlyList<ICustomCodec> CreateCodecs() => new ICustomCodec[] { new Blob(), new Rgb() };
        }
        """";

    [TestMethod]
    public void DeclaredCodecs_ReadThroughTheInstancesTheClassSupplies()
    {
        GeneratorResult result = GeneratorRunner.Run(Consumer).AssertClean();
        Snapshot.Match("Codecs.Packet", result.Source);
        Assembly assembly = result.Load();
        Type packet = assembly.GetType("Demo.Packet")!;
        var runtime = (CStruct)packet.GetProperty("Layout")!.GetValue(null)!;
        MethodInfo parse = packet.GetMethods().Single(method => method.Name == "ParseRoot" && method.GetParameters()[0].ParameterType == typeof(byte[]));

        // head, name "hi", colour 0x102030, ptr → offset 12 (the rgb after items), items "a" and "", tail, target.
        byte[] bytes = [7, 2, 0, (byte)'h', (byte)'i', 0x10, 0x20, 0x30, 12, 1, 0, (byte)'a', 0, 0, 9, 0xAA, 0xBB, 0xCC];
        object generated = parse.Invoke(null, [bytes, null, null])!;
        ParityComparer.AssertSame(runtime.Parse(bytes, "root"), generated, "root");
        Assert.AreEqual("hi", packet.GetNestedType("Root")!.GetProperty("Name")!.GetValue(generated));

        // The failure texts and offsets: a short read (the position moves to the end), a rejected value, a codec that
        // throws, a codec that over-reports, and the read budget charged with the consumed bytes.
        foreach ((byte[] input, ReadOptions? options) in new (byte[], ReadOptions?)[]
                 {
                     ([7, 5, 0, (byte)'h'], null),
                     ([7, 0xFF, 0xFF, 1], null),
                     ([7, 0xFE, 0xFF, 1], null),
                     ([7, 0xFD, 0xFF, 1], null),
                     (bytes, new ReadOptions { MaxTotalBytesRead = 4 }),
                     (bytes, new ReadOptions { DereferencePointers = false }),
                 })
        {
            Exception? expected = Catch(() => runtime.Parse(input, "root", options: options));
            Exception? actual = Catch(() => parse.Invoke(null, [input, null, options]));
            if (expected is null)
            {
                Assert.IsNull(actual, actual?.Message);
                ParityComparer.AssertSame(runtime.Parse(input, "root", options: options), parse.Invoke(null, [input, null, options]), "root");
                continue;
            }

            Assert.AreEqual(expected.GetType(), actual?.GetType(), actual?.Message);
            Assert.AreEqual(expected.Message, actual!.Message);
        }

        foreach (int length in Enumerable.Range(0, bytes.Length))
        {
            byte[] prefix = bytes[..length];
            Exception? expected = Catch(() => runtime.Parse(prefix, "root"));
            Exception? actual = Catch(() => parse.Invoke(null, [prefix, null, null]));
            Assert.AreEqual(expected?.Message, actual?.Message, "truncated to " + length);
        }
    }

    [TestMethod]
    public void MismatchedInstance_FailsAtFirstUse_AndMalformedDeclarationsAreCsg006()
    {
        GeneratorResult result = GeneratorRunner.Run(Consumer.Replace("\"rgb:3:1\"", "\"rgb:4:1\"", StringComparison.Ordinal)).AssertClean();
        Type packet = result.Load().GetType("Demo.Packet")!;
        MethodInfo parse = packet.GetMethods().Single(method => method.Name == "ParseRoot" && method.GetParameters()[0].ParameterType == typeof(byte[]));
        Exception? failure = Catch(() => parse.Invoke(null, [new byte[16], null, null]));
        Assert.IsInstanceOfType<InvalidOperationException>(failure);
        StringAssert.Contains(failure.Message, "rgb:4:1 is declared");
        StringAssert.Contains(failure.Message, "reports rgb:3:1");

        foreach (string declaration in new[] { string.Empty, "rgb:x", "rgb:3:3", "uint8:1", "a:b:c:d", "2rgb" })
        {
            GeneratorResult malformed = GeneratorRunner.Run(Consumer.Replace("\"rgb:3:1\"", "\"" + declaration + "\"", StringComparison.Ordinal));
            Assert.AreEqual(1, malformed.DiagnosticsWithId("CSG006").Count, declaration + ": " + string.Join("; ", malformed.GeneratorDiagnostics));
        }
    }

    private static Exception? Catch(Action action)
    {
        try
        {
            action();
            return null;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            return exception.InnerException;
        }
        catch (CStructException exception)
        {
            return exception;
        }
    }
}
