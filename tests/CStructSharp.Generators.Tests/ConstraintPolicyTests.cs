namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     <see cref="ConstraintPolicy.Skip"/> on the generated readers: every entry point of every surface reads a value that breaks its
///     conditions when the options or limits skip the constraints, nothing is collected, and the same read with a violation
///     collection is an <see cref="ArgumentException"/>.
/// </summary>
[TestClass]
public class ConstraintPolicyTests
{
    private const string Fixed = "struct fx { uint32 a @expect(a != 0, \\\"fx.a\\\"); uint32 b @supports(b < 100); uint32 c; };";

    private const string Variable = "struct vx { uint8 n @expect(n != 0); uint8 data[n]; uint8 tail @supports(tail != 5); };";

    private const string One = "struct one { uint32 magic @expect(magic == 0xEF53); };";

    private static readonly string Consumer = """"
        using System;
        using System.Buffers;
        using System.Collections.Generic;
        using System.IO;
        using System.Linq;
        using System.Threading.Tasks;
        using CStructSharp;
        using CStructSharp.Diagnostics;

        namespace Demo;

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Reader)]
        public static partial class FixedReader { }

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Parser, Views = false)]
        public static partial class FixedParser { }

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Full)]
        public static partial class FixedFull { }

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Reader, Shape = GeneratedShape.Struct)]
        public static partial class FixedValue { }

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Reader, RuntimeByteOrder = true)]
        public static partial class FixedOrder { }

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Views)]
        public static partial class FixedViews { }

        [CStructLayout("@VARIABLE@", Root = "vx", Surface = GeneratedSurface.Full)]
        public static partial class VariableFull { }

        [CStructLayout("@ONE@", Root = "one", Surface = GeneratedSurface.Reader, Shape = GeneratedShape.Scalar)]
        public static partial class OneScalar { }

        public static class Calls
        {
            private static readonly ReadOptions Skip = new() { Constraints = ConstraintPolicy.Skip };
            private static readonly ReadLimits SkipLimits = new() { Constraints = ConstraintPolicy.Skip };

            public static string Run(string surface, byte[] bytes)
            {
                try
                {
                    return Read(surface, bytes);
                }
                catch (Exception exception)
                {
                    return exception.GetType().Name;
                }
            }

            private static string Read(string surface, byte[] bytes)
            {
                switch (surface)
                {
                case "Reader": return FixedReader.Parse(bytes, null, Skip).B.ToString();
                case "ReaderLimits": return FixedReader.Parse(bytes, SkipLimits).B.ToString();
                case "ReaderSpanLimits": return FixedReader.Parse(new ReadOnlySpan<byte>(bytes), in SkipLimits).B.ToString();
                case "Parser": return FixedParser.Parse(bytes, null, Skip).B.ToString();
                case "Full": return FixedFull.Parse(bytes, null, Skip).B.ToString();
                case "Struct": return FixedValue.Parse(bytes, null, Skip).B.ToString();
                case "StructLimits": return FixedValue.Parse(bytes, SkipLimits).B.ToString();
                case "Order": return FixedOrder.Parse(bytes, true, null, Skip).B.ToString();
                case "OrderLimits": return FixedOrder.Parse(bytes, true, SkipLimits).B.ToString();
                case "Stream": return FixedFull.Parse(new MemoryStream(bytes), null, Skip).B.ToString();
                case "Sequence": return FixedFull.Parse(new ReadOnlySequence<byte>(bytes), null, Skip).B.ToString();
                case "Async": return FixedFull.ParseAsync(new MemoryStream(bytes), null, Skip).AsTask().GetAwaiter().GetResult().B.ToString();
                case "Records": return FixedFull.Records(bytes, Skip).Single().B.ToString();
                case "RecordsStream": return FixedFull.Records(new MemoryStream(bytes), Skip).Single().B.ToString();
                case "ViewObject": return new FixedFull.FxView(bytes, Skip).ToObject().B.ToString();
                case "ViewValidate": new FixedViews.FxView(bytes, Skip).Validate(); return "valid";
                case "ViewTryValidate": return new FixedViews.FxView(bytes, Skip).TryValidate(out CStructException? viewFailure).ToString() + (viewFailure is null);
                case "TryParse": return FixedFull.TryParse(bytes, out FixedFull.Fx? tried, out CStructException? triedFailure, null, Skip).ToString() + (triedFailure is null) + tried!.B;
                case "TryParseLimits": return FixedReader.TryParse(new ReadOnlySpan<byte>(bytes), out FixedReader.Fx? limited, out CStructException? limitedFailure, SkipLimits).ToString() + (limitedFailure is null);
                case "TryParseStruct": return FixedValue.TryParse(bytes, out FixedValue.Fx structValue, out CStructException? structFailure, null, Skip).ToString() + (structFailure is null) + structValue.B;
                case "VariableSpan": return VariableFull.Parse(bytes, null, Skip).Tail.ToString();
                case "VariableLimits": return VariableFull.Parse(bytes, SkipLimits).Tail.ToString();
                case "VariableStream": return VariableFull.Parse(new MemoryStream(bytes), null, Skip).Tail.ToString();
                case "VariableRecords": return VariableFull.Records(bytes, Skip).Single().Tail.ToString();
                case "Scalar": return OneScalar.Parse(bytes, Skip).ToString();
                case "ScalarLimits": return OneScalar.Parse(bytes, SkipLimits).ToString();
                case "ScalarTry": return OneScalar.TryParse(bytes, out uint scalar, out CStructException? scalarFailure, SkipLimits).ToString() + (scalarFailure is null) + scalar;
                case "Matches": return FixedFull.Matches(bytes).ToString();
                case "Collecting": return FixedFull.Parse(bytes, null, new ReadOptions { Constraints = ConstraintPolicy.Skip, ConstraintViolations = new List<CStructConstraintException>() }).B.ToString();
                case "CollectingValidate": new FixedFull.FxView(bytes, Skip).Validate(new List<CStructConstraintException>()); return "valid";
                case "NotSkipped": return FixedFull.Parse(bytes, null, new ReadOptions { Constraints = ConstraintPolicy.Enforce }).B.ToString();
                case "NotSkippedLimits": return FixedFull.Parse(bytes, new ReadLimits()).B.ToString();
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
        string source = Consumer
            .Replace("@FIXED@", Fixed, StringComparison.Ordinal)
            .Replace("@VARIABLE@", Variable, StringComparison.Ordinal)
            .Replace("@ONE@", One, StringComparison.Ordinal);
        assembly = GeneratorRunner.Run(source).AssertClean().Load();
    }

    /// <summary>Every entry point of a fixed struct reads the violating value when the read skips its constraints, from options and from limits.</summary>
    [TestMethod]
    public void Skip_ReadsAViolatingFixedStruct_OnEverySurface()
    {
        byte[] bytes = [0, 0, 0, 0, 200, 0, 0, 0, 3, 0, 0, 0];
        foreach (string surface in new[] { "Reader", "ReaderLimits", "ReaderSpanLimits", "Parser", "Full", "Struct", "StructLimits", "Order", "OrderLimits", "Stream", "Sequence", "Async", "Records", "RecordsStream", "ViewObject" })
        {
            Assert.AreEqual("200", Run(surface, bytes), surface);
        }

        Assert.AreEqual("valid", Run("ViewValidate", bytes));
        Assert.AreEqual("TrueTrue", Run("ViewTryValidate", bytes));
        Assert.AreEqual("TrueTrue200", Run("TryParse", bytes));
        Assert.AreEqual("TrueTrue", Run("TryParseLimits", bytes));
        Assert.AreEqual("TrueTrue200", Run("TryParseStruct", bytes));
    }

    /// <summary>The member-by-member readers skip their conditions too.</summary>
    [TestMethod]
    public void Skip_ReadsAViolatingVariableStruct()
    {
        byte[] bytes = [0, 5];
        foreach (string surface in new[] { "VariableSpan", "VariableLimits", "VariableStream", "VariableRecords" })
        {
            Assert.AreEqual("5", Run(surface, bytes), surface);
        }

        Assert.AreEqual("0", Run("Scalar", [0, 0, 0, 0]));
        Assert.AreEqual("0", Run("ScalarLimits", [0, 0, 0, 0]));
        Assert.AreEqual("TrueTrue0", Run("ScalarTry", [0, 0, 0, 0]));
    }

    /// <summary>Without the policy the same bytes still fail, and <c>Matches</c> takes no read settings so it still evaluates its conditions.</summary>
    [TestMethod]
    public void Enforce_StillFails_AndMatchesIgnoresThePolicy()
    {
        byte[] bytes = [0, 0, 0, 0, 200, 0, 0, 0, 3, 0, 0, 0];
        Assert.AreEqual(nameof(CStructConstraintException), Run("NotSkipped", bytes));
        Assert.AreEqual(nameof(CStructConstraintException), Run("NotSkippedLimits", bytes));
        Assert.AreEqual("False", Run("Matches", bytes));
    }

    /// <summary>Skipping and collecting in one read is an <see cref="ArgumentException"/>, for a read and for a view's <c>Validate</c>.</summary>
    [TestMethod]
    public void SkipWithViolations_IsAnArgumentException()
    {
        byte[] bytes = [1, 0, 0, 0, 1, 0, 0, 0, 3, 0, 0, 0];
        Assert.AreEqual(nameof(ArgumentException), Run("Collecting", bytes));
        Assert.AreEqual(nameof(ArgumentException), Run("CollectingValidate", bytes));
    }

    /// <summary>Calls the consumer's <c>Run</c> method.</summary>
    /// <param name="surface">The surface the method switches on.</param>
    /// <param name="bytes">The input bytes.</param>
    /// <returns>The value the read produced, or the type name of the exception it raised.</returns>
    private static string Run(string surface, byte[] bytes)
        => (string)assembly.GetType("Demo.Calls")!.GetMethod("Run")!.Invoke(null, [surface, bytes])!;
}
