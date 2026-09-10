namespace CStructSharp.Tests;

using CStructSharp.Structure;

/// <summary>
///     Exercises <see cref="CompositeFieldPlacementCursor"/> directly, independent of address resolution or extent
///     measurement. Only reachable indirectly through the public API before this type was extracted to remove two
///     byte-identical copies of the same bitfield-unit-and-alignment state machine from
///     <c>CStructAddressResolver.cs</c>.
/// </summary>
/// <remarks>
///     Each test cross-validates the cursor's output against <c>CompiledField.FixedOffset</c>/<c>BitOffset</c>,
///     which are computed independently by <c>PlaceCompiledFields</c> during construction using the same algorithm.
///     That construction-time code was not touched by this extraction, so agreement between the two is a strong
///     signal the cursor reproduces it exactly.
/// </remarks>
[TestClass]
public class CompositeFieldPlacementCursorTests
{
    private static CompiledCompositeType CompileRoot(string layout, bool aligned)
    {
        var cstruct = new CStruct(layout, aligned: aligned);
        Struct root = cstruct.GetStruct("root");
        return (CompiledCompositeType)cstruct.CompiledModel.Composites[root].Definition!;
    }

    /// <summary>Sequential fixed-width fields in aligned mode place each field exactly where the compiled model already does.</summary>
    [TestMethod]
    public void AdvanceToField_AlignedFixedWidthFields_MatchesCompiledOffsets()
    {
        CompiledCompositeType composite = CompileRoot("struct root { uint8 a; uint32 b; uint8 c; };", aligned: true);

        var cursor = new CompositeFieldPlacementCursor(0, aligned: true);
        foreach (CompiledField field in composite.Fields)
        {
            (long fieldStart, int bitOffset) = cursor.AdvanceToField(field);

            Assert.AreEqual((long)field.FixedOffset!.Value, fieldStart);
            Assert.AreEqual(field.BitOffset, bitOffset);

            cursor.CompleteField(fieldStart + (field.FixedStorageSize ?? 0));
        }
    }

    /// <summary>Packed (unaligned) mode places every field back-to-back, exactly matching the compiled model's own offsets.</summary>
    [TestMethod]
    public void AdvanceToField_PackedFixedWidthFields_MatchesCompiledOffsets()
    {
        CompiledCompositeType composite = CompileRoot("struct root { uint8 a; uint32 b; uint8 c; };", aligned: false);

        var cursor = new CompositeFieldPlacementCursor(0, aligned: false);
        foreach (CompiledField field in composite.Fields)
        {
            (long fieldStart, int bitOffset) = cursor.AdvanceToField(field);

            Assert.AreEqual((long)field.FixedOffset!.Value, fieldStart);
            Assert.AreEqual(0, bitOffset);

            cursor.CompleteField(fieldStart + (field.FixedStorageSize ?? 0));
        }

        // Packed mode never pads between byte-sized fields: 1 + 4 + 1 = 6.
        Assert.AreEqual(6, cursor.Current);
    }

    /// <summary>Bitfields that fit the same storage unit share one field start and advance through distinct bit offsets.</summary>
    [TestMethod]
    public void AdvanceToField_BitfieldsSharingOneUnit_MatchesCompiledOffsets()
    {
        CompiledCompositeType composite = CompileRoot(
            "struct root { uint8 low:3; uint8 mid:3; uint8 high:3; uint8 tail; };",
            aligned: true);

        var cursor = new CompositeFieldPlacementCursor(0, aligned: true);
        foreach (CompiledField field in composite.Fields)
        {
            (long fieldStart, int bitOffset) = cursor.AdvanceToField(field);

            Assert.AreEqual((long)field.FixedOffset!.Value, fieldStart, field.Declaration.Name.Name);
            Assert.AreEqual(field.BitOffset, bitOffset, field.Declaration.Name.Name);

            if (field.EffectiveField.BitSize == 0)
            {
                cursor.CompleteField(fieldStart + (field.FixedStorageSize ?? 0));
            }
        }

        // low(3)+mid(3) fit one byte; high(3) cannot (9 > 8) and opens a second byte; tail follows that byte.
        Assert.AreEqual(0, composite.FieldsByName["low"].FixedOffset);
        Assert.AreEqual(0, composite.FieldsByName["low"].BitOffset);
        Assert.AreEqual(0, composite.FieldsByName["mid"].FixedOffset);
        Assert.AreEqual(3, composite.FieldsByName["mid"].BitOffset);
        Assert.AreEqual(1, composite.FieldsByName["high"].FixedOffset);
        Assert.AreEqual(0, composite.FieldsByName["high"].BitOffset);
        Assert.AreEqual(2, composite.FieldsByName["tail"].FixedOffset);
    }

    /// <summary>The cursor's final position, aligned up to the composite's own alignment, matches the compiled struct size.</summary>
    [TestMethod]
    public void Current_AfterEveryField_MatchesCompiledStructAlignment()
    {
        CompiledCompositeType composite = CompileRoot("struct root { uint8 a; uint32 b; };", aligned: true);

        var cursor = new CompositeFieldPlacementCursor(0, aligned: true);
        foreach (CompiledField field in composite.Fields)
        {
            (long fieldStart, _) = cursor.AdvanceToField(field);
            cursor.CompleteField(fieldStart + (field.FixedStorageSize ?? 0));
        }

        long aligned = LayoutMath.AlignUp(cursor.Current, composite.Symbol.Alignment);
        Assert.AreEqual((long)composite.Symbol.FixedSize!.Value, aligned);
    }

    /// <summary>
    ///     A starting position that already meets the composite's own alignment offsets every subsequent field by
    ///     exactly that amount.
    /// </summary>
    [TestMethod]
    public void AdvanceToField_AlignedNonZeroStart_OffsetsEveryFieldByTheStart()
    {
        CompiledCompositeType composite = CompileRoot("struct root { uint8 a; uint32 b; };", aligned: true);

        // 8 is itself a multiple of every field's alignment (max 4), so the offset is translation-invariant here -
        // unlike an arbitrary start, which could shift where padding falls relative to the fields.
        var cursorFromZero = new CompositeFieldPlacementCursor(0, aligned: true);
        var cursorFromEight = new CompositeFieldPlacementCursor(8, aligned: true);
        foreach (CompiledField field in composite.Fields)
        {
            (long fieldStartFromZero, _) = cursorFromZero.AdvanceToField(field);
            (long fieldStartFromEight, _) = cursorFromEight.AdvanceToField(field);

            Assert.AreEqual(fieldStartFromZero + 8, fieldStartFromEight);

            cursorFromZero.CompleteField(fieldStartFromZero + (field.FixedStorageSize ?? 0));
            cursorFromEight.CompleteField(fieldStartFromEight + (field.FixedStorageSize ?? 0));
        }
    }
}
