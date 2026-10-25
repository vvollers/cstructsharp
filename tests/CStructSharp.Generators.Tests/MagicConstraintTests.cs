namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     <c>@magic</c> on the generated readers: every surface checks the magic first, fails as unsupported with the runtime's message
///     and offset, and <c>Matches</c> of a struct with a magic evaluates the magic alone.
/// </summary>
[TestClass]
public class MagicConstraintTests
{
    private const string FixedLayout = "struct sb { uint32 blocks @expect(blocks > 0, \\\"blocks\\\"); uint16 magic @magic(magic == 0xEF53, \\\"magic\\\", \\\"Not an ext volume\\\"); uint16 rev @expect(rev < 2, \\\"rev\\\"); };";

    private const string VariableLayout = "struct vb { uint8 n; uint8 pad[n]; uint32 blocks @expect(blocks > 0, \\\"blocks\\\"); uint16 magic @magic(magic == 0xEF53, \\\"magic\\\"); uint16 rev @expect(rev < 2, \\\"rev\\\"); };";

    private static readonly string Consumer = """"
        using System;
        using System.Collections.Generic;
        using CStructSharp;
        using CStructSharp.Diagnostics;

        namespace Demo;

        [CStructLayout("@FIXED@", Root = "sb")]
        public static partial class FixedFull { }

        [CStructLayout("@FIXED@", Root = "sb", Surface = GeneratedSurface.Views)]
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
                case "TryFixed": return FixedFull.TryParse(bytes, out FixedFull.Sb? value, out CStructException? failure) ? "ok" : Describe(failure!);
                case "Variable": VariableFull.Parse(bytes); return "ok";
                case "TryVariable": return VariableFull.TryParse(bytes, out VariableFull.Vb? other, out CStructException? otherFailure) ? "ok" : Describe(otherFailure!);
                case "Validate": new FixedViews.SbView(bytes).Validate(); return "ok";
                case "TryValidate": return new FixedViews.SbView(bytes).TryValidate(out CStructException? viewFailure) ? "ok" : Describe(viewFailure!);
                case "Collected":
                    {
                        var found = new List<CStructConstraintException>();
                        FixedFull.Parse(bytes, null, new ReadOptions { ConstraintViolations = found });
                        return string.Join(";", found.ConvertAll(violation => violation.Kind + "|" + violation.Label + "|" + violation.Offset));
                    }

                case "Matches": return FixedFull.Matches(bytes).ToString();
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

    /// <summary>Each surface reports what the runtime layout reports for the same bytes: the magic before a garbage field, then the held-back condition, then the later one.</summary>
    [TestMethod]
    public void EverySurface_ChecksTheMagicFirst_LikeTheRuntime()
    {
        var fixedRuntime = new CStruct(FixedLayout.Replace("\\\"", "\"", StringComparison.Ordinal));
        var variableRuntime = new CStruct(VariableLayout.Replace("\\\"", "\"", StringComparison.Ordinal));
        byte[][] fixedCases =
        [
            Fixed16(0, 0x1234, 0), Fixed16(0, 0xEF53, 5), Fixed16(1, 0xEF53, 5), Fixed16(1, 0xEF53, 1), Fixed16(1, 0x1234, 1),
        ];
        foreach (byte[] bytes in fixedCases)
        {
            string expected = Runtime(() => fixedRuntime.Parse(bytes, "sb"));
            foreach (string surface in new[] { "Fixed", "TryFixed", "Validate", "TryValidate" })
            {
                Assert.AreEqual(expected, Run(surface, bytes), surface + " " + Convert.ToHexString(bytes));
            }
        }

        byte[][] variableCases =
        [
            [2, 0, 0, 0, 0, 0, 0, 0x34, 0x12, 0, 0], [2, 0, 0, 1, 0, 0, 0, 0x53, 0xEF, 5, 0], [0, 0, 0, 0, 0, 0x53, 0xEF, 1, 0], [0, 1, 0, 0, 0, 0x53, 0xEF, 1, 0],
        ];
        foreach (byte[] bytes in variableCases)
        {
            string expected = Runtime(() => variableRuntime.Parse(bytes, "vb"));
            Assert.AreEqual(expected, Run("Variable", bytes), "Variable " + Convert.ToHexString(bytes));
            Assert.AreEqual(expected, Run("TryVariable", bytes), "TryVariable " + Convert.ToHexString(bytes));
        }
    }

    /// <summary>A collecting read lists the magic first, with the offsets of the runtime's collecting read.</summary>
    [TestMethod]
    public void CollectedViolations_MatchTheRuntime()
    {
        var runtime = new CStruct(FixedLayout.Replace("\\\"", "\"", StringComparison.Ordinal));
        byte[] bytes = Fixed16(0, 0x1234, 5);
        var found = new List<CStructConstraintException>();
        runtime.Parse(bytes, "sb", null, new ReadOptions { ConstraintViolations = found });
        Assert.AreEqual(string.Join(";", found.Select(violation => violation.Kind + "|" + violation.Label + "|" + violation.Offset)), Run("Collected", bytes));
        Assert.AreEqual("magic", found[0].Label);
    }

    /// <summary><c>Matches</c> of a struct with a magic evaluates the magic alone: an @expect that fails does not matter, a wrong magic does.</summary>
    [TestMethod]
    public void Matches_EvaluatesOnlyTheMagic()
    {
        Assert.AreEqual("True", Run("Matches", Fixed16(0, 0xEF53, 9)));
        Assert.AreEqual("False", Run("Matches", Fixed16(1, 0x1234, 0)));
        Assert.AreEqual("False", Run("Matches", [1, 2]));
    }

    /// <summary>Little-endian bytes of the fixed layout.</summary>
    /// <param name="blocks">The first field.</param>
    /// <param name="magic">The magic.</param>
    /// <param name="rev">The last field.</param>
    /// <returns>The eight bytes.</returns>
    private static byte[] Fixed16(uint blocks, ushort magic, ushort rev) => [.. BitConverter.GetBytes(blocks), .. BitConverter.GetBytes(magic), .. BitConverter.GetBytes(rev)];

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
