namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Generated readers against the runtime, the oracle: for every fixture with bytes, <c>Parse</c> yields the same
///     value member by member, and every truncated prefix of the bytes fails with the same exception type and message.
/// </summary>
[TestClass]
public class ReaderParityTests
{
    private const string Header = """
        using CStructSharp;

        namespace Parity;

        """;

    /// <summary>
    ///     Generated readers decode every manual fixture with bytes as the runtime does, and every truncated prefix
    ///     fails with the runtime's exception type and message.
    /// </summary>
    [TestMethod]
    public void ManualFixtures_ParseAndTruncationSweep_MatchTheRuntime()
    {
        var failures = new List<string>();
        foreach (ManualFixture fixture in ManualFixtures.Load())
        {
            if (fixture.Bytes is null)
            {
                continue;
            }

            try
            {
                RunParity(fixture);
            }
            catch (Exception exception) when (exception is AssertFailedException or InvalidOperationException or TargetInvocationException or CStructException)
            {
                failures.Add(fixture.Id + ": " + (exception.InnerException ?? exception).Message);
            }
        }

        Assert.IsEmpty(failures, string.Join("\n\n", failures));
    }

    /// <summary>The benchmark harness's fixtures (up to 128 KiB of bytes): values, expected errors, and a truncation sweep bounded by the fixture's size.</summary>
    [TestMethod]
    public void BenchmarkFixtures_ParseAndTruncationSweep_MatchTheRuntime()
    {
        var failures = new List<string>();
        foreach (BenchmarkFixture fixture in BenchmarkFixtures.Load(128 * 1024))
        {
            if (fixture.Root.Contains('[', StringComparison.Ordinal))
            {
                // A synthetic root (a type spelling such as `uint32[256]`) is the runtime's alone; a generated
                // Parse needs a declaration.
                continue;
            }

            try
            {
                string arguments = "Root = \"" + fixture.Root + "\", PointerSize = " + fixture.PointerSize + ", Aligned = " + (fixture.Aligned ? "true" : "false") + ", LittleEndian = " + (fixture.LittleEndian ? "true" : "false");
                RunParity(fixture.Id, fixture.Definition, arguments, fixture.Root, fixture.Bytes, fixture.Variables, fixture.ReadOptions, fixture.ExpectedError);
            }
            catch (Exception exception) when (exception is AssertFailedException or InvalidOperationException or TargetInvocationException or CStructException)
            {
                failures.Add(fixture.Id + ": " + (exception.InnerException ?? exception).Message);
            }
        }

        Assert.IsEmpty(failures, string.Join("\n\n", failures));
    }

    /// <summary>Runs one manual fixture through both readers with the options it was recorded with.</summary>
    /// <param name="fixture">A fixture that has input bytes.</param>
    private static void RunParity(ManualFixture fixture)
    {
        RunParity(fixture.Id, fixture.Definition, ManualFixtures.AttributeArguments(fixture), fixture.Root, Convert.FromHexString(fixture.Bytes!), fixture.Variables, null, null);
    }

    /// <summary>A layout declared from a <c>.cstruct</c> file generates the same readers as the inline text: every manual fixture, through the file path.</summary>
    [TestMethod]
    public void FileDeclaredFixtures_MatchTheInlineDeclarations()
    {
        int compared = 0;
        foreach (ManualFixture fixture in ManualFixtures.Load().Where(item => item.Bytes is not null).Take(12))
        {
            string arguments = ManualFixtures.AttributeArguments(fixture);
            string inline = Header + "[CStructLayout(" + Literal(fixture.Definition) + ", " + arguments + ")]\npublic static partial class Fixture { }\n";
            string fromFile = Header + "[CStructLayout(File = \"layouts/" + fixture.Id + ".cstruct\", " + arguments + ")]\npublic static partial class Fixture { }\n";
            string inlineSource = GeneratorRunner.Run(inline).AssertClean().Source;
            string fileSource = GeneratorRunner.Run(fromFile, [("/project/layouts/" + fixture.Id + ".cstruct", fixture.Definition)]).AssertClean().Source;

            // Only the Definition constant differs (the file's text is used verbatim); every generated member is the same.
            Assert.AreEqual(Strip(inlineSource), Strip(fileSource), fixture.Id);
            compared++;
        }

        Assert.AreEqual(12, compared);

        // Returns the generated source without its Definition constant line.
        static string Strip(string source) => string.Join("\n", source.Split('\n').Where(line => !line.Contains("public const string Definition = ", StringComparison.Ordinal)));
    }

    /// <summary>
    ///     Pointers followed after their struct, including <c>@count(N)</c> targets whose count is declared after the
    ///     pointer: both readers produce the same value and, for every truncated prefix and invalid count, the same
    ///     first failure.
    /// </summary>
    [TestMethod]
    public void DeferredAndCountedPointers_MatchTheRuntime()
    {
        const string LittleOne = "PointerSize = 1, Aligned = false, LittleEndian = true";
        RunParity(
            "counted-gcm",
            "typedef uint8 *byte_ptr; struct params { byte_ptr iv @count(iv_len); uint16 iv_len; byte_ptr tag @count(tag_bits / 8); uint16 tag_bits; };",
            "Root = \"params\", " + LittleOne,
            "params",
            [8, 3, 0, 11, 16, 0, 0, 0, 0xA1, 0xA2, 0xA3, 0xB1, 0xB2,],
            new Dictionary<string, int>(),
            null,
            null);
        RunParity(
            "counted-structs",
            "struct blob { uint16 a; uint8 b; }; struct rec { blob *items @count(n); uint8 n; uint8 tail; };",
            "Root = \"rec\", " + LittleOne,
            "rec",
            [3, 2, 9, 1, 0, 2, 3, 0, 4,],
            new Dictionary<string, int>(),
            null,
            null);
        RunParity(
            "counted-text",
            "struct rec { char *name @count(len); uint8 len; };",
            "Root = \"rec\", " + LittleOne,
            "rec",
            [2, 3, (byte)'a', (byte)'b', (byte)'c',],
            new Dictionary<string, int>(),
            null,
            null);
        RunParity(
            "deferred-pointer-array",
            "struct node { uint8 v; }; struct rec { node *items[2]; uint8 tail; };",
            "Root = \"rec\", " + LittleOne,
            "rec",
            [3, 4, 9, 7, 8,],
            new Dictionary<string, int>(),
            null,
            null);
        RunParity(
            "counted-negative",
            "struct rec { uint8 *iv @count(n); int8 n; };",
            "Root = \"rec\", " + LittleOne,
            "rec",
            [2, 0xFF, 0,],
            new Dictionary<string, int>(),
            null,
            "CStructReadException");
        RunParity(
            "counted-limit",
            "struct rec { uint16 *v @count(n); uint8 n; };",
            "Root = \"rec\", " + LittleOne,
            "rec",
            [2, 3, 1, 0, 2, 0, 3, 0,],
            new Dictionary<string, int>(),
            new ReadOptions { MaxPointerTargetBytes = 5, },
            "CStructReadLimitException");
    }

    /// <summary>
    ///     An <c>@N</c> assertion after a runtime-sized field is checked while reading and writing, counted from the start
    ///     of the field's own struct (here <c>inner</c> starts at byte 1): both readers and writers accept the right
    ///     offset and fail the wrong one with the same message.
    /// </summary>
    [TestMethod]
    public void RuntimeCheckedOffsetAssertions_MatchTheRuntime()
    {
        const string LittleOne = "PointerSize = 1, Aligned = false, LittleEndian = true";
        const string Layout = "struct inner { uint8 n; uint8 d[n]; uint8 x @2; }; struct root { uint8 pad; inner i; };";
        RunParity("offset-nested", Layout, "Root = \"root\", " + LittleOne, "root", [0, 1, 2, 3,], new Dictionary<string, int>(), null, null);
        RunParity("offset-nested-wrong", Layout.Replace("@2", "@3", StringComparison.Ordinal), "Root = \"root\", " + LittleOne, "root", [0, 1, 2, 3,], new Dictionary<string, int>(), null, "CStructLayoutException");
    }

    /// <summary>
    ///     Arrays of <c>bool</c> and <c>int8</c> are decoded and encoded in bulk: a fixed struct through the fixed reader
    ///     and writer, a runtime-sized one member by member. Both match the runtime, and so do the written bytes.
    /// </summary>
    [TestMethod]
    public void BooleanAndSignedByteArrays_MatchTheRuntime()
    {
        const string LittleOne = "PointerSize = 1, Aligned = false, LittleEndian = true";
        RunParity("bulk-fixed", "struct root { bool flags[3]; int8 deltas[2]; uint8 tail; };", "Root = \"root\", " + LittleOne, "root", [1, 0, 1, 0xFF, 2, 9,], new Dictionary<string, int>(), null, null);
        RunParity("bulk-runtime", "struct root { uint8 n; bool flags[n]; int8 deltas[n]; };", "Root = \"root\", " + LittleOne, "root", [2, 1, 0, 0x80, 0x7F,], new Dictionary<string, int>(), null, null);
    }

    /// <summary>
    ///     Counts evaluated in the 128-bit domain: exact arithmetic on <c>uint64</c> fields and enums above 2^63, exact
    ///     hexadecimal literals, the element limit naming a count beyond Int32, and a <c>uint128</c> count beyond the
    ///     domain, all read (and written back) exactly as the runtime does.
    /// </summary>
    [TestMethod]
    public void WideCounts_MatchTheRuntime()
    {
        const string LittleEight = "PointerSize = 8, Aligned = false, LittleEndian = true";
        const string Root = "Root = \"root\", " + LittleEight;
        var none = new Dictionary<string, int>();
        RunParity("count-kernel", "struct root { uint64 next; uint8 data[next - 0xFFFF800000000FFE]; };", Root, "root", [0x00, 0x10, 0, 0, 0, 0x80, 0xFF, 0xFF, 1, 2,], none, null, null);
        RunParity("count-enum", "enum big : uint64 { X = 0xFFFFFFFFFFFFFFFE }; struct root { big e; uint8 data[e - 0xFFFFFFFFFFFFFFFC]; };", Root, "root", [0xFE, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 1, 2,], none, null, null);
        RunParity("count-pointer", "struct root { uint8 *p; uint8 data[p - 0xFFFFFFFE]; };", Root, "root", [0, 0, 0, 0, 1, 0, 0, 0, 1, 2,], none, new ReadOptions { DereferencePointers = false, }, null);
        RunParity("count-literal", "struct root { uint8 n; uint8 data[n + 0xFFFFFFFF - 4294967294]; };", Root, "root", [1, 7, 8,], none, null, null);
        RunParity("count-limit", "struct root { uint64 n; uint8 data[n]; };", Root, "root", [0, 0, 0, 0, 0, 1, 0, 0, 1,], none, null, "CStructReadLimitException");
        RunParity("count-beyond-domain", "struct root { uint128 n; uint8 data[n]; };", Root, "root", [0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0x80, 1,], none, null, "CStructReadException");
        RunParity("count-overflow", "struct root { uint64 a; uint8 data[a * a * a]; };", Root, "root", [0, 0, 0, 0, 0, 0, 0, 0x80, 1,], none, null, "CStructReadException");
        RunParity("count-at-pointer", "struct root { uint64 n; uint8 *p @count(n - 0xFFFFFFFFFFFFFFFD); };", Root, "root", [0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 16, 0, 0, 0, 0, 0, 0, 0, 1, 2,], none, null, null);
    }

    /// <summary>Runs one ad-hoc case through both readers: the value (or the expected failure) and the truncation sweep.</summary>
    /// <param name="id">The case name, used in assertion messages and to name the generated class.</param>
    /// <param name="definition">The layout source compiled by both the generator and the runtime.</param>
    /// <param name="arguments">The other <c>[CStructLayout]</c> arguments: the root and compile settings.</param>
    /// <param name="root">The root declaration to parse.</param>
    /// <param name="bytes">The input bytes.</param>
    /// <param name="fixtureVariables">The external variables for the read; an empty dictionary passes none.</param>
    /// <param name="options">The read limits, or <see langword="null"/> for the defaults.</param>
    /// <param name="expectedError">
    ///     The exception type name the runtime must raise, or <see langword="null"/> when the read succeeds.
    /// </param>
    internal static void RunParity(string id, string definition, string arguments, string root, byte[] bytes, IReadOnlyDictionary<string, int> fixtureVariables, ReadOptions? options, string? expectedError)
    {
        string className = "Fixture" + string.Concat(id.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
        string source = Header + "[CStructLayout(" + Literal(definition) + ", " + arguments + ")]\npublic static partial class " + className + " { }\n";
        GeneratorResult result = GeneratorRunner.Run(source).AssertClean();
        Assembly assembly = result.Load();
        Type generated = assembly.GetType("Parity." + className)!;
        var runtime = (CStruct)generated.GetProperty("Layout")!.GetValue(null)!;
        IReadOnlyDictionary<string, int>? variables = fixtureVariables.Count == 0 ? null : fixtureVariables;
        MethodInfo parse = generated.GetMethods().Single(method => method.Name == "Parse" + PascalRoot(root, generated) && method.GetParameters()[0].ParameterType == typeof(ReadOnlySpan<byte>));

        if (expectedError is not null)
        {
            Exception? runtimeError = Catch(() => runtime.ReadValue(bytes, root, variables, options));
            Exception? generatedError = Catch(() => Invoke(parse, bytes, variables, options));
            Assert.IsNotNull(runtimeError, id + ": the runtime did not fail");
            Assert.AreEqual(expectedError, runtimeError.GetType().Name, id);
            Assert.AreEqual(runtimeError.GetType(), generatedError?.GetType(), id + ": exception type (" + generatedError?.Message + ")");
            Assert.AreEqual(runtimeError.Message, generatedError!.Message, id);
        }
        else
        {
            object runtimeValue = runtime.ReadValue(bytes, root, variables, options)!;
            object generatedValue = Invoke(parse, bytes, variables, options);
            ParityComparer.AssertSame(runtimeValue, generatedValue, root, strict: true);
            WriteParity.AssertRoundTrip(id, generated, runtime, root, runtimeValue, generatedValue, variables);
        }

        // The truncation sweep: every prefix (every prefix of a small input; a spread of prefixes of a large one)
        // fails the same way (type and message) in both readers.
        foreach (int length in SweepLengths(bytes.Length))
        {
            byte[] prefix = bytes[..length];
            Exception? runtimeError = Catch(() => runtime.ReadValue(prefix, root, variables, options));
            Exception? generatedError = Catch(() => Invoke(parse, prefix, variables, options));
            Assert.AreEqual(runtimeError?.GetType(), generatedError?.GetType(), $"{id} truncated to {length}: exception type ({runtimeError?.Message} vs {generatedError?.Message})");
            Assert.AreEqual(runtimeError?.Message, generatedError?.Message, $"{id} truncated to {length}");
        }
    }

    /// <summary>
    ///     Chooses the prefix lengths to try: every prefix of an input up to 1024 bytes; for a larger one, the first
    ///     64, about 97 spread prefixes, and the last 16.
    /// </summary>
    /// <param name="length">The input length in bytes.</param>
    /// <returns>Prefix lengths shorter than the input.</returns>
    private static IEnumerable<int> SweepLengths(int length)
    {
        if (length <= 1024)
        {
            for (int prefix = 0; prefix < length; prefix++)
            {
                yield return prefix;
            }

            yield break;
        }

        for (int prefix = 0; prefix < 64; prefix++)
        {
            yield return prefix;
        }

        for (int prefix = 64; prefix < length; prefix += Math.Max(1, length / 97))
        {
            yield return prefix;
        }

        for (int prefix = Math.Max(64, length - 16); prefix < length; prefix++)
        {
            yield return prefix;
        }
    }

    /// <summary>
    ///     Calls a generated span reader through its <c>byte[]</c> overload, rethrowing its exceptions unwrapped.
    /// </summary>
    /// <param name="parse">The generated span reader.</param>
    /// <param name="bytes">The input.</param>
    /// <param name="variables">The caller variables, or <see langword="null"/> for none.</param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The generated value.</returns>
    private static object Invoke(MethodInfo parse, byte[] bytes, IReadOnlyDictionary<string, int>? variables, ReadOptions? options)
    {
        // A ReadOnlySpan<byte> parameter cannot be boxed; call the byte[] overload, which forwards to the span one.
        MethodInfo arrayOverload = parse.DeclaringType!.GetMethods().Single(method => method.Name == parse.Name && method.GetParameters()[0].ParameterType == typeof(byte[]));
        try
        {
            return arrayOverload.Invoke(null, [bytes, variables, options])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    /// <summary>Runs a read and returns the <see cref="CStructException"/> it throws.</summary>
    /// <param name="action">The read to run.</param>
    /// <returns>The exception, or <see langword="null"/> when the read succeeds; other exceptions propagate.</returns>
    private static Exception? Catch(Func<object?> action)
    {
        try
        {
            action();
            return null;
        }
        catch (CStructException exception)
        {
            return exception;
        }
    }

    /// <summary>
    ///     Finds the name suffix of the generated <c>Parse</c> method for a root, ignoring case and underscores.
    /// </summary>
    /// <param name="root">The root name in the layout.</param>
    /// <param name="generated">The generated layout class.</param>
    /// <returns>The suffix after <c>Parse</c>, or the root name when no method matches.</returns>
    private static string PascalRoot(string root, Type generated)
    {
        foreach (MethodInfo method in generated.GetMethods())
        {
            if (method.Name.StartsWith("Parse", StringComparison.Ordinal) && method.Name.Length > 5 && method.Name.Substring(5).Equals(root.Replace("_", string.Empty), StringComparison.OrdinalIgnoreCase))
            {
                return method.Name.Substring(5);
            }
        }

        return root;
    }

    /// <summary>
    ///     Quotes a layout as a regular C# string literal, escaping backslashes, quotes, and line breaks.
    /// </summary>
    /// <param name="definition">The layout text.</param>
    /// <returns>The literal, including its quotes.</returns>
    internal static string Literal(string definition)
    {
        return "\"" + definition.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    }
}
