namespace CStructSharp.Generators.Tests;

using System;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     Generated code around <c>@expect</c> and <c>@supports</c> in the places a struct is not a plain sequence of members:
///     a view's <c>Validate()</c> beside an anonymous struct or union and at the reader's offsets, conditions that name a
///     dependent <c>#define</c>, run-time byte order over anonymous bodies, and the generated names the layout may use.
/// </summary>
[TestClass]
public class ConstraintReviewTests
{
    private const string UnionLayout = "struct p { uint8 version @expect(version == 1); union { uint32 a; float32 b; }; uint8 kind @expect(kind < 4); };";

    private const string StructLayout = "struct q { struct { uint8 v @expect(v < 4); uint8 w; }; uint8 tail @expect(tail != v); };";

    private const string PairLayout = "struct pair { uint32 a @expect(a != 0); uint32 b @expect(b > a); uint8 n; uint8 tail @expect(tail == n); };";

    private const string DefineLayout = "#define TOTAL 10\n#define UNIT 1\n#define PER (TOTAL / UNIT)\nstruct s { uint8 n @expect(n <= PER); };";

    private const string Consumer = """"
        using System.Collections.Generic;
        using CStructSharp;

        namespace Demo;

        [CStructLayout("@UNION@", Root = "p", Surface = GeneratedSurface.Views)]
        public static partial class ViewsP { }

        [CStructLayout("@STRUCT@", Root = "q", Surface = GeneratedSurface.Views)]
        public static partial class ViewsQ { }

        [CStructLayout("@PAIR@", Root = "pair", Surface = GeneratedSurface.Views)]
        public static partial class ViewsPair { }

        [CStructLayout("@DEFINE@", Root = "s", Surface = GeneratedSurface.Reader)]
        public static partial class DefineReader { }

        public static class Probe
        {
            public static void ValidateP(byte[] bytes) => new Demo.ViewsP.PView(bytes).Validate();

            public static void ValidateQ(byte[] bytes) => new Demo.ViewsQ.QView(bytes).Validate();

            public static void ValidatePair(byte[] bytes) => new Demo.ViewsPair.PairView(bytes).Validate();

            public static object ParseDefine(byte[] bytes, int unit)
                => Demo.DefineReader.ParseS(bytes, new Dictionary<string, int> { ["UNIT"] = unit }, null);
        }
        """";

    private static Assembly assembly = null!;

    /// <summary>Generates the layouts once.</summary>
    /// <param name="context">The test context.</param>
    [ClassInitialize]
    public static void Generate(TestContext context)
    {
        _ = context;
        string source = Consumer.
                        Replace("@UNION@", UnionLayout, StringComparison.Ordinal).
                        Replace("@STRUCT@", StructLayout, StringComparison.Ordinal).
                        Replace("@PAIR@", PairLayout, StringComparison.Ordinal).
                        Replace("@DEFINE@", DefineLayout.Replace("\n", "\\n", StringComparison.Ordinal), StringComparison.Ordinal);
        assembly = GeneratorRunner.Run(source).AssertClean().Load();
    }

    /// <summary>Calls a probe method and returns the failure it raised, or <see langword="null"/> when it returned.</summary>
    /// <param name="method">The probe method.</param>
    /// <param name="arguments">Its arguments.</param>
    /// <returns>The inner exception, or null.</returns>
    private static Exception? Failure(string method, params object[] arguments)
    {
        try
        {
            assembly.GetType("Demo.Probe")!.GetMethod(method)!.Invoke(null, arguments);
            return null;
        }
        catch (TargetInvocationException exception)
        {
            return exception.InnerException;
        }
    }

    /// <summary>The failure of the runtime layout for the same bytes.</summary>
    /// <param name="layout">The layout text.</param>
    /// <param name="root">The root declaration.</param>
    /// <param name="bytes">The bytes.</param>
    /// <returns>The constraint failure the runtime raised.</returns>
    private static CStructConstraintException Runtime(string layout, string root, byte[] bytes)
        => Assert.Throws<CStructConstraintException>(() => new CStruct(layout).Parse(bytes, root));

    /// <summary>Asserts that a view's failure is the runtime's: condition, kind, member and offset.</summary>
    /// <param name="expected">The runtime's failure.</param>
    /// <param name="actual">The view's failure.</param>
    /// <param name="label">The case, for the failure message.</param>
    private static void AssertSameFailure(CStructConstraintException expected, Exception? actual, string label)
    {
        var failure = Assert.IsInstanceOfType<CStructConstraintException>(actual, label);
        Assert.AreEqual(expected.Constraint, failure.Constraint, label);
        Assert.AreEqual(expected.Kind, failure.Kind, label);
        Assert.AreEqual(expected.Member, failure.Member, label);
        Assert.AreEqual(expected.Offset, failure.Offset, label);
    }

    /// <summary>A member after an anonymous union keeps its check in <c>Validate()</c>.</summary>
    [TestMethod]
    public void Validate_ChecksMembersAfterAnAnonymousUnion()
    {
        byte[] good = [1, 7, 0, 0, 0, 2];
        byte[] badKind = [1, 7, 0, 0, 0, 9];
        byte[] badVersion = [2, 7, 0, 0, 0, 2];
        Assert.IsNull(Failure("ValidateP", (object)good));
        AssertSameFailure(Runtime(UnionLayout, "p", badKind), Failure("ValidateP", (object)badKind), "kind after the union");
        AssertSameFailure(Runtime(UnionLayout, "p", badVersion), Failure("ValidateP", (object)badVersion), "version before the union");
    }

    /// <summary>The members of an anonymous struct are checked by <c>Validate()</c>, and a later condition can name them.</summary>
    [TestMethod]
    public void Validate_ChecksTheMembersOfAnAnonymousStruct_AndThoseAfterIt()
    {
        byte[] good = [1, 5, 2];
        byte[] badMember = [200, 5, 2];
        byte[] badTail = [3, 5, 3];
        Assert.IsNull(Failure("ValidateQ", (object)good));
        AssertSameFailure(Runtime(StructLayout, "q", badMember), Failure("ValidateQ", (object)badMember), "inside the anonymous struct");
        AssertSameFailure(Runtime(StructLayout, "q", badTail), Failure("ValidateQ", (object)badTail), "after it, naming its member");
    }

    /// <summary>A failure raised by <c>Validate()</c> reports the end of the failing field, as the runtime and the readers do.</summary>
    [TestMethod]
    public void Validate_ReportsTheOffsetOfTheReader()
    {
        byte[] equalValues = [5, 0, 0, 0, 5, 0, 0, 0, 2, 2];
        byte[] zeroFirst = [0, 0, 0, 0, 5, 0, 0, 0, 2, 2];
        byte[] badTail = [1, 0, 0, 0, 5, 0, 0, 0, 2, 3];
        CStructConstraintException second = Runtime(PairLayout, "pair", equalValues);
        Assert.AreEqual(8, second.Offset, "the end of b");
        AssertSameFailure(second, Failure("ValidatePair", (object)equalValues), "b > a");
        AssertSameFailure(Runtime(PairLayout, "pair", zeroFirst), Failure("ValidatePair", (object)zeroFirst), "a != 0");
        AssertSameFailure(Runtime(PairLayout, "pair", badTail), Failure("ValidatePair", (object)badTail), "tail == n");
    }

    /// <summary>A condition that names a dependent <c>#define</c> keeps its exception handler, so a division by zero is the read's expression failure.</summary>
    [TestMethod]
    public void ConditionOnADependentDefine_FailsAsAReadFailure()
    {
        Assert.IsNull(Failure("ParseDefine", new byte[] { 3 }, 1));
        var divide = Failure("ParseDefine", new byte[] { 3 }, 0);
        Assert.IsInstanceOfType<CStructReadException>(divide);
        Assert.IsNotInstanceOfType<DivideByZeroException>(divide);

        string source = GeneratorRunner.Run("using CStructSharp;\nnamespace Demo;\n[CStructLayout(\"" + DefineLayout.Replace("\n", "\\n", StringComparison.Ordinal) + "\", Root = \"s\", Surface = GeneratedSurface.Reader)]\npublic static partial class L { }\n").AssertClean().Source;
        StringAssert.Contains(source, "catch (global::System.Exception expressionFailure)");
    }

    /// <summary>A literal-only or member-only condition still needs no handler.</summary>
    [TestMethod]
    public void ConditionOnMembersAndLiterals_StillNeedsNoHandler()
    {
        string source = GeneratorRunner.Run("using CStructSharp;\nnamespace Demo;\n[CStructLayout(\"#define LIMIT 9\\nstruct s { uint8 a; uint8 b @expect(b <= LIMIT && b != a); };\", Root = \"s\", Surface = GeneratedSurface.Reader)]\npublic static partial class L { }\n").AssertClean().Source;
        Assert.DoesNotContain("catch (global::System.Exception expressionFailure)", source);
    }

    /// <summary>Anonymous bodies share the empty name and still pair up between the two byte orders.</summary>
    [TestMethod]
    public void RuntimeByteOrder_PairsAnonymousBodiesByPosition()
    {
        GeneratorResult result = GeneratorRunner.Run("using CStructSharp;\nnamespace Demo;\n[CStructLayout(\"struct a { union { struct { uint8 x; uint8 y; }; struct { uint16 w; }; }; }; struct b { struct { uint16 w; }; uint8 z; };\", Root = \"a\", RuntimeByteOrder = true, Surface = GeneratedSurface.Reader)]\npublic static partial class L { }\n");
        result.AssertClean();
        Assert.IsEmpty(result.DiagnosticsWithId("CSG013"));
    }

    /// <summary>The inline-array container name is reserved only where the class generates it.</summary>
    [TestMethod]
    public void InlineArraysName_IsReservedOnlyForValueTypes()
    {
        const string Header = "using CStructSharp;\nnamespace Demo;\n";
        GeneratorRunner.Run(Header + "[CStructLayout(\"struct inline_arrays { uint8 a[4]; };\", Root = \"inline_arrays\")]\npublic static partial class AsClass { }\n").AssertClean();
        GeneratorRunner.Run(Header + "[CStructLayout(\"struct x { uint8 a; };\", Root = \"x\")]\npublic static partial class InlineArrays { }\n").AssertClean();

        GeneratorResult valueTypes = GeneratorRunner.Run(Header + "[CStructLayout(\"struct inline_arrays { uint8 a[4]; };\", Root = \"inline_arrays\", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Reader)]\npublic static partial class AsStruct { }\n");
        StringAssert.Contains(valueTypes.DiagnosticsWithId("CSG003").Single().GetMessage(), "InlineArrays");
    }
}
