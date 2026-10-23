namespace CStructSharp.Generators.Tests;

using System;
using System.Linq;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     <see cref="ReadOptions.InvalidText"/> through the generated code: the generated reader returns what the runtime
///     returns when invalid text is replaced and fails with the runtime's failure when it is not, and a view's text
///     members and its stored bytes follow the same option.
/// </summary>
[TestClass]
public class LenientTextTests
{
    private const string Consumer = """"
        using CStructSharp;

        namespace Demo;

        [CStructLayout("struct root { utf8 a[3]; utf16le b[4]; wchar c[2]; cstring d; utf8_string_zero e; string< f; uint8 tail; };", Root = "root")]
        public static partial class TextLayout { }

        public static class Probe
        {
            public static object[] Read(byte[] bytes, ReadOptions options)
            {
                var view = new Demo.TextLayout.RootView(bytes, options);
                return new object[] { view.A, view.B, view.C, view.ABytes.Length };
            }
        }
        """";

    // a "abc", b "ab", c "ab", d "x", e "y", f "z", tail.
    private static readonly byte[] Valid = [0x61, 0x62, 0x63, 0x61, 0x00, 0x62, 0x00, 0x61, 0x00, 0x62, 0x00, 0x78, 0x00, 0x79, 0x00, 0x7A, 0x00, 0x00, 0x00, 9];

    private static readonly ReadOptions Lenient = new() { InvalidText = InvalidTextHandling.Replace };

    private static Assembly assembly = null!;

    /// <summary>Generates and compiles the consumer once.</summary>
    /// <param name="context">The test context.</param>
    [ClassInitialize]
    public static void Generate(TestContext context)
    {
        _ = context;
        assembly = GeneratorRunner.Run(Consumer).AssertClean().Load();
    }

    /// <summary>Reads the root with the generated reader and unwraps a failure it throws.</summary>
    /// <param name="bytes">The input.</param>
    /// <param name="options">The read options.</param>
    /// <returns>The parsed root.</returns>
    private static object Parse(byte[] bytes, ReadOptions? options)
    {
        MethodInfo parse = assembly.GetType("Demo.TextLayout")!.GetMethods().Single(method => method.Name == "Parse" && method.GetParameters()[0].ParameterType == typeof(byte[]));
        try
        {
            return parse.Invoke(null, [bytes, options])!;
        }
        catch (TargetInvocationException exception)
        {
            throw exception.InnerException!;
        }
    }

    /// <summary>Each text type, corrupted on its own, fails by default as the runtime does and reads as the runtime does when replaced.</summary>
    [TestMethod]
    public void EveryTextType_MatchesTheRuntime_UnderBothSettings()
    {
        var runtime = (CStruct)assembly.GetType("Demo.TextLayout")!.GetProperty("Layout")!.GetValue(null)!;
        foreach ((string member, int offset, byte[] corruption) in new (string, int, byte[])[]
                 {
                     ("a", 1, [0xFF]),
                     ("b", 3, [0x00, 0xD8]),
                     ("c", 7, [0x00, 0xD8]),
                     ("d", 11, [0x80]),
                     ("e", 13, [0xFF]),
                     ("f", 15, [0x00, 0xD8]),
                 })
        {
            byte[] bytes = (byte[])Valid.Clone();
            corruption.CopyTo(bytes, offset);
            Exception expected = Assert.Throws<CStructReadException>(() => runtime.Parse(bytes, "root"), member);
            Exception actual = Assert.Throws<CStructReadException>(() => Parse(bytes, null), member);
            Assert.AreEqual(expected.Message, actual.Message, member + " strict message");
            ParityComparer.AssertSame(runtime.Parse(bytes, "root", options: Lenient), Parse(bytes, Lenient), "root", strict: true);
        }
    }

    /// <summary>A view's text members replace invalid text when asked, fail otherwise, and always keep the stored bytes reachable.</summary>
    [TestMethod]
    public void ViewText_FollowsTheOption_AndKeepsTheBytes()
    {
        byte[] bytes = (byte[])Valid.Clone();
        bytes[1] = 0xFF;
        bytes[3] = 0x00;
        bytes[4] = 0xD8;
        MethodInfo read = assembly.GetType("Demo.Probe")!.GetMethod("Read")!;
        var readings = (object[])read.Invoke(null, [bytes, Lenient])!;
        Assert.AreEqual("a�c", readings[0]);
        Assert.AreEqual("�b", readings[1]);
        Assert.AreEqual(3, readings[3]);
        Exception failure = Assert.Throws<TargetInvocationException>(() => read.Invoke(null, [bytes, null])).InnerException!;
        Assert.IsInstanceOfType<CStructReadException>(failure);
    }
}
