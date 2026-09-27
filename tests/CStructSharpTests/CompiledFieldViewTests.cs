namespace CStructSharp.Tests;

using System.Buffers;
using CStructSharp.Codecs;
using CStructSharp.Compilation;

/// <summary>Checks the immutable field views shared by selected reads, writers and generated codec planning.</summary>
[TestClass]
public class CompiledFieldViewTests
{
    /// <summary>Peeling pointer levels preserves pointer storage facts until the final primitive target is selected.</summary>
    /// <param name="littleEndian">The layout's neutral byte order, retained by every derived descriptor.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void PointerViews_SwitchCodecsOnlyAtTheFinalTarget(bool littleEndian)
    {
        var layout = new CStruct("struct root { uint16 **value; };", pointerSize: 4, isLittleEndian: littleEndian);
        CompiledField pointer = Field(layout, "value");
        Assert.AreEqual(PrimitiveCodecKind.None, pointer.Codec.Kind);
        Assert.AreEqual(littleEndian, pointer.LayoutLittleEndian);
        CompiledField middle = pointer.SelectPointerTarget(1, null, 4);
        Assert.AreEqual(1, middle.PointerDepth);
        Assert.AreEqual(4, middle.Alignment);
        Assert.AreEqual(4, middle.FixedElementSize);
        Assert.AreEqual(4, middle.FixedStorageSize);
        Assert.AreEqual(pointer.Codec, middle.Codec);
        CompiledField value = middle.SelectPointerTarget(0, null, 4);
        Assert.AreEqual(0, value.PointerDepth);
        Assert.AreEqual(2, value.Alignment);
        Assert.AreEqual(2, value.FixedElementSize);
        Assert.AreEqual(PrimitiveCodec.Resolve("uint16", littleEndian), value.Codec);
        Assert.IsFalse(value.IsUnsizedCharacterArray);
    }

    /// <summary>Wide-character metadata preserves explicit byte order and does not label fixed arrays as terminated text.</summary>
    /// <param name="type">The wide-character spelling.</param>
    /// <param name="codePage">Explicit UTF-16 code page, or zero for the layout's neutral order.</param>
    [TestMethod]
    [DataRow("wchar>", 1201)]
    [DataRow("wchar<", 1200)]
    [DataRow("wchar", 0)]
    public void WideCharacterViews_RetainTheirEncodingAndShape(string type, int codePage)
    {
        var layout = new CStruct("struct root { " + type + " text[2]; };");
        CompiledField text = Field(layout, "text");
        Assert.IsTrue(text.IsWideCharElement);
        Assert.IsTrue(text.IsCharacterArray);
        Assert.AreEqual(type, text.DisplayTypeSpelling);
        Assert.AreEqual(codePage, text.ExplicitWideCharacterEncoding?.CodePage ?? 0);
        Assert.IsFalse(text.IsUnsizedCharacterArray);
        CompiledField element = text.SelectArrayElement();
        Assert.AreEqual(CompiledArrayKind.Scalar, element.Array.Kind);
        Assert.IsFalse(element.IsUnsizedCharacterArray);
        Assert.AreEqual(text.Codec, element.Codec);
    }

    /// <summary>Composite and enum descriptors distinguish stored values from pointers and named versus promoted inline members.</summary>
    [TestMethod]
    public void FieldRoles_DistinguishCompositesEnumsAndPromotedMembers()
    {
        var layout = new CStruct("enum kind : uint8 { A = 1 }; struct child { uint8 n; }; struct root { child value; child *pointer; kind choice; kind *choicePointer; struct { uint8 inner; } named; struct { uint8 promoted; }; uint8 data[named.inner]; };");
        CompiledField composite = Field(layout, "value");
        Assert.AreEqual(PrimitiveCodecKind.None, composite.Codec.Kind);
        Assert.IsNotNull(composite.Composite);
        Assert.IsFalse(composite.IsInlineComposite);
        Assert.IsFalse(composite.IsPromotedComposite);
        Assert.IsNull(composite.QualifiedPrefix);
        Assert.IsNull(Field(layout, "pointer").Composite);
        Assert.IsNotNull(Field(layout, "pointer").TargetComposite);
        Assert.IsNotNull(Field(layout, "choice").Enum);
        Assert.IsNull(Field(layout, "choicePointer").Enum);
        CompiledField named = Field(layout, "named");
        Assert.IsTrue(named.IsInlineComposite);
        Assert.IsFalse(named.IsPromotedComposite);
        Assert.AreEqual("named.", named.QualifiedPrefix);
        CompiledCompositeType root = (CompiledCompositeType)layout.CompiledModel.Composites[layout.GetStruct("root")].Definition!;
        Assert.IsTrue(root.PromotedFields.Single().IsPromotedComposite);
    }

    /// <summary>Custom values retain their full size, while pointer levels defer the custom codec until the final target.</summary>
    [TestMethod]
    public void LargeCustomCodec_KeepsFullSizeOutsideTheCompactDescriptor()
    {
        var layout = new CStruct(
            "struct root { fixed_block value; fixed_block **pointer; };",
            pointerSize: 4,
            compilationOptions: new CStructCompilationOptions { Codecs = [new MetadataCodec(),], });
        CompiledField value = Field(layout, "value");
        Assert.AreEqual(PrimitiveCodecKind.Custom, value.Codec.Kind);
        Assert.AreEqual(byte.MaxValue, value.Codec.Size);
        Assert.AreEqual(512, value.FixedElementSize);
        CompiledField pointer = Field(layout, "pointer");
        Assert.AreEqual(PrimitiveCodecKind.None, pointer.Codec.Kind);
        CompiledField middle = pointer.SelectPointerTarget(1, null, 4);
        Assert.AreEqual(PrimitiveCodecKind.None, middle.Codec.Kind);
        Assert.AreEqual(4, middle.FixedElementSize);
        CompiledField target = middle.SelectPointerTarget(0, null, 4);
        Assert.AreEqual(value.Codec, target.Codec);
        Assert.AreEqual(512, target.FixedElementSize);
    }

    /// <summary>A terminated wide-string target uses byte alignment rather than the alignment of a single wide character.</summary>
    [TestMethod]
    public void TerminatedWideTarget_UsesByteAlignment()
    {
        var layout = new CStruct("struct root { wchar *text; };", pointerSize: 4);
        CompiledField pointer = Field(layout, "text");
        CompiledField target = pointer.SelectPointerTarget(0, "unicode_string_zero", 4);

        Assert.AreEqual(1, target.Alignment);
        Assert.IsNull(target.FixedElementSize);
        Assert.IsNull(target.FixedStorageSize);
        Assert.AreEqual(pointer.TerminatedCodecId, target.CodecId);
    }

    /// <summary>Placement sets every placement fact it is given, while an omitted unit size retains the current window.</summary>
    [TestMethod]
    public void PlacementViews_OverrideOrRetainTheBitWindowExplicitly()
    {
        var layout = new CStruct("struct root { uint32 value:20; };");
        CompiledField value = Field(layout, "value");
        Assert.AreEqual(3, value.BitUnitSize);
        CompiledField moved = value.WithPlacement(value.MemberIndex, 9, 1, 4, 7, value.ConditionalBranches);
        Assert.AreEqual(9, moved.FixedOffset);
        Assert.AreEqual(1, moved.BitOffset);
        Assert.AreEqual(4, moved.BitUnitSize);
        Assert.AreEqual(7, moved.BitRunBits);
        Assert.AreEqual(4, moved.WithPlacement(moved.MemberIndex, 12, 0, null, 7, moved.ConditionalBranches).BitUnitSize);
        Assert.AreEqual(3, value.BitUnitSize);
    }

    /// <summary>A user composite that shadows a primitive alias must not acquire that primitive's codec during placement.</summary>
    [TestMethod]
    public void CompositeAliasName_RetainsItsCompositeCodecIdentity()
    {
        var layout = new CStruct("struct DWORD { uint8 value; }; struct root { DWORD nested; };");
        CompiledField nested = Field(layout, "nested");

        Assert.IsNotNull(nested.Composite);
        Assert.AreEqual(PrimitiveCodecKind.None, nested.Codec.Kind);
        Assert.AreEqual(1, nested.FixedElementSize);
    }

    /// <summary>A compiled field is complete when constructed: no property can be set afterwards except through an initializer.</summary>
    [TestMethod]
    public void CompiledField_HasNoSetterAfterConstruction()
    {
        foreach (System.Reflection.PropertyInfo property in typeof(CompiledField).GetProperties(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic))
        {
            System.Reflection.MethodInfo? setter = property.SetMethod;
            bool initOnly = setter is not null &&
                            setter.ReturnParameter.GetRequiredCustomModifiers().Contains(typeof(System.Runtime.CompilerServices.IsExternalInit));
            Assert.IsTrue(setter is null || initOnly, property.Name + " can be set after construction.");
        }
    }

    /// <summary>
    ///     Members carry their capture, qualified-prefix, position and arm facts from construction; an array element or
    ///     pointer target keeps the member's facts but no arms, since it is read only after its member was selected.
    /// </summary>
    [TestMethod]
    public void MembersAndViews_CarryTheFactsTheyWereBuiltWith()
    {
        var layout = new CStruct("struct hdr { uint8 n; }; struct root { hdr h; uint8 flag; if (flag) { uint8 values[h.n]; hdr *next; } };", pointerSize: 4);
        CompiledField header = Field(layout, "h");
        Assert.IsTrue(header.HasQualifiedPrefix);
        Assert.IsTrue(Field(layout, "n").CapturesLayoutVariable);
        Assert.IsTrue(Field(layout, "flag").CapturesLayoutVariable);
        Assert.IsFalse(Field(layout, "values").CapturesLayoutVariable);

        CompiledField values = Field(layout, "values");
        Assert.AreEqual(2, values.MemberIndex);
        Assert.HasCount(1, values.ConditionalBranches);
        CompiledField element = values.SelectArrayElement();
        Assert.AreEqual(values.MemberIndex, element.MemberIndex);
        Assert.IsEmpty(element.ConditionalBranches);

        CompiledField next = Field(layout, "next");
        Assert.AreEqual(3, next.MemberIndex);
        Assert.AreSame(values.ConditionalBranches[0].Group, next.ConditionalBranches[0].Group);
        CompiledField target = next.SelectPointerTarget(0, null, 4);
        Assert.AreEqual(next.MemberIndex, target.MemberIndex);
        Assert.IsEmpty(target.ConditionalBranches);
        Assert.IsFalse(target.HasQualifiedPrefix);
    }

    /// <summary>Finds a uniquely named field in the compiled fixture without reinterpreting its source declaration.</summary>
    /// <param name="layout">Compiled fixture.</param>
    /// <param name="name">Unique member name.</param>
    /// <returns>The immutable field descriptor.</returns>
    private static CompiledField Field(CStruct layout, string name)
    {
        // Every fixture uses distinct member names, including nested members.
        return layout.CompiledModel.AllFields().Single(field => field.Name == name);
    }

    /// <summary>Supplies a large fixed-size custom symbol for metadata-only tests, without an executable codec.</summary>
    private sealed class MetadataCodec : ICustomCodec
    {
        public string Name => "fixed_block";

        public int? FixedSize => 512;

        public int Alignment => 1;

        /// <summary>Rejects decoding because this fixture tests only compilation metadata.</summary>
        /// <param name="source">Unused borrowed input.</param>
        /// <param name="value">Always null.</param>
        /// <param name="bytesConsumed">Always zero.</param>
        /// <returns>InvalidData without consuming input.</returns>
        public OperationStatus Read(ReadOnlySpan<byte> source, out object? value, out int bytesConsumed)
        {
            value = null;
            bytesConsumed = 0;
            return OperationStatus.InvalidData;
        }

        /// <summary>Rejects encoding because this fixture tests only compilation metadata.</summary>
        /// <param name="destination">Unused borrowed output.</param>
        /// <param name="value">Unused input value.</param>
        /// <param name="bytesWritten">Always zero.</param>
        /// <returns>InvalidData without changing output.</returns>
        public OperationStatus Write(Span<byte> destination, object value, out int bytesWritten)
        {
            bytesWritten = 0;
            return OperationStatus.InvalidData;
        }
    }
}
