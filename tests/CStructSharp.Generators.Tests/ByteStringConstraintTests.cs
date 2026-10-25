namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     Byte-string comparisons (<c>oem == "NTFS    "</c>) on the generated readers: every surface fails and succeeds as the runtime
///     layout does for the same bytes, and <c>Matches</c> evaluates the comparison without building a value.
/// </summary>
[TestClass]
public class ByteStringConstraintTests
{
    private const string FixedLayout = "struct boot { uint8 oem[8] @magic(oem == \\\"NTFS    \\\", \\\"ntfs.oem\\\"); char vol[4] @expect(vol != \\\"XXXX\\\", \\\"vol\\\"); uint32 sectors @expect(sectors > 0, \\\"sectors\\\"); };";

    private const string VariableLayout = "struct vb { uint8 n; uint8 pad[n]; uint8 sig[4] @expect(sig == b\\\"\\\\x7fELF\\\", \\\"elf\\\"); uint8 name[2] @expect(name == \\\"AB\\\" || name == \\\"CD\\\", \\\"name\\\"); };";

    private static readonly string Consumer = """"
        using System;
        using System.Collections.Generic;
        using CStructSharp;
        using CStructSharp.Diagnostics;

        namespace Demo;

        [CStructLayout("@FIXED@", Root = "boot")]
        public static partial class FixedFull { }

        [CStructLayout("@FIXED@", Root = "boot", Surface = GeneratedSurface.Views)]
        public static partial class FixedViews { }

        [CStructLayout("@VARIABLE@", Root = "vb")]
        public static partial class VariableFull { }

        public static class Calls
        {
            public static string Run(string surface, byte[] bytes)
            {
                try
                {
                    return Read(surface, bytes);
                }
                catch (CStructException exception)
                {
                    return Describe(exception);
                }
            }

            private static string Describe(Exception exception)
                => exception is CStructConstraintException constraint ? constraint.Kind + "|" + constraint.Label + "|" + constraint.Offset + "|" + constraint.Message : exception.GetType().Name + "|" + exception.Message;

            private static string Read(string surface, byte[] bytes)
            {
                switch (surface)
                {
                case "Fixed": FixedFull.Parse(bytes); return "ok";
                case "TryFixed": return FixedFull.TryParse(bytes, out FixedFull.Boot? value, out CStructException? failure) ? "ok" : Describe(failure!);
                case "Variable": VariableFull.Parse(bytes); return "ok";
                case "TryVariable": return VariableFull.TryParse(bytes, out VariableFull.Vb? other, out CStructException? otherFailure) ? "ok" : Describe(otherFailure!);
                case "Validate": new FixedViews.BootView(bytes).Validate(); return "ok";
                case "TryValidate": return new FixedViews.BootView(bytes).TryValidate(out CStructException? viewFailure) ? "ok" : Describe(viewFailure!);
                case "Collected":
                    {
                        var found = new List<CStructConstraintException>();
                        FixedFull.Parse(bytes, null, new ReadOptions { ConstraintViolations = found });
                        return string.Join(";", found.ConvertAll(violation => violation.Kind + "|" + violation.Label + "|" + violation.Offset));
                    }

                case "Matches": return FixedFull.Matches(bytes).ToString();
                case "Trimmed": FixedFull.Parse(bytes, null, new ReadOptions { TrimFixedText = true }); return "ok";
                default: throw new ArgumentException(surface);
                }
            }
        }
        """";

    private static Assembly assembly = null!;

    /// <summary>Generates the layouts once.</summary>
    /// <param name="context">The test context.</param>
    [ClassInitialize]
    public static void Generate(TestContext context)
    {
        _ = context;
        string source = Consumer.Replace("@FIXED@", FixedLayout, StringComparison.Ordinal).Replace("@VARIABLE@", VariableLayout, StringComparison.Ordinal);
        assembly = GeneratorRunner.Run(source).AssertClean().Load();
    }

    /// <summary>The layout text as the runtime layout parses it: the consumer's escapes removed.</summary>
    /// <param name="text">The text as embedded in the consumer's attribute.</param>
    /// <returns>The layout text.</returns>
    private static string RuntimeText(string text) => text.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\", "\\", StringComparison.Ordinal);

    /// <summary>Little-endian bytes of the fixed layout.</summary>
    /// <param name="oem">The eight-byte tag.</param>
    /// <param name="vol">The four-byte text.</param>
    /// <param name="sectors">The last field.</param>
    /// <returns>The sixteen bytes.</returns>
    private static byte[] Boot(string oem, string vol, uint sectors) => [.. System.Text.Encoding.Latin1.GetBytes(oem), .. System.Text.Encoding.Latin1.GetBytes(vol), .. BitConverter.GetBytes(sectors)];

    /// <summary>Each surface reports what the runtime layout reports for the same bytes.</summary>
    [TestMethod]
    public void EverySurface_ComparesBytes_LikeTheRuntime()
    {
        var fixedRuntime = new CStruct(RuntimeText(FixedLayout));
        var variableRuntime = new CStruct(RuntimeText(VariableLayout));
        byte[][] fixedCases =
        [
            Boot("NTFS    ", "ABCD", 1), Boot("NTFS   X", "ABCD", 1), Boot("XXXXXXXX", "ABCD", 1), Boot("NTFS    ", "XXXX", 1), Boot("NTFS    ", "ABCD", 0), Boot("NTFS    ", "XXXX", 0),
        ];
        foreach (byte[] bytes in fixedCases)
        {
            string expected = Runtime(() => fixedRuntime.Parse(bytes, "boot"));
            foreach (string surface in new[] { "Fixed", "TryFixed", "Validate", "TryValidate" })
            {
                Assert.AreEqual(expected, Run(surface, bytes), surface + " " + Convert.ToHexString(bytes));
            }
        }

        byte[][] variableCases =
        [
            [2, 0, 0, 0x7F, 0x45, 0x4C, 0x46, 0x41, 0x42], [2, 0, 0, 0x7F, 0x45, 0x4C, 0x46, 0x43, 0x44], [2, 0, 0, 0x7F, 0x45, 0x4C, 0x46, 0x43, 0x45],
            [2, 0, 0, 0x7F, 0x45, 0x4C, 0x47, 0x41, 0x42], [0, 0x7F, 0x45, 0x4C, 0x46, 0x41, 0x42], [0, 0x7F, 0x45, 0x4C, 0x46, 0x41],
        ];
        foreach (byte[] bytes in variableCases)
        {
            string expected = Runtime(() => variableRuntime.Parse(bytes, "vb"));
            Assert.AreEqual(expected, Run("Variable", bytes), "Variable " + Convert.ToHexString(bytes));
            Assert.AreEqual(expected, Run("TryVariable", bytes), "TryVariable " + Convert.ToHexString(bytes));
        }
    }

    /// <summary>A trimming read compares the raw bytes of a char array, as the runtime does.</summary>
    [TestMethod]
    public void TrimFixedText_DoesNotChangeTheComparison()
    {
        byte[] bytes = Boot("NTFS    ", "AB\0\0", 1);
        Assert.AreEqual("ok", Run("Trimmed", bytes));
    }

    /// <summary>A collecting read lists the failed comparisons in the runtime's order with its offsets.</summary>
    [TestMethod]
    public void CollectedViolations_MatchTheRuntime()
    {
        var runtime = new CStruct(RuntimeText(FixedLayout));
        byte[] bytes = Boot("XXXXXXXX", "XXXX", 0);
        var found = new List<CStructConstraintException>();
        runtime.Parse(bytes, "boot", null, new ReadOptions { ConstraintViolations = found });
        Assert.AreEqual(string.Join(";", found.Select(violation => violation.Kind + "|" + violation.Label + "|" + violation.Offset)), Run("Collected", bytes));
        Assert.AreEqual(3, found.Count);
    }

    /// <summary><c>Matches</c> of a struct with a byte-string magic answers from the bytes alone.</summary>
    [TestMethod]
    public void Matches_ComparesTheBytes()
    {
        Assert.AreEqual("True", Run("Matches", Boot("NTFS    ", "XXXX", 0)));
        Assert.AreEqual("False", Run("Matches", Boot("NTFS   X", "ABCD", 1)));
        Assert.AreEqual("False", Run("Matches", Boot("NTFS", string.Empty, 0)[..6]));
    }

    /// <summary>Describes what the runtime layout does for a read, in the format the generated consumer uses.</summary>
    /// <param name="read">The read.</param>
    /// <returns>The description, or <c>ok</c>.</returns>
    private static string Runtime(Action read)
    {
        try
        {
            read();
            return "ok";
        }
        catch (CStructConstraintException constraint)
        {
            return constraint.Kind + "|" + constraint.Label + "|" + constraint.Offset + "|" + constraint.Message;
        }
        catch (CStructException exception)
        {
            return exception.GetType().Name + "|" + exception.Message;
        }
    }

    /// <summary>Calls the consumer's <c>Run</c> method.</summary>
    /// <param name="surface">The surface the method switches on.</param>
    /// <param name="bytes">The input bytes.</param>
    /// <returns>The description of the outcome.</returns>
    private static string Run(string surface, byte[] bytes)
        => (string)assembly.GetType("Demo.Calls")!.GetMethod("Run")!.Invoke(null, [surface, bytes])!;
}
