namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     <c>@expect</c> and <c>@supports</c> in generated readers: for every input the generated reader (member by member,
///     on the full, reader and parser surfaces) succeeds with the runtime's value or fails with the same exception, kind,
///     condition, message and field as the runtime, and a layout with conditions never takes the fixed reader.
/// </summary>
[TestClass]
public class ConstraintParityTests
{
    private const string Layout = "enum kind : uint8 { Ok = 1, Bad = 2 }; struct e { uint8 id @expect(id != 0); }; struct sb { uint16 magic @expect(magic == 0xEF53); uint8 version @supports(version <= 2); uint8 n @expect(n <= 3); e items[n]; kind k @expect(k != kind.Bad); uint8 lo:4 @expect(lo != 15) @supports(lo < 9); uint8 hi:4; uint8 d; uint8 q @expect(100 / d > 1); };";

    private const string Consumer = """"
        using CStructSharp;

        namespace Demo;

        [CStructLayout("@LAYOUT@", Root = "sb")]
        public static partial class FullSb { }

        [CStructLayout("@LAYOUT@", Root = "sb", Surface = GeneratedSurface.Reader)]
        public static partial class ReaderSb { }

        [CStructLayout("@LAYOUT@", Root = "sb", Surface = GeneratedSurface.Parser)]
        public static partial class ParserSb { }

        [CStructLayout("struct fixedlike { uint32 a @expect(a != 0); uint32 b; };", Root = "fixedlike", Surface = GeneratedSurface.Reader)]
        public static partial class FixedLike { }

        [CStructLayout("struct pair { uint32 a @expect(a != 0) @supports(a < 10); uint32 b @expect(b > a); uint8 n @expect(n <= LIMIT); uint8 v[n]; uint8 tail @expect(tail == n); };", Root = "pair", Surface = GeneratedSurface.Views)]
        public static partial class ViewsPair { }

        public static class ViewProbe
        {
            public static void Validate(byte[] bytes) => new Demo.ViewsPair.PairView(bytes, null, new System.Collections.Generic.Dictionary<string, int> { ["LIMIT"] = 3 }).Validate();
        }

        public static class ParserProbe
        {
            public static object Parse(byte[] bytes) => Demo.ParserSb.Parse(bytes);
        }
        """";

    private static Assembly assembly = null!;

    /// <summary>Generates the layouts once.</summary>
    /// <param name="context">The test context.</param>
    [ClassInitialize]
    public static void Generate(TestContext context)
    {
        _ = context;
        GeneratorResult result = GeneratorRunner.Run(Consumer.Replace("@LAYOUT@", Layout, StringComparison.Ordinal)).AssertClean();
        assembly = result.Load();
    }

    /// <summary>The generated root reader of a class, called by reflection with a byte array.</summary>
    /// <param name="type">The generated class.</param>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The value, or the exception the reader threw.</returns>
    private static object Read(string type, byte[] bytes)
    {
        MethodInfo parse = type == "ParserSb"
                               ? assembly.GetType("Demo.ParserProbe")!.GetMethod("Parse")!
                               : assembly.GetType("Demo." + type)!.GetMethods().First(method => method.Name == "Parse" && method.GetParameters().Length == 2 && method.GetParameters()[0].ParameterType == typeof(byte[]) && method.GetParameters()[1].ParameterType == typeof(ReadOptions));
        try
        {
            return parse.Invoke(null, type == "ParserSb" ? [bytes] : [bytes, null])!;
        }
        catch (TargetInvocationException exception)
        {
            return exception.InnerException!;
        }
    }

    /// <summary>Every input reads or fails the way the runtime does, on every surface.</summary>
    [TestMethod]
    public void GeneratedReaders_MatchTheRuntime()
    {
        var runtime = new CStruct(Layout);
        (string Name, byte[] Bytes)[] inputs =
        [
            ("ok", [0x53, 0xEF, 2, 2, 7, 8, 1, 0x18, 4, 2]),
            ("bad magic", [0x54, 0xEF, 2, 2, 7, 8, 1, 0x18, 4, 2]),
            ("unsupported version", [0x53, 0xEF, 3, 2, 7, 8, 1, 0x18, 4, 2]),
            ("count too large", [0x53, 0xEF, 2, 9, 7, 8, 1, 0x18, 4, 2]),
            ("zero element", [0x53, 0xEF, 2, 2, 7, 0, 1, 0x18, 4, 2]),
            ("bad enum", [0x53, 0xEF, 2, 2, 7, 8, 2, 0x18, 4, 2]),
            ("lo 15", [0x53, 0xEF, 2, 2, 7, 8, 1, 0x1F, 4, 2]),
            ("lo 9 unsupported", [0x53, 0xEF, 2, 2, 7, 8, 1, 0x19, 4, 2]),
            ("division by zero", [0x53, 0xEF, 2, 2, 7, 8, 1, 0x18, 0, 2]),
            ("last condition", [0x53, 0xEF, 2, 2, 7, 8, 1, 0x18, 4, 1]),
            ("short", [0x53, 0xEF, 2]),
        ];
        foreach ((string name, byte[] bytes) in inputs)
        {
            object expected;
            try
            {
                expected = runtime.Parse(bytes, "sb");
            }
            catch (CStructException exception)
            {
                expected = exception;
            }

            foreach (string type in new[] { "FullSb", "ReaderSb", "ParserSb" })
            {
                object actual = Read(type, bytes);
                string where = name + " / " + type;
                if (expected is CStructException expectedFailure)
                {
                    Assert.IsInstanceOfType<CStructException>(actual, where);
                    var actualFailure = (CStructException)actual;
                    Assert.AreEqual(expectedFailure.GetType(), actualFailure.GetType(), where);
                    Assert.AreEqual(expectedFailure.Message, actualFailure.Message, where);
                    if (expectedFailure is CStructConstraintException expectedConstraint)
                    {
                        var actualConstraint = (CStructConstraintException)actualFailure;
                        Assert.AreEqual(expectedConstraint.Kind, actualConstraint.Kind, where);
                        Assert.AreEqual(expectedConstraint.Constraint, actualConstraint.Constraint, where);
                        Assert.AreEqual(expectedConstraint.Member, actualConstraint.Member, where);
                    }
                }
                else
                {
                    ParityComparer.AssertSame(expected, actual, "sb", strict: false);
                }
            }
        }
    }

    /// <summary>A view never checks on its own; <c>Validate()</c> checks the conditions it can evaluate, with the reader's failures, including a condition that names an earlier member, a caller variable and a member after an array.</summary>
    [TestMethod]
    public void ViewValidate_ChecksTheConditionsItCanEvaluate()
    {
        MethodInfo validate = assembly.GetType("Demo.ViewProbe")!.GetMethod("Validate")!;
        // Runs the view's Validate over the bytes and rethrows the failure it raised.
        void Run(byte[] bytes)
        {
            try
            {
                validate.Invoke(null, [bytes]);
            }
            catch (TargetInvocationException exception)
            {
                throw exception.InnerException!;
            }
        }

        // a=1 b=2 n=2 v=[5,6] tail=2
        Run([1, 0, 0, 0, 2, 0, 0, 0, 2, 5, 6, 2]);
        Assert.AreEqual(ConstraintKind.Malformed, Assert.Throws<CStructConstraintException>(() => Run([0, 0, 0, 0, 2, 0, 0, 0, 2, 5, 6, 2])).Kind);
        Assert.AreEqual(ConstraintKind.Unsupported, Assert.Throws<CStructConstraintException>(() => Run([10, 0, 0, 0, 20, 0, 0, 0, 2, 5, 6, 2])).Kind);
        Assert.AreEqual("b > a", Assert.Throws<CStructConstraintException>(() => Run([5, 0, 0, 0, 5, 0, 0, 0, 2, 5, 6, 2])).Constraint);
        Assert.AreEqual("n <= LIMIT", Assert.Throws<CStructConstraintException>(() => Run([1, 0, 0, 0, 2, 0, 0, 0, 4, 5, 6, 7, 8, 4])).Constraint);
        Assert.AreEqual("tail == n", Assert.Throws<CStructConstraintException>(() => Run([1, 0, 0, 0, 2, 0, 0, 0, 2, 5, 6, 9])).Constraint);
    }

    /// <summary>A struct with a condition has no fixed reader, so even a layout of two plain integers checks it.</summary>
    [TestMethod]
    public void ConstrainedStruct_NeverTakesTheFixedReader()
    {
        object failure = Read("FixedLike", [0, 0, 0, 0, 1, 0, 0, 0]);
        Assert.IsInstanceOfType<CStructConstraintException>(failure);
        Assert.IsNotNull(Read("FixedLike", [1, 0, 0, 0, 0, 0, 0, 0]).GetType().GetProperty("A"));
        string source = GeneratorRunner.Run("using CStructSharp;\nnamespace Demo;\n[CStructLayout(\"struct fixedlike { uint32 a @expect(a != 0); uint32 b; };\", Root = \"fixedlike\", Surface = GeneratedSurface.Reader)]\npublic static partial class L { }\n").AssertClean().Source;
        Assert.DoesNotContain("ReadFixedlikeFixed", source);
        StringAssert.Contains(source, "FailConstraint");
    }
}
