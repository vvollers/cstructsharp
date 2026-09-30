namespace CStructSharp.Tests;

using SweepLayout = EngineSweepLayouts.SweepLayout;
using Variant = EngineSweepLayouts.Variant;

/// <summary>
///     Sweeps the layout capture an update compares (every value's path and byte range, then the conditional-layout trace)
///     over the representative layouts of <see cref="EngineSweepLayouts"/>, packed and aligned, checking the compiled
///     engine's debug program against the golden captures (<see cref="EngineLayoutCapture"/>): conditional roots, whose
///     trace records each decision and the position it was made at, and terminated values, whose extent the update
///     compares.
/// </summary>
[TestClass]
public class EngineLayoutCaptureTests
{
    /// <summary>Gets the sweep layout names as data rows.</summary>
    public static IEnumerable<object[]> Layouts => EngineSweepLayouts.Names;

    /// <summary>
    ///     Every prefix of the input, from empty to complete, captures its golden layout, or fails as the golden outcomes record, through every
    ///     stream an update may read: memory streams with a hidden and an exposed buffer (starting past their beginning),
    ///     chunked streams and a file.
    /// </summary>
    /// <param name="name">The sweep layout.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void Truncations_CaptureTheLayoutFromEveryStream(string name)
    {
        foreach (Variant variant in EngineSweepLayouts.Both(name))
        {
            SweepLayout source = variant.Source;
            for (int length = 0; length <= variant.Data.Length; length++)
            {
                foreach (EngineInput input in EngineStreams.All)
                {
                    EngineLayoutCapture.AssertGolden(variant.Name + " length " + length, variant.Layout, variant.Data[..length], input, "rec", source.Variables, variant.BaseRead(input));
                }
            }
        }
    }

    /// <summary>
    ///     Every <see cref="ReadOptions.MaxTotalBytesRead"/> from 1 to one past the capture's natural total, and every element,
    ///     string, nesting, pointer-depth and pointer-target limit from 0 to the input length plus two, captures its golden
    ///     layout or fails where the golden outcomes record.
    /// </summary>
    /// <param name="name">The sweep layout.</param>
    [TestMethod]
    [DynamicData(nameof(Layouts))]
    public void Limits_CaptureTheLayoutOrFailAtTheLimit(string name)
    {
        foreach (Variant variant in EngineSweepLayouts.Both(name))
        {
            SweepLayout source = variant.Source;
            byte[] data = variant.Data;
            ReadOptions baseRead = variant.BaseRead();

            // Captures one set of options from a whole memory stream and a three-byte chunked stream.
            void Compare(string limit, ReadOptions read)
            {
                EngineLayoutCapture.AssertGolden(variant.Name + " " + limit, variant.Layout, data, EngineInput.Stream, "rec", source.Variables, read);
                EngineLayoutCapture.AssertGolden(variant.Name + " " + limit, variant.Layout, data, EngineInput.ChunkedStream3, "rec", source.Variables, read);
            }

            for (int budget = 1; budget <= (16 * data.Length) + 64; budget++)
            {
                Compare("budget " + budget, baseRead with { MaxTotalBytesRead = budget, });
            }

            for (int limit = 0; limit <= data.Length + 2; limit++)
            {
                Compare("elements " + limit, baseRead with { MaxArrayElements = limit, });
                Compare("strings " + limit, baseRead with { MaxStringBytes = limit, });
                Compare("pointer target " + limit, baseRead with { MaxPointerTargetBytes = limit, });
            }

            for (int depth = 0; depth <= 4; depth++)
            {
                Compare("nesting " + depth, baseRead with { MaxNestingDepth = depth, });
                Compare("pointer depth " + depth, baseRead with { MaxPointerDepth = depth, });
            }

            Compare("untrimmed", baseRead with { TrimFixedText = false, });
            Compare("no pointers", baseRead with { DereferencePointers = false, });
        }
    }

    /// <summary>
    ///     A conditional root captures each decision - the member's declaration name, the position it was decided at, and
    ///     whether it was active - after the value records, for both arms of an <c>if</c>, a <c>switch</c> and conditionals
    ///     in nested structs and array elements.
    /// </summary>
    [TestMethod]
    public void ConditionalTrace_RecordsEveryDecision()
    {
        var layout = new CStruct("struct inner { uint8 kind; if (kind == 1) { uint8 x; } }; struct rec { uint8 flag; if (flag == 1) { uint16 yes; } else { uint8 no; } inner pair[2]; switch (flag) { case 2: { uint16 two; } default: { uint8 other; } } uint8 tail; };");
        string rendering = EngineLayoutCapture.AssertGolden("both arms", layout, [1, 0x34, 0x12, 1, 7, 0, 5, 9], EngineInput.Stream, "rec", null, new ReadOptions());
        StringAssert.Contains(rendering, "entry = \"yes\" 1 1\nentry = \"no\" 3 0\nentry = \"x\" 4 1\nentry = \"x\" 6 0\nentry = \"two\" 6 0\nentry = \"other\" 6 1\n");
        EngineLayoutCapture.AssertGolden("else arm", layout, [0, 3, 1, 7, 0, 2, 5, 9], EngineInput.ExposedStream, "rec", null, new ReadOptions());
    }

    /// <summary>
    ///     A root the layout does not declare has no layout to capture: the capture reports it as an unknown root, naming
    ///     it, and the root has no debug program.
    /// </summary>
    [TestMethod]
    public void UnknownRoot_HasNoLayout()
    {
        var layout = new CStruct("struct rec { uint8 a; };");
        var settings = Reading.ReadOperationSettings.SnapshotReadOptions(null);
        Expressions.LayoutVariableInput variables = Expressions.LayoutVariableInput.FromIntegers(null);
        using var stream = new MemoryStream([1]);
        Diagnostics.CStructPathException failure = Assert.Throws<Diagnostics.CStructPathException>(() => EngineLayoutCapture.Capture(layout, stream, 0, "missing", variables, settings));
        StringAssert.Contains(failure.Message, "missing");
        Assert.IsNull(layout.Compilation.GetRootDebugReadProgram("missing"));
    }
}
