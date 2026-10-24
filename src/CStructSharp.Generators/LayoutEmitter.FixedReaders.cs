namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using static CStructSharp.Generators.Emit;

/// <summary>
///     The fixed readers: for a struct whose every member sits at an offset known at build time, a
///     <c>Read&lt;Type&gt;Fixed(source, trimFixedText)</c> that decodes each member at its constant offset, the
///     build-time counterpart of the runtime's static read plan. The composite's generated <c>Read&lt;Type&gt;</c> tries it first
///     through <c>ReadCursor.TryTakeFixed</c>, which hands over the struct's bytes only when the member-by-member reader would
///     read exactly those bytes without a failure (enough input, within the read budget and the nesting and array
///     limits, and on the struct's alignment); any other input takes the member-by-member reader, which reports any
///     failure.
/// </summary>
internal sealed partial class LayoutEmitter
{
    // The eligibility of each composite, decided once; null for a composite that needs the member-by-member reader.
    private readonly Dictionary<GeneratedComposite, FixedPlan?> fixedPlans = new(ReferenceEqualityComparer.Instance);

    /// <summary>Emits the fixed readers of every eligible composite.</summary>
    /// <param name="writer">The generated source destination.</param>
    private void EmitFixedReaders(SourceWriter writer)
    {
        foreach (GeneratedComposite composite in this.model.Composites)
        {
            if (this.FixedPlanOf(composite, 0) is { } plan)
            {
                writer.Line();
                this.EmitFixedReader(writer, composite, plan);
                if (composite.Composite.HasConstraints)
                {
                    writer.Line();
                    this.EmitFixedConstraintCheck(writer, composite);
                }
            }
        }
    }

    /// <summary>Emits the fast-path test at the top of a composite's <c>Read&lt;Type&gt;</c>, when it has a fixed reader.</summary>
    /// <param name="writer">The generated source destination, inside the reader before the composite is entered.</param>
    /// <param name="composite">The composite being read.</param>
    /// <param name="plan">The fixed plan of the composite.</param>
    private void EmitFixedReaderShortcut(SourceWriter writer, GeneratedComposite composite, FixedPlan plan)
    {
        // A struct with conditions decodes through its fixed reader and is then checked in field order; a failure reports
        // the position the member-by-member reader would have stopped at.
        bool constrained = composite.Composite.HasConstraints;
        if (constrained)
        {
            writer.Line("int fixedStart = cursor.Position;");
        }

        writer.Open("if (" + this.TakeFixedCall(plan) + ")");
        if (constrained)
        {
            writer.Line("var fixedValue = Read" + composite.Name + "Fixed(fixedBytes, cursor.TrimFixedText" + (this.HasRuntimeByteOrder ? ", cursor.LittleEndian" : string.Empty) + ");");
            writer.Open("if (!TryCheck" + composite.Name + "Constraints(ref cursor, variables, " + (this.UsesValueTypes ? "in " : string.Empty) + "fixedValue, fixedStart, out var constraintFailure))");
            writer.Line("throw constraintFailure!;");
            writer.Close();
            writer.Line("return fixedValue;");
        }
        else
        {
            writer.Line("return Read" + composite.Name + "Fixed(fixedBytes, cursor.TrimFixedText" + (this.HasRuntimeByteOrder ? ", cursor.LittleEndian" : string.Empty) + ");");
        }

        writer.Close();
    }

    /// <summary>
    ///     Emits the constraint pass of a fixed struct that has <c>@expect</c> or <c>@supports</c> conditions: the members
    ///     are published in declaration order, as the member-by-member reader publishes them, so a condition sees the
    ///     same values and fails the same way, and the cursor is moved to the end of the failing field first so the failure
    ///     reports the same offset.
    /// </summary>
    /// <param name="writer">The generated source destination.</param>
    /// <param name="composite">A constrained composite with a fixed reader.</param>
    private void EmitFixedConstraintCheck(SourceWriter writer, GeneratedComposite composite)
    {
        string name = composite.Name;
        writer.Line("/// <summary>Checks the <c>@expect</c> and <c>@supports</c> conditions of a <c>" + composite.LayoutName + "</c> that its fixed reader just decoded, in field order, failing as the member-by-member reader would: the failure comes back instead of being thrown.</summary>");
        writer.Line("/// <param name=\"cursor\">The cursor, which sits after the struct.</param>");
        writer.Line(VariablesDoc);
        writer.Line("/// <param name=\"value\">The decoded value.</param>");
        writer.Line("/// <param name=\"start\">The position of the struct's first byte.</param>");
        writer.Line("/// <param name=\"failure\">The first condition that is not satisfied (its position is attached when it leaves the operation), or <see langword=\"null\"/>; null as well when violations are being collected.</param>");
        writer.Line("/// <returns><see langword=\"false\"/> when a condition stopped the check.</returns>");
        writer.Open("private static bool TryCheck" + name + "Constraints(ref " + Cursor + " cursor, " + VariablesType + " variables, " + (this.UsesValueTypes ? "in " : string.Empty) + name + " value, int start, out global::CStructSharp.Diagnostics.CStructConstraintException? failure)");
        writer.Line("failure = null;");
        var scope = new ReaderScope(this, composite);
        foreach (CompiledField field in composite.Composite.Fields)
        {
            GeneratedMember member = scope.Member(field) ?? throw new InvalidOperationException("No generated member for field " + field.Name);
            scope.Publish(member, "value." + member.PropertyName);
            if (field.Constraints.IsDefaultOrEmpty)
            {
                continue;
            }

            int end = (field.FixedOffset ?? 0) + (field.FixedStorageSize ?? 0);
            foreach (CompiledConstraint constraint in field.Constraints)
            {
                EmitConstraintBlock(writer, field, constraint, scope.Expressions.Emit(constraint.Expression), SourceWriter.Literal(field.Name), SourceWriter.Literal(field.DisplayTypeSpelling), "start + " + Int(end), returnsFailure: true);
            }
        }

        writer.Line("return true;");
        writer.Close();
    }

    /// <summary>
    ///     The call that hands over a fixed composite's bytes when the member-by-member reader would read exactly those bytes
    ///     without a failure. Alignment only constrains the start in an aligned layout; a packed layout places members by size alone.
    /// </summary>
    /// <param name="plan">The fixed plan of the composite.</param>
    /// <returns>The C# expression, which declares <c>fixedBytes</c>.</returns>
    private string TakeFixedCall(FixedPlan plan)
        => "cursor.TryTakeFixed(" + Int(plan.Size) + ", " + Int(this.request.Settings.Aligned ? plan.Alignment : 1) + ", " + plan.ChargedBytes.ToString(System.Globalization.CultureInfo.InvariantCulture) + ", " + Int(plan.NestingLevels) + ", " + Int(plan.MaximumArrayCount) + ", out global::System.ReadOnlySpan<byte> fixedBytes)";

    /// <summary>
    ///     Emits the body of a non-throwing span reader for a fixed composite with <c>@expect</c> or <c>@supports</c> conditions:
    ///     the fixed reader decodes it and the constraint pass returns the first failure, so a value that breaks a condition costs
    ///     an allocation, not an exception. Input the fixed reader does not take (too short, over a limit, misaligned) goes
    ///     through the member-by-member reader, which reports the failure the way it always has, and is caught.
    /// </summary>
    /// <param name="writer">The output, inside the method.</param>
    /// <param name="composite">A constrained composite that has a fixed reader.</param>
    /// <param name="plan">Its fixed plan.</param>
    /// <param name="cursorCreation">The C# statement that declares <c>cursor</c> over the span.</param>
    private void EmitFixedTryBody(SourceWriter writer, GeneratedComposite composite, FixedPlan plan, string cursorCreation)
    {
        string name = composite.Name;
        writer.Line(cursorCreation);
        writer.Open("try");
        writer.Open("if (" + this.TakeFixedCall(plan) + ")");
        writer.Line("var fixedValue = Read" + name + "Fixed(fixedBytes, cursor.TrimFixedText" + (this.HasRuntimeByteOrder ? ", cursor.LittleEndian" : string.Empty) + ");");
        writer.Open("if (!TryCheck" + name + "Constraints(ref cursor, variables, " + (this.UsesValueTypes ? "in " : string.Empty) + "fixedValue, 0, out var constraintFailure))");
        writer.Line("cursor.Complete(constraintFailure!);");
        writer.Line("value = " + (this.UsesValueTypes ? "default;" : "null;"));
        writer.Line("failure = constraintFailure;");
        writer.Line("return false;");
        writer.Close();
        writer.Line("value = fixedValue;");
        writer.Line("failure = null;");
        writer.Line("return true;");
        writer.Close();
        writer.Line("value = Read" + name + "Members(ref cursor, variables, null, null);");
        writer.Line("failure = null;");
        writer.Line("return true;");
        writer.Close();
        writer.Open("catch (global::CStructSharp.Diagnostics.CStructException exception)");
        writer.Line("cursor.Complete(exception);");
        writer.Line(this.UsesValueTypes ? "value = default;" : "value = null;");
        writer.Line("failure = exception;");
        writer.Line("return false;");
        writer.Close();
    }

    /// <summary>Emits one composite's fixed reader.</summary>
    /// <param name="writer">The generated source destination.</param>
    /// <param name="composite">An eligible composite.</param>
    /// <param name="plan">Its plan.</param>
    private void EmitFixedReader(SourceWriter writer, GeneratedComposite composite, FixedPlan plan)
    {
        string name = composite.Name;
        writer.Line("/// <summary>Reads one <c>" + composite.LayoutName + "</c> from exactly its " + Int(plan.Size) + " bytes, each member at its constant offset (reached only through <c>ReadCursor.TryTakeFixed</c>).</summary>");
        writer.Line("/// <param name=\"source\">The struct's bytes.</param>");
        writer.Line("/// <param name=\"trimFixedText\">Whether fixed-capacity text drops its trailing NUL padding.</param>");
        if (this.HasRuntimeByteOrder)
        {
            writer.Line(this.OrderParameterDoc);
        }

        writer.Line("/// <returns>The value.</returns>");
        writer.Open("private static " + name + " Read" + name + "Fixed(global::System.ReadOnlySpan<byte> source, bool trimFixedText" + this.TrailingOrderParameter + ")");
        if (this.UsesValueTypes)
        {
            // The fixed reader assigns every member of the struct, so the value need not be zeroed first.
            writer.Line(name + " value;");
            writer.Line("global::System.Runtime.CompilerServices.Unsafe.SkipInit(out value);");
        }
        else
        {
            writer.Line("var value = new " + name + "();");
        }

        if (this.HasRuntimeByteOrder && InlineFixedMembers(composite, plan))
        {
            // A small struct is filled by an inlined helper, so passing the order as a constant on each branch lets the JIT
            // drop the per-member byte-order test: the order is decided once per struct, not once per field.
            string reference = this.UsesValueTypes ? "ref " : string.Empty;
            writer.Open("if (littleEndian)");
            writer.Line("Fill" + name + "Fixed(source, trimFixedText, true, " + reference + "value);");
            writer.Close();
            writer.Open("else");
            writer.Line("Fill" + name + "Fixed(source, trimFixedText, false, " + reference + "value);");
            writer.Close();
        }
        else
        {
            writer.Line("Fill" + name + "Fixed(source, trimFixedText" + this.TrailingOrderArgument + ", " + (this.UsesValueTypes ? "ref " : string.Empty) + "value);");
        }

        writer.Line("return value;");
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>Decodes one <c>" + composite.LayoutName + "</c> from exactly its " + Int(plan.Size) + " bytes into a value just constructed, each member at its constant offset.</summary>");
        writer.Line("/// <param name=\"source\">The struct's bytes.</param>");
        writer.Line("/// <param name=\"trimFixedText\">Whether fixed-capacity text drops its trailing NUL padding.</param>");
        if (this.HasRuntimeByteOrder)
        {
            writer.Line(this.OrderParameterDoc);
        }

        writer.Line("/// <param name=\"value\">A new value, whose nested struct members are the new values its constructor created; they are filled in place.</param>");

        // Fixed leaf helpers expose constant offsets when inlined into their parent; array loops stay separate.
        if (InlineFixedMembers(composite, plan))
        {
            writer.Line("[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
        }

        writer.Open("private static void Fill" + name + "Fixed(global::System.ReadOnlySpan<byte> source, bool trimFixedText" + this.TrailingOrderParameter + ", " + (this.UsesValueTypes ? "ref " : string.Empty) + name + " value)");
        var scope = new ReaderScope(this, composite);

        // Without a constructor of the consumer's, the property initializers alone decide what a new value holds.
        bool nestedValuesAreNew = !this.UsesValueTypes && !this.request.TypesWithConstructors.Contains(name);
        foreach (CompiledField field in composite.Composite.Fields)
        {
            GeneratedMember member = scope.Member(field) ?? throw new InvalidOperationException("No generated member for field " + field.Name);
            this.EmitFixedMember(writer, field, member, "value." + member.PropertyName, nestedValuesAreNew);
        }

        writer.Close();
    }

    /// <summary>Bounds inlining hints to small fixed helpers without array loops.</summary>
    /// <param name="composite">The fixed composite whose helper is being emitted.</param>
    /// <param name="plan">Its storage and limit facts.</param>
    /// <returns>Whether exposing the member operations to the caller keeps expansion small.</returns>
    private static bool InlineFixedMembers(GeneratedComposite composite, FixedPlan plan)
    {
        // Array loops keep their own method body, while scalar helpers expose offsets to their callers.
        return plan.Size <= 64 && composite.Composite.Fields.Length <= 8 && composite.Composite.Fields.All(field => field.Array.Kind == CompiledArrayKind.Scalar);
    }

    /// <summary>Emits the statement that decodes one member of a fixed reader.</summary>
    /// <param name="writer">The generated source destination.</param>
    /// <param name="field">The member's compiled field.</param>
    /// <param name="member">The generated member.</param>
    /// <param name="property">The C# expression of the member's property on the value being filled.</param>
    /// <param name="nestedValuesAreNew">
    ///     Whether a nested struct member of the value is the new value its property initializer created, so it can be
    ///     decoded in place; otherwise the member is replaced with a newly read value.
    /// </param>
    private void EmitFixedMember(SourceWriter writer, CompiledField field, GeneratedMember member, string property, bool nestedValuesAreNew)
    {
        int offset = field.FixedOffset ?? throw new InvalidOperationException("Fixed member without an offset: " + field.Name);
        writer.Line("// " + DescribeDeclaration(field));
        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            if ((nestedValuesAreNew || this.UsesValueTypes) && member.Composite is { } nested && field.PointerDepth == 0)
            {
                // The value's constructor created this nested value (its property initializer), or the nested struct is a
                // field of a value type: decode into it instead of replacing it with another new one.
                int size = nested.Composite.Symbol.FixedSize ?? throw new InvalidOperationException("Fixed composite without a size: " + nested.Name);
                writer.Line("Fill" + nested.Name + "Fixed(source.Slice(" + Int(offset) + ", " + Int(size) + "), trimFixedText" + this.TrailingOrderArgument + ", " + (this.UsesValueTypes ? "ref " : string.Empty) + property + ");");
                return;
            }

            writer.Line(property + " = " + this.FixedElement(field, member, "source", Int(offset)) + ";");
            return;
        }

        int count = field.Array.FixedCount ?? throw new InvalidOperationException("Fixed array without a count: " + field.Name);
        if (field.IsCharacterArray)
        {
            // The member-by-member reader's TakeFixedText: one Latin-1 character per byte, TrimFixedText applied.
            writer.Line(property + " = " + CodecClass + ".DecodeFixedText(source.Slice(" + Int(offset) + ", " + Int(count) + "), trimFixedText);");
            return;
        }

        string elementType = ElementType(member.TypeName, 1);
        writer.Open(string.Empty);
        if (this.InlineArrayOf(member) is not null)
        {
            // A value-type layout keeps the array in a field of the struct: decode straight into it.
            if (member.Composite is { } element)
            {
                // Each struct of the array is filled in place at its stride.
                int elementSize = element.Composite.Symbol.FixedSize ?? throw new InvalidOperationException("Fixed composite without a size: " + element.Name);
                int elementStride = field.FixedElementSize ?? throw new InvalidOperationException("Fixed array without an element size: " + field.Name);
                writer.Open("for (int index = 0; index < " + Int(count) + "; index++)");
                writer.Line("Fill" + element.Name + "Fixed(source.Slice(" + Int(offset) + " + index * " + Int(elementStride) + ", " + Int(elementSize) + "), trimFixedText" + this.TrailingOrderArgument + ", ref " + property + "[index]);");
                writer.Close();
            }
            else
            {
                writer.Line("global::System.ReadOnlySpan<byte> bytes = source.Slice(" + Int(offset) + ", " + Int(count * field.Codec.Size) + ");");
                if (member.Enum is not null)
                {
                    // An enum's elements are its stored numbers: decode into the same storage, viewed as the underlying type.
                    string underlying = member.Enum.UnderlyingType;
                    writer.Line(BulkDecode(field.Codec, underlying, "bytes", "global::System.Runtime.InteropServices.MemoryMarshal.Cast<" + elementType + ", " + underlying + ">((global::System.Span<" + elementType + ">)" + property + ")", this.EndianOf(field), elementsAreSpan: true));
                }
                else
                {
                    writer.Line(BulkDecode(field.Codec, elementType, "bytes", property, this.EndianOf(field), elementsAreSpan: true));
                }
            }

            writer.Close();
            return;
        }

        if (member.Composite is null && member.Enum is null)
        {
            // The bulk decode the member-by-member reader applies to the same bytes; it overwrites every element, so
            // the array is allocated without zeroing.
            writer.Line("var elements = " + UninitializedArray(elementType, Int(count)) + ";");
            writer.Line("global::System.ReadOnlySpan<byte> bytes = source.Slice(" + Int(offset) + ", " + Int(count * field.Codec.Size) + ");");
            writer.Line(BulkDecode(field.Codec, elementType, "bytes", "elements", this.EndianOf(field)));
        }
        else
        {
            writer.Line("var elements = new " + elementType + "[" + Int(count) + "];");
            int stride = field.FixedElementSize ?? throw new InvalidOperationException("Fixed array without an element size: " + field.Name);
            writer.Open("for (int index = 0; index < " + Int(count) + "; index++)");
            writer.Line("elements[index] = " + this.FixedElement(field, member, "source", Int(offset) + " + index * " + Int(stride)) + ";");
            writer.Close();
        }

        writer.Line(property + " = elements;");
        writer.Close();
    }

    /// <summary>The expression decoding one scalar or array element of a fixed member at <paramref name="offset"/>.</summary>
    private string FixedElement(CompiledField field, GeneratedMember member, string source, string offset)
    {
        if (member.Composite is { } nested)
        {
            int size = nested.Composite.Symbol.FixedSize ?? throw new InvalidOperationException("Fixed composite without a size: " + nested.Name);
            return "Read" + nested.Name + "Fixed(" + source + ".Slice(" + offset + ", " + Int(size) + "), trimFixedText" + this.TrailingOrderArgument + ")";
        }

        string slice = source + ".Slice(" + offset + ", " + Int(field.Codec.Size) + ")";
        return member.Enum is not null ? "(" + ElementType(member.TypeName, 1) + ")" + NumericDecode(field.Codec, slice, this.EndianOf(field)) : NumericDecode(field.Codec, slice, this.EndianOf(field));
    }

    /// <summary>
    ///     Decides whether <paramref name="composite"/> has a fixed reader: the conditions of the runtime's static read
    ///     plan, applied to what the generated member-by-member reader does, and the same for every nested composite.
    /// </summary>
    /// <param name="composite">The composite.</param>
    /// <param name="depth">The nesting depth of this check, bounded like the runtime's plan builder.</param>
    /// <returns>The plan, or <see langword="null"/> when a member needs the member-by-member reader.</returns>
    private FixedPlan? FixedPlanOf(GeneratedComposite composite, int depth)
    {
        if (this.fixedPlans.TryGetValue(composite, out FixedPlan? known))
        {
            return known;
        }

        FixedPlan? plan = depth > FixedLayoutRule.MaximumNestingDepth ? null : this.BuildFixedPlan(composite, depth);
        this.fixedPlans[composite] = plan;
        return plan;
    }

    /// <summary>Checks every member of <paramref name="composite"/> and sums what its fixed reader needs; see <see cref="FixedPlanOf"/>.</summary>
    /// <param name="composite">The composite.</param>
    /// <param name="depth">The nesting depth of this check.</param>
    /// <returns>The plan, or <see langword="null"/> when a member needs the member-by-member reader.</returns>
    private FixedPlan? BuildFixedPlan(GeneratedComposite composite, int depth)
    {
        CompiledCompositeType compiled = composite.Composite;
        if (!FixedLayoutRule.IsFixedComposite(compiled, allowConstraints: true) || compiled.Symbol.FixedSize is not int size)
        {
            return null;
        }

        var scope = new ReaderScope(this, composite);
        long charged = 0;
        int nesting = 1;
        int arrays = 0;
        foreach (CompiledField field in compiled.Fields)
        {
            // A fixed member (FixedLayoutRule) that is named: the member-by-member reader then only seeks, takes and
            // decodes, which the fixed reader reproduces. Unnamed padding has no member, so it takes that reader.
            if (!FixedLayoutRule.IsFixedMember(field) || field.IsUnnamed || scope.Member(field) is not { IsConditional: false } member)
            {
                return null;
            }

            bool scalar = field.Array.Kind == CompiledArrayKind.Scalar;
            int count = field.Array.FixedCount ?? 1;
            if (!scalar)
            {
                arrays = Math.Max(arrays, count);
            }

            if (member.Composite is { } nested)
            {
                // A nested struct's conditions are checked by its own reader, which a parent's fixed decode bypasses.
                if (nested.IsUnion || nested.Composite.HasConstraints || this.FixedPlanOf(nested, depth + 1) is not { } nestedPlan || (!scalar && field.FixedElementSize != nestedPlan.Size))
                {
                    return null;
                }

                charged += nestedPlan.ChargedBytes * count;
                nesting = Math.Max(nesting, 1 + nestedPlan.NestingLevels);
                arrays = Math.Max(arrays, nestedPlan.MaximumArrayCount);
                continue;
            }

            bool fixedText = !scalar && field.IsCharacterArray && !field.IsWideCharElement && field.Codec.Size == 1 && !BoundedTextCodec.IsType(field.TypeSpelling);
            bool numeric = member.Enum is null
                               ? (scalar ? IsFixedDecodable(field.Codec) : field.Codec.IsFixedWidthNumeric && !field.IsCharacterArray && !BoundedTextCodec.IsType(field.TypeSpelling))
                               : field.Codec.IsFixedWidthNumeric;
            if (!fixedText && !numeric)
            {
                return null;
            }

            charged += (long)field.Codec.Size * count;
        }

        return new FixedPlan(size, compiled.Symbol.Alignment, charged, nesting, arrays);
    }

    /// <summary>Whether a scalar codec is read by the member-by-member reader's fixed-width numeric decode.</summary>
    private static bool IsFixedDecodable(PrimitiveCodec codec)
    {
        switch (codec.Kind)
        {
        case PrimitiveCodecKind.UInt8:
        case PrimitiveCodecKind.Int8:
        case PrimitiveCodecKind.Bool:
        case PrimitiveCodecKind.Char:
        case PrimitiveCodecKind.WChar:
        case PrimitiveCodecKind.Int16:
        case PrimitiveCodecKind.UInt16:
        case PrimitiveCodecKind.Int24:
        case PrimitiveCodecKind.UInt24:
        case PrimitiveCodecKind.Int32:
        case PrimitiveCodecKind.UInt32:
        case PrimitiveCodecKind.Int48:
        case PrimitiveCodecKind.UInt48:
        case PrimitiveCodecKind.Int64:
        case PrimitiveCodecKind.UInt64:
        case PrimitiveCodecKind.Int128:
        case PrimitiveCodecKind.UInt128:
        case PrimitiveCodecKind.Float16:
        case PrimitiveCodecKind.Float32:
        case PrimitiveCodecKind.Float64:
            return true;
        default:
            return false;
        }
    }

    /// <summary>What <c>ReadCursor.TryTakeFixed</c> checks before handing over a fixed composite's bytes.</summary>
    /// <param name="Size">The composite's storage size, tail padding included.</param>
    /// <param name="Alignment">The composite's alignment, which its start must meet in an aligned layout.</param>
    /// <param name="ChargedBytes">The bytes the member-by-member reader charges to the read budget (members only, not padding).</param>
    /// <param name="NestingLevels">The composite levels it enters, itself included.</param>
    /// <param name="MaximumArrayCount">The largest fixed array count it reads, for the array element limit.</param>
    private sealed record FixedPlan(int Size, int Alignment, long ChargedBytes, int NestingLevels, int MaximumArrayCount);
}
