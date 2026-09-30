namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>
///     The compiled read engine's pointers: targets followed after their struct in declaration order and charged once,
///     cycles and the depth limit, target size limits, pointers in union views and <c>void *</c>. Each case checks its
///     outcomes against the golden outcomes (<see cref="EngineGolden"/>) under both execution paths, and pins the expected
///     outcome.
/// </summary>
[TestClass]
public class ReadEnginePointerTests
{
    /// <summary>
    ///     A struct follows its pointers after its last member, in declaration order, so a <c>@count</c> can name a later
    ///     field and the first pointer that fails is the one reported: a target outside the input fails naming its pointer
    ///     with the position just after that pointer's address, and so does a target that ends early (the position is
    ///     restored after the failed target read). Every byte of every target is charged once on top of the struct's own:
    ///     5 + 1 + 1 + 2 = 9 bytes here. A deferred count is checked before the pointer depth.
    /// </summary>
    [TestMethod]
    public void Pointers_FollowAfterTheStruct_InDeclarationOrder()
    {
        var layout = new CStruct("struct leaf { uint8 v; }; struct rec { uint8 *a; leaf *b; uint8 *c @count(n); uint8 n; uint8 tail; };", 1);
        byte[] data = [0x05, 0x06, 0x05, 0x02, 0x09, 0xA1, 0xB2];
        var wide = new CStruct("struct rec { uint8 *a; uint16 *w; uint8 tail; };", 1);
        var countFirst = new CStruct("struct rec { uint8 *c @count(n); uint8 n; };", 1);
        foreach (ExecutionPath path in ExecutionPaths.Both)
        {
            string complete = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(complete, "result.tail = Byte 9\n");
            for (long budget = 1; budget <= 10; budget++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream1])
                {
                    string outcome = EngineDifferential.AssertGolden(
                        EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }),
                        path: path);
                    StringAssert.Contains(outcome, budget < 9 ? "CStructReadLimitException" : "result.tail = Byte 9\n", "budget " + budget);
                }
            }

            for (int length = 0; length <= data.Length; length++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream, EngineInput.ChunkedStream3])
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data[..length], input, "rec"), path: path);
                }
            }

            // a and b both point outside the input: a, declared first, fails first, after its own address.
            string outside = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, [0x20, 0x21, 0x05, 0x00, 0x09, 0xA1], EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(outside, "failure.member = \"a\"\n");
            StringAssert.Contains(outside, "position = 1\n");

            // w's two-byte target starts at the last byte: the read fails, and the position is back after w's address.
            string truncated = EngineDifferential.AssertGolden(EngineOperations.Parse(wide, [0x03, 0x03, 0x09, 0x07], EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(truncated, "failure.member = \"w\"\n");
            StringAssert.Contains(truncated, "position = 2\n");

            // The deferred count is evaluated before the depth limit is checked.
            string counted = EngineDifferential.AssertGolden(
                EngineOperations.Parse(countFirst, [0x02, 0x02, 0xA1, 0xA2], EngineInput.Span, "rec", options: new ReadOptions { MaxArrayElements = 1, MaxPointerDepth = 0, }),
                path: path);
            StringAssert.Contains(counted, "failure.member = \"c\"\n");
            StringAssert.Contains(counted, ReadFailures.ArrayLengthLimit(2, 1).TrimEnd('.'));

            foreach (bool dereference in (bool[])[true, false])
            {
                foreach ((PointerAddressingMode mode, long origin) in ((PointerAddressingMode, long)[])[(PointerAddressingMode.Absolute, 0), (PointerAddressingMode.Relative, 0), (PointerAddressingMode.Relative, 2), (PointerAddressingMode.Relative, long.MaxValue)])
                {
                    var read = new ReadOptions { DereferencePointers = dereference, AddressingMode = mode, Origin = origin, };
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec", options: read), path: path);
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.ChunkedStream1, "rec", options: read), path: path);
                }
            }
        }
    }

    /// <summary>
    ///     A target already on the active path is a cycle: a node whose pointer leads back to itself fails naming the
    ///     pointer, with the position restored after the outermost followed pointer's address. The active path is left on
    ///     unwind, so the same node read again elsewhere is no cycle. The pointer depth limit counts every level, list
    ///     links included, and a root pointer is followed in place.
    /// </summary>
    [TestMethod]
    public void Pointers_DetectCycles_AndLimitTheDepth()
    {
        var layout = new CStruct("struct node { uint8 v; node *next; }; struct rec { node head; node again; };", 1);
        byte[] data = [0x01, 0x04, 0x02, 0x04, 0x03, 0x00];
        var roots = new CStruct("typedef uint8 *byte_ptr; typedef uint8 **byte_ptr_ptr;", 1);
        byte[] rootData = [0x01, 0x02, 0x07];
        foreach (ExecutionPath path in ExecutionPaths.Both)
        {
            string fine = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(fine, "result.again.next");

            string cycle = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, [0x01, 0x04, 0x02, 0x00, 0x03, 0x04], EngineInput.Stream, "rec"), path: path);
            StringAssert.Contains(cycle, "Cyclic pointer target");
            StringAssert.Contains(cycle, "failure.member = \"next\"\n");
            StringAssert.Contains(cycle, "position = 2\n");

            for (int depth = 0; depth <= 3; depth++)
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.Stream])
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { MaxPointerDepth = depth, }), path: path);
                    EngineDifferential.AssertGolden(EngineOperations.Parse(roots, rootData, input, "byte_ptr_ptr", options: new ReadOptions { MaxPointerDepth = depth, }), path: path);
                }
            }

            for (int length = 0; length <= 3; length++)
            {
                EngineDifferential.AssertGolden(EngineOperations.Parse(roots, rootData[..length], EngineInput.Span, "byte_ptr"), path: path);
            }
        }
    }

    /// <summary>
    ///     A target size limit (<see cref="ReadOptions.MaxPointerTargetBytes"/>) refuses a text target, whose size is not
    ///     known, and a counted target larger than the limit, before either is read; a pointer in a union view keeps only
    ///     its address, and a <c>void *</c> is never followed.
    /// </summary>
    [TestMethod]
    public void Pointers_TargetLimits_UnionViews_AndOpaqueAddresses()
    {
        var layout = new CStruct("union u { uint8 raw; uint8 *p; }; struct rec { char *name; uint8 *bytes @count(n); u view; void *opaque; uint8 n; };", 1);
        byte[] data = [0x05, 0x07, 0x07, 0x07, 0x02, (byte)'o', 0x00, 0xA1, 0xA2];
        foreach (ExecutionPath path in ExecutionPaths.Both)
        {
            string complete = EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Span, "rec"), path: path);
            StringAssert.Contains(complete, "\"o\"");
            foreach (long? limit in (long?[])[null, 0, 1, 2, 3])
            {
                foreach (EngineInput input in (EngineInput[])[EngineInput.Span, EngineInput.ChunkedStream1])
                {
                    EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, input, "rec", options: new ReadOptions { MaxPointerTargetBytes = limit, }), path: path);
                }
            }

            for (long budget = 1; budget <= 16; budget++)
            {
                EngineDifferential.AssertGolden(EngineOperations.Parse(layout, data, EngineInput.Stream, "rec", options: new ReadOptions { MaxTotalBytesRead = budget, }), path: path);
            }
        }
    }
}
