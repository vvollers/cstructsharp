namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     <c>ConstraintFailures = FormatExceptions</c>: the generated readers of every surface throw <see cref="InvalidDataException"/> for
///     <c>@expect</c> and <see cref="NotSupportedException"/> for <c>@supports</c>, with the constraint failure as the inner exception and the
///     layout's own message as the text; the default mode, the non-throwing forms and the runtime layout keep the constraint exception.
/// </summary>
[TestClass]
public class FormatExceptionTests
{
    private const string Fixed = "struct fx { uint32 a @expect(a != 0, \\\"fx.a\\\", \\\"The magic is missing.\\\"); uint32 b @supports(b < 100, \\\"fx.b\\\", \\\"Revision \\\\\\\"b\\\\\\\" is too new.\\\"); uint32 c @expect(c != 7, \\\"fx.c\\\"); uint32 d @expect(d != 9); };";

    private const string Variable = "struct vx { uint8 n @expect(n != 0, \\\"vx.n\\\", \\\"Empty record.\\\"); uint8 data[n]; uint8 tail @supports(tail != 5, \\\"vx.tail\\\", \\\"Tail 5 is reserved.\\\"); };";

    private const string One = "struct one { uint32 magic @expect(magic == 0xEF53, \\\"one.magic\\\", \\\"Not an ext superblock.\\\"); };";

    private static readonly string RuntimeFixed = Fixed.Replace("\\\"", "\"", StringComparison.Ordinal).Replace("\\\\\"", "\\\"", StringComparison.Ordinal);

    private static readonly string Consumer = """"
        using System;
        using System.Buffers;
        using System.IO;
        using System.Linq;
        using System.Threading.Tasks;
        using CStructSharp;
        using CStructSharp.Diagnostics;

        namespace Demo;

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Reader, ConstraintFailures = ConstraintFailureMode.FormatExceptions)]
        public static partial class FixedReader { }

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Parser, Views = false, ConstraintFailures = ConstraintFailureMode.FormatExceptions)]
        public static partial class FixedParser { }

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Full, ConstraintFailures = ConstraintFailureMode.FormatExceptions)]
        public static partial class FixedFull { }

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Reader, Shape = GeneratedShape.Struct, ConstraintFailures = ConstraintFailureMode.FormatExceptions)]
        public static partial class FixedValue { }

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Reader, RuntimeByteOrder = true, ConstraintFailures = ConstraintFailureMode.FormatExceptions)]
        public static partial class FixedOrder { }

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Views, ConstraintFailures = ConstraintFailureMode.FormatExceptions)]
        public static partial class FixedViews { }

        [CStructLayout("@FIXED@", Root = "fx", Surface = GeneratedSurface.Full)]
        public static partial class FixedDefault { }

        [CStructLayout("@VARIABLE@", Root = "vx", Surface = GeneratedSurface.Full, Enumerators = true, ConstraintFailures = ConstraintFailureMode.FormatExceptions)]
        public static partial class VariableFull { }

        [CStructLayout("@VARIABLE@", Root = "vx", Surface = GeneratedSurface.Full)]
        public static partial class VariableDefault { }

        [CStructLayout("@ONE@", Root = "one", Surface = GeneratedSurface.Reader, Shape = GeneratedShape.Scalar, ConstraintFailures = ConstraintFailureMode.FormatExceptions)]
        public static partial class OneScalar { }

        public static class Calls
        {
            public static Exception? Catch(Action action)
            {
                try
                {
                    action();
                    return null;
                }
                catch (Exception exception)
                {
                    return exception;
                }
            }

            public static Exception? Parse(string surface, byte[] bytes) => surface switch
            {
                "Reader" => Catch(() => FixedReader.Parse(bytes)),
                "ReaderLimits" => Catch(() => FixedReader.Parse(bytes, new ReadLimits { OffsetBase = 40 })),
                "ReaderOptions" => Catch(() => FixedReader.Parse(bytes, new ReadOptions { OffsetBase = 40 })),
                "Parser" => Catch(() => FixedParser.Parse(bytes)),
                "Full" => Catch(() => FixedFull.Parse(bytes)),
                "Struct" => Catch(() => FixedValue.Parse(bytes)),
                "Order" => Catch(() => FixedOrder.Parse(bytes, true)),
                "Scalar" => Catch(() => OneScalar.Parse(bytes)),
                "ScalarLimits" => Catch(() => OneScalar.Parse(bytes, new ReadLimits { OffsetBase = 40 })),
                "View" => Catch(() => new FixedViews.FxView(bytes).Validate()),
                "ViewObject" => Catch(() => new FixedFull.FxView(bytes).ToObject()),
                "Stream" => Catch(() => FixedFull.Parse(new MemoryStream(bytes))),
                "Sequence" => Catch(() => FixedFull.Parse(new ReadOnlySequence<byte>(bytes))),
                "Async" => Catch(() => FixedFull.ParseAsync(new MemoryStream(bytes)).AsTask().GetAwaiter().GetResult()),
                "Records" => Catch(() => FixedFull.Records(bytes).ToList()),
                "RecordsStream" => Catch(() => FixedFull.Records(new MemoryStream(bytes)).ToList()),
                "VariableSpan" => Catch(() => VariableFull.Parse(bytes)),
                "VariableLimits" => Catch(() => VariableFull.Parse(bytes, new ReadLimits { OffsetBase = 40 })),
                "VariableStream" => Catch(() => VariableFull.Parse(new MemoryStream(bytes))),
                "VariableRecords" => Catch(() => VariableFull.Records(bytes).ToList()),
                "Default" => Catch(() => FixedDefault.Parse(bytes)),
                "DefaultView" => Catch(() => new FixedDefault.FxView(bytes).Validate()),
                "VariableDefault" => Catch(() => VariableDefault.Parse(bytes)),
                _ => throw new ArgumentException(surface),
            };

            public static (bool Ok, Exception? Failure) Try(string surface, byte[] bytes)
            {
                switch (surface)
                {
                case "Full":
                    return (FixedFull.TryParse(bytes, out FixedFull.Fx? fixedValue, out CStructException? fixedFailure), fixedFailure);
                case "FullStream":
                    return (FixedFull.TryParse(new MemoryStream(bytes), out FixedFull.Fx? streamValue, out CStructException? streamFailure), streamFailure);
                case "Variable":
                    return (VariableFull.TryParse(bytes, out VariableFull.Vx? variableValue, out CStructException? variableFailure), variableFailure);
                case "VariableLimits":
                    return (VariableFull.TryParse(new ReadOnlySpan<byte>(bytes), out VariableFull.Vx? limitsValue, out CStructException? limitsFailure, new ReadLimits()), limitsFailure);
                case "VariableSequence":
                    return (VariableFull.TryParse(new ReadOnlySequence<byte>(bytes), out VariableFull.Vx? sequenceValue, out CStructException? sequenceFailure), sequenceFailure);
                case "Scalar":
                    return (OneScalar.TryParse(bytes, out uint scalar, out CStructException? scalarFailure), scalarFailure);
                case "View":
                    return (new FixedViews.FxView(bytes).TryValidate(out CStructException? viewFailure), viewFailure);
                default:
                    throw new ArgumentException(surface);
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

    /// <summary>A failed <c>@expect</c> throws <see cref="InvalidDataException"/> from every entry point, with the layout's message, the label and the offset of the constraint failure.</summary>
    [TestMethod]
    public void Expect_ThrowsInvalidDataException_OnEverySurface()
    {
        byte[] bytes = [0, 0, 0, 0, 1, 0, 0, 0, 5, 0, 0, 0, 5, 0, 0, 0];
        foreach (string surface in new[] { "Reader", "Parser", "Full", "Struct", "Order", "View", "ViewObject", "Stream", "Sequence", "Async", "Records", "RecordsStream" })
        {
            Exception? thrown = Parse(surface, bytes);
            Assert.IsInstanceOfType<InvalidDataException>(thrown, surface);
            var constraint = (CStructConstraintException)thrown!.InnerException!;
            Assert.AreEqual(ConstraintKind.Malformed, constraint.Kind, surface);
            Assert.AreEqual("fx.a", constraint.Label, surface);
            Assert.AreEqual("The magic is missing.", constraint.Description, surface);
            Assert.AreEqual("a", constraint.Member, surface);
            Assert.AreEqual(4, constraint.Offset, surface);
            StringAssert.StartsWith(thrown.Message, "The magic is missing. [fx.a]", surface);
            StringAssert.Contains(thrown.Message, "offset 4", surface.StartsWith("Records", StringComparison.Ordinal) ? string.Empty : surface);
        }
    }

    /// <summary>A failed <c>@supports</c> throws <see cref="NotSupportedException"/>, and the message keeps the escaped quotes of the layout text.</summary>
    [TestMethod]
    public void Supports_ThrowsNotSupportedException()
    {
        byte[] bytes = [1, 0, 0, 0, 200, 0, 0, 0, 5, 0, 0, 0, 5, 0, 0, 0];
        foreach (string surface in new[] { "Reader", "Parser", "Full", "Struct", "Order", "View", "Stream" })
        {
            Exception? thrown = Parse(surface, bytes);
            Assert.IsInstanceOfType<NotSupportedException>(thrown, surface);
            var constraint = (CStructConstraintException)thrown!.InnerException!;
            Assert.AreEqual(ConstraintKind.Unsupported, constraint.Kind, surface);
            Assert.AreEqual("Revision \"b\" is too new.", constraint.Description, surface);
            StringAssert.StartsWith(thrown.Message, "Revision \"b\" is too new. [fx.b]", surface);
        }
    }

    /// <summary>A constraint without a message keeps the generated sentence inside the format exception, and one without a label has none in brackets.</summary>
    [TestMethod]
    public void WithoutMessage_KeepsTheGeneratedSentence()
    {
        Exception? labelled = Parse("Full", [1, 0, 0, 0, 1, 0, 0, 0, 7, 0, 0, 0, 5, 0, 0, 0]);
        Assert.IsInstanceOfType<InvalidDataException>(labelled);
        var constraint = (CStructConstraintException)labelled!.InnerException!;
        Assert.IsNull(constraint.Description);
        StringAssert.StartsWith(labelled.Message, "Invalid value for 'c': it must satisfy 'c != 7'. [fx.c]");
        Exception? plain = Parse("Full", [1, 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 9, 0, 0, 0]);
        Assert.IsNull(((CStructConstraintException)plain!.InnerException!).Label);
        Assert.IsFalse(plain.Message.Contains('[', StringComparison.Ordinal) && plain.Message.Contains("[fx", StringComparison.Ordinal));
    }

    /// <summary>The offset base shifts the inner failure's offset, whichever form carries it.</summary>
    [TestMethod]
    public void OffsetBase_IsHonoured()
    {
        byte[] bytes = [0, 0, 0, 0, 1, 0, 0, 0, 5, 0, 0, 0, 5, 0, 0, 0];
        foreach (string surface in new[] { "ReaderLimits", "ReaderOptions" })
        {
            Exception? thrown = Parse(surface, bytes);
            Assert.AreEqual(44, ((CStructConstraintException)thrown!.InnerException!).Offset, surface);
            StringAssert.Contains(thrown.Message, "offset 44", surface);
        }

        Exception? scalar = Parse("ScalarLimits", [0, 0, 0, 0]);
        Assert.IsInstanceOfType<InvalidDataException>(scalar);
        Assert.AreEqual(44, ((CStructConstraintException)scalar!.InnerException!).Offset);
    }

    /// <summary>The one-field scalar shape throws the format exception too, for both its throwing forms.</summary>
    [TestMethod]
    public void Scalar_ThrowsInvalidDataException()
    {
        Exception? thrown = Parse("Scalar", [0, 0, 0, 0]);
        Assert.IsInstanceOfType<InvalidDataException>(thrown);
        StringAssert.StartsWith(thrown!.Message, "Not an ext superblock. [one.magic]");
    }

    /// <summary>Members read one by one (runtime-sized arrays) report through the same wrapping, for the span, limits, stream and record forms.</summary>
    [TestMethod]
    public void MemberByMemberReaders_ThrowFormatExceptions()
    {
        foreach (string surface in new[] { "VariableSpan", "VariableLimits", "VariableStream", "VariableRecords" })
        {
            Assert.IsInstanceOfType<InvalidDataException>(Parse(surface, [0]), surface);
            Assert.IsInstanceOfType<NotSupportedException>(Parse(surface, [1, 9, 5]), surface);
        }
    }

    /// <summary>Other failures (a short read) are not constraint failures and keep their type.</summary>
    [TestMethod]
    public void OtherFailures_AreUnchanged()
    {
        Assert.IsInstanceOfType<CStructReadException>(Parse("Full", [1, 0]));
        Assert.IsInstanceOfType<CStructReadException>(Parse("VariableSpan", [3, 1]));
    }

    /// <summary>The default mode keeps throwing the constraint exception, with the layout's message when it has one.</summary>
    [TestMethod]
    public void DefaultMode_ThrowsTheConstraintException()
    {
        byte[] bytes = [0, 0, 0, 0, 1, 0, 0, 0, 5, 0, 0, 0, 5, 0, 0, 0];
        foreach (string surface in new[] { "Default", "DefaultView", "VariableDefault" })
        {
            Exception? thrown = Parse(surface, surface == "VariableDefault" ? [0] : bytes);
            Assert.IsInstanceOfType<CStructConstraintException>(thrown, surface);
            Assert.IsNotNull(((CStructConstraintException)thrown!).Description, surface);
        }
    }

    /// <summary>The non-throwing forms return the constraint failure itself and raise no format exception.</summary>
    [TestMethod]
    public void NonThrowingForms_ReturnTheConstraintFailure()
    {
        byte[] fixedBytes = [0, 0, 0, 0, 1, 0, 0, 0, 5, 0, 0, 0, 5, 0, 0, 0];
        foreach (string surface in new[] { "Full", "FullStream", "View" })
        {
            (bool ok, Exception? failure) = Try(surface, fixedBytes);
            Assert.IsFalse(ok, surface);
            Assert.IsInstanceOfType<CStructConstraintException>(failure, surface);
        }

        foreach (string surface in new[] { "Variable", "VariableLimits", "VariableSequence" })
        {
            (bool ok, Exception? failure) = Try(surface, [0]);
            Assert.IsFalse(ok, surface);
            var constraint = (CStructConstraintException)failure!;
            Assert.AreEqual("vx.n", constraint.Label, surface);
            Assert.AreEqual("Empty record.", constraint.Description, surface);
        }

        (bool scalarOk, Exception? scalarFailure) = Try("Scalar", [0, 0, 0, 0]);
        Assert.IsFalse(scalarOk);
        Assert.IsInstanceOfType<CStructConstraintException>(scalarFailure);
    }

    /// <summary>The runtime layout of the same text throws the constraint exception with the same description and message sentence.</summary>
    [TestMethod]
    public void RuntimeLayout_ThrowsTheConstraintExceptionWithTheSameMessage()
    {
        byte[] bytes = [0, 0, 0, 0, 1, 0, 0, 0, 5, 0, 0, 0, 5, 0, 0, 0];
        var thrown = Assert.ThrowsExactly<CStructConstraintException>(() => new CStruct(RuntimeFixed).Parse(bytes, "fx"));
        Assert.AreEqual("The magic is missing.", thrown.Description);
        Exception? generated = Parse("Full", bytes);
        Assert.AreEqual(thrown.Message, generated!.Message);
    }

    /// <summary>Calls a method of the consumer's <c>Calls</c> class.</summary>
    /// <param name="surface">The surface name the method switches on.</param>
    /// <param name="bytes">The input bytes.</param>
    /// <returns>The exception the call raised, or <see langword="null"/>.</returns>
    private static Exception? Parse(string surface, byte[] bytes)
        => (Exception?)assembly.GetType("Demo.Calls")!.GetMethod("Parse")!.Invoke(null, [surface, bytes]);

    /// <summary>Calls the consumer's non-throwing form.</summary>
    /// <param name="surface">The surface name the method switches on.</param>
    /// <param name="bytes">The input bytes.</param>
    /// <returns>Whether the read succeeded and the failure it returned.</returns>
    private static (bool Ok, Exception? Failure) Try(string surface, byte[] bytes)
        => ((bool, Exception?))assembly.GetType("Demo.Calls")!.GetMethod("Try")!.Invoke(null, [surface, bytes])!;
}
