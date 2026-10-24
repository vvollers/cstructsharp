namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using static CStructSharp.Generators.Emit;

/// <summary>
///     The views: one <c>readonly ref struct &lt;Type&gt;View</c> per composite over the composite's bytes, with a
///     direct decode at a constant offset for every statically placed scalar, a nested view for a statically placed
///     struct, the raw bytes, one element and a bulk copy of a numeric array (fixed, or sized by earlier members), text
///     and custom codec members, and <c>ToObject()</c> for everything else. A member after a runtime-sized array is read at
///     an offset the view computes from the members before it. Nothing is allocated until a string, a custom codec value,
///     or <c>ToObject()</c> is asked for.
/// </summary>
internal sealed partial class LayoutEmitter
{
    /// <summary>Emits the view of every composite, unless views are switched off.</summary>
    /// <param name="writer">The output.</param>
    private void EmitViews(SourceWriter writer)
    {
        if (!this.request.Views)
        {
            return;
        }

        this.UseRuntimeOrder("this.littleEndian");
        foreach (GeneratedComposite composite in this.model.Composites)
        {
            writer.Line();
            this.EmitView(writer, composite);
        }
    }

    private static string ViewName(GeneratedComposite composite) => composite.Name + "View";

    /// <summary>What a view passes to a nested view or enumerator between the source and the options when the byte order is chosen at read time: its own order, or nothing.</summary>
    private string ViewOrderPrefix => this.HasRuntimeByteOrder ? "this.littleEndian, " : string.Empty;

    /// <summary>Whether views of fixed-size structs get a <c>foreach</c> enumerator: always on the full surface, otherwise when the layout asks for it.</summary>
    private bool EmitsEnumerators => this.request.Surface == GenerationSurface.Full || this.request.Enumerators;

    /// <summary>The arguments that follow the options when a view hands its caller variables on to a nested view or enumerator: empty unless the layout's views carry variables.</summary>
    private string VariablesArgument => this.ViewsCarryVariables ? ", this.variables" : string.Empty;

    /// <summary>
    ///     Whether the views of this layout carry the caller's variables: some view evaluates a count that reads a caller
    ///     variable, or a define the caller may override, so every view of the layout accepts the variables and passes them
    ///     to the views nested in it.
    /// </summary>
    private bool ViewsCarryVariables => this.viewsCarryVariables ??= this.request.Views && this.model.Composites.Any(composite => this.PlannedView(composite).UsesVariables);

    /// <summary>What a view's read options are used for: the whole-value reader, or (on the views surface, which has none) the text members.</summary>
    private string OptionsUse => this.request.Surface == GenerationSurface.Views ? "each view's text members use" : "each view's <c>ToObject</c> uses";

    /// <summary>Emits the view of one composite: a <c>ref struct</c> over its bytes whose statically placed members decode on access.</summary>
    /// <param name="writer">The output.</param>
    /// <param name="composite">The composite.</param>
    private void EmitView(SourceWriter writer, GeneratedComposite composite)
    {
        string name = ViewName(composite);
        string layout = SourceWriter.Literal(composite.LayoutName);
        int? size = composite.Composite.Symbol.FixedSize;
        writer.Line("/// <summary>");
        writer.Line("///     A zero-allocation view of one <c>" + composite.LayoutName + "</c> over its bytes: each statically placed member decodes");
        if (this.request.Surface == GenerationSurface.Views)
        {
            writer.Line("///     directly from the span when read, or at an offset computed after a runtime-sized array; a member the view does not expose");
            writer.Line("///     (conditional, a pointer's target, a terminated string, or placed after a member of unknown size) is not available with this surface.");
            writer.Line("///     A view cannot leave the method that created it.");
        }
        else
        {
            writer.Line("///     directly from the span when read, or at an offset computed after a runtime-sized array; a member the view does not expose");
            writer.Line("///     (conditional, a pointer's target, a terminated string, or placed after a member of unknown size) is reached through");
            writer.Line("///     <see cref=\"ToObject\"/>. A view cannot leave the method that created it.");
        }

        writer.Line("/// </summary>");
        writer.Open("public readonly ref struct " + name);
        writer.Line("private readonly global::System.ReadOnlySpan<byte> source;");
        writer.Line("private readonly global::CStructSharp.ReadOptions? options;");
        if (this.HasRuntimeByteOrder)
        {
            writer.Line("private readonly bool littleEndian;");
        }

        if (this.ViewsCarryVariables)
        {
            writer.Line("private readonly " + VariablesType + " variables;");
        }

        writer.Line();
        writer.Line("/// <summary>Creates a view over <paramref name=\"source\"/>, whose first byte is the value's first byte" + (size is not null ? "; the source must hold the value's " + Int(size.Value) + " bytes" : string.Empty) + ".</summary>");
        writer.Line("/// <param name=\"source\">The bytes, from the value's start; more may follow (pointer targets, the rest of the input).</param>");
        this.EmitOrderDoc(writer);
        writer.Line("/// <param name=\"options\">The read options " + (this.request.Surface == GenerationSurface.Views ? "text members use" : "<see cref=\"ToObject\"/> uses") + "; <see langword=\"null\"/> uses the documented defaults.</param>");
        if (this.ViewsCarryVariables)
        {
            writer.Line("/// <param name=\"variables\">The values the layout's free identifiers have where a count needs them, or <see langword=\"null\"/>.</param>");
        }

        writer.Open("public " + name + "(global::System.ReadOnlySpan<byte> source, " + this.OrderParameter + "global::CStructSharp.ReadOptions? options = null" + (this.ViewsCarryVariables ? ", " + VariablesType + " variables = null" : string.Empty) + ")");
        if (size is { } fixedSize)
        {
            // The runtime's short-read text for a source that cannot hold the value. The failure is built in a local
            // function, so the constructor stays small enough for the JIT to inline at every nested view access.
            writer.Open("if (source.Length < " + Int(fixedSize) + ")");
            writer.Line("throw ShortSource(source, options);");
            writer.Close();
        }

        writer.Line("this.source = source;");
        writer.Line("this.options = options;");
        if (this.HasRuntimeByteOrder)
        {
            writer.Line("this.littleEndian = littleEndian;");
        }

        if (this.ViewsCarryVariables)
        {
            writer.Line("this.variables = variables;");
        }

        if (size is { } checkedSize)
        {
            writer.Line();
            writer.Line("// Builds the short-read failure, with the runtime's context, for a source shorter than the value.");
            writer.Open("static global::CStructSharp.Diagnostics.CStructException ShortSource(global::System.ReadOnlySpan<byte> source, global::CStructSharp.ReadOptions? options)");
            writer.Line("var cursor = new global::CStructSharp.Generated.ReadCursor(source, options, " + layout + ");");
            writer.Line("global::CStructSharp.Diagnostics.CStructException failure = cursor.Fail(global::CStructSharp.Generated.ReadCursor.ShortReadText(" + Int(checkedSize) + ", source.Length), null, null);");
            writer.Line("cursor.Complete(failure);");
            writer.Line("return failure;");
            writer.Close();
        }

        writer.Close();
        writer.Line();
        writer.Line("/// <summary>Gets the value's bytes" + (size is not null ? " (its " + Int(size.Value) + " bytes)" : ": the whole source, as the value's extent depends on its contents") + ".</summary>");
        writer.Line("public global::System.ReadOnlySpan<byte> Bytes => this.source" + (size is { } bytes ? ".Slice(0, " + Int(bytes) + ")" : string.Empty) + ";");
        ViewPlan plan = this.PlannedView(composite);
        foreach (GeneratedMember member in composite.Members)
        {
            this.EmitViewMember(writer, composite, member, plan);
        }

        this.EmitViewHelpers(writer, composite, plan);
        this.EmitViewValidate(writer, composite, plan);

        if (this.request.Surface != GenerationSurface.Views)
        {
            this.EmitViewToObject(writer, composite);
        }

        if (size is { } stride && this.EmitsEnumerators)
        {
            writer.Line();
            writer.Line("/// <summary>Views over the records of <paramref name=\"source\"/> - one <c>" + composite.LayoutName + "</c> of " + Int(stride) + " bytes after another until the source ends - for <c>foreach</c>, allocating nothing. Trailing bytes shorter than one record fail on the step that meets them; each record is its own region (a stored absolute pointer address counts from the record's first byte).</summary>");
            writer.Line("/// <param name=\"source\">The bytes of the records, with nothing else after them.</param>");
            writer.Line("/// <param name=\"options\">The read options each view's " + (this.request.Surface == GenerationSurface.Views ? "text members use" : "<see cref=\"ToObject\"/> uses") + "; <see langword=\"null\"/> uses the documented defaults.</param>");
            writer.Line("/// <returns>The enumeration.</returns>");
            string enumeration = this.request.Surface == GenerationSurface.Full ? name + "Enumerable" : name + "Enumerator";
            writer.Line("public static " + enumeration + " Enumerate(global::System.ReadOnlySpan<byte> source, " + this.OrderParameter + "global::CStructSharp.ReadOptions? options = null" + (this.ViewsCarryVariables ? ", " + VariablesType + " variables = null" : string.Empty) + ") => new " + enumeration + "(source, " + this.OrderArgument + "options" + this.VariablesArgument.Replace("this.variables", "variables") + ");");
        }

        writer.Close();
        if (size is { } recordSize && this.EmitsEnumerators)
        {
            this.EmitViewEnumerator(writer, composite, recordSize);
        }
    }

    /// <summary>
    ///     Emits <c>Validate()</c> for a struct whose members carry <c>@expect</c> or <c>@supports</c> conditions: it checks
    ///     every condition the view can evaluate - one that names a member the view does not expose is left to the reader -
    ///     in declaration order, with the reader's failures. A view reads lazily and never checks on its own.
    /// </summary>
    /// <param name="writer">The output.</param>
    /// <param name="composite">The composite.</param>
    /// <param name="plan">The view plan, which collected the checks.</param>
    private void EmitViewValidate(SourceWriter writer, GeneratedComposite composite, ViewPlan plan)
    {
        if (plan.Checks.Count == 0)
        {
            return;
        }

        string name = plan.Unique("Validate");
        string checks = plan.Unique("ValidateChecks");
        string layout = SourceWriter.Literal(composite.LayoutName);
        string orderArgument = this.HasRuntimeByteOrder ? ", this.littleEndian" : string.Empty;
        string variablesLine = this.ViewsCarryVariables ? "global::System.Collections.Generic.IReadOnlyDictionary<string, int>? variables = this.variables;" : NoVariables;
        writer.Line();
        writer.Line("/// <summary>Checks the <c>@expect</c> and <c>@supports</c> conditions of this struct's members, in declaration order, with the reader's failures; a view never checks them on its own.</summary>");
        writer.Line("/// <param name=\"violations\">When given, receives every condition a member breaks and the check goes on (see <see cref=\"global::CStructSharp.ReadOptions.ConstraintViolations\"/>), instead of the first one throwing.</param>");
        writer.Line("/// <exception cref=\"global::CStructSharp.Diagnostics.CStructConstraintException\">A member's value does not satisfy a condition and <paramref name=\"violations\"/> is not given.</exception>");
        writer.Open("public void " + name + "(global::System.Collections.Generic.ICollection<global::CStructSharp.Diagnostics.CStructConstraintException>? violations = null)");
        writer.Line(variablesLine);
        writer.Line("var cursor = new " + Cursor + "(this.source, violations is null ? this.options : (this.options ?? new global::CStructSharp.ReadOptions()) with { ConstraintViolations = violations }, " + layout + orderArgument + ");");
        writer.Open("try");
        writer.Open("if (!this." + checks + "(ref cursor, variables, out var failure))");
        writer.Line("throw failure!;");
        writer.Close();
        writer.Close();
        writer.Open("catch (global::CStructSharp.Diagnostics.CStructException exception)");
        writer.Line("cursor.Complete(exception);");
        writer.Line("throw;");
        writer.Close();
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>Checks the <c>@expect</c> and <c>@supports</c> conditions of this struct's members like <see cref=\"" + name + "\"/>, but returns the failure instead of throwing it: a constraint that is not satisfied allocates the failure and nothing is thrown.</summary>");
        writer.Line("/// <param name=\"failure\">The first condition that is not satisfied, or the failure of a condition that could not be evaluated; <see langword=\"null\"/> when every condition holds.</param>");
        writer.Line("/// <returns>Whether every condition holds.</returns>");
        writer.Open("public bool Try" + name + "([global::System.Diagnostics.CodeAnalysis.NotNullWhen(false)] out global::CStructSharp.Diagnostics.CStructException? failure)");
        writer.Line(variablesLine);
        writer.Line("var cursor = new " + Cursor + "(this.source, this.options, " + layout + orderArgument + ");");
        writer.Open("try");
        writer.Open("if (this." + checks + "(ref cursor, variables, out var stopped))");
        writer.Line("failure = null;");
        writer.Line("return true;");
        writer.Close();
        writer.Line("cursor.Complete(stopped!);");
        writer.Line("failure = stopped;");
        writer.Line("return false;");
        writer.Close();
        writer.Open("catch (global::CStructSharp.Diagnostics.CStructException exception)");
        writer.Line("cursor.Complete(exception);");
        writer.Line("failure = exception;");
        writer.Line("return false;");
        writer.Close();
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>Runs the checks of <see cref=\"" + name + "\"/> in declaration order; a condition that is not satisfied stops them with its failure unless the read collects violations.</summary>");
        writer.Open("private bool " + checks + "(ref " + Cursor + " cursor, global::System.Collections.Generic.IReadOnlyDictionary<string, int>? variables, out global::CStructSharp.Diagnostics.CStructConstraintException? failure)");
        writer.Line("failure = null;");
        foreach ((CompiledField field, CompiledConstraint constraint, string code, string? end) in plan.Checks)
        {
            EmitConstraintBlock(writer, field, constraint, code, SourceWriter.Literal(field.Name), SourceWriter.Literal(field.DisplayTypeSpelling), end, returnsFailure: true);
        }

        writer.Line("return true;");
        writer.Close();
    }

    /// <summary>Emits <c>ToObject()</c>, which reads the whole value with the generated reader (every surface except <c>Views</c>).</summary>
    /// <param name="writer">The output.</param>
    /// <param name="composite">The composite.</param>
    private void EmitViewToObject(SourceWriter writer, GeneratedComposite composite)
    {
        string layout = SourceWriter.Literal(composite.LayoutName);
        writer.Line();
        writer.Line("/// <summary>Reads the whole value with the generated reader (every member, runtime-sized ones included).</summary>");
        writer.Line(VariablesDoc);
        writer.Line("/// <returns>The value.</returns>");
        writer.Open("public " + composite.Name + " ToObject(" + VariablesType + " variables = null)");
        writer.Line("var cursor = new " + Cursor + "(this.source, this.options, " + layout + (this.HasRuntimeByteOrder ? ", this.littleEndian" : string.Empty) + ");");
        writer.Open("try");
        writer.Line("return Read" + composite.Name + "(ref cursor, " + (this.ViewsCarryVariables ? "variables ?? this.variables" : "variables") + ", null, null);");
        writer.Close();
        writer.Open("catch (global::CStructSharp.Diagnostics.CStructException exception)");
        writer.Line("cursor.Complete(exception);");
        writer.Line("throw;");
        writer.Close();
        writer.Close();
    }

    /// <summary>
    ///     Emits one view property that decodes a member straight from the struct's bytes, at a build-time offset or at
    ///     one the view computes when the member sits after a runtime-sized array; a conditional member, a bitfield or
    ///     union placement the plan cannot follow, and a separator get none.
    /// </summary>
    /// <param name="writer">The output.</param>
    /// <param name="composite">The struct or union the view reads.</param>
    /// <param name="member">The member.</param>
    /// <param name="plan">Where the view finds each member.</param>
    private void EmitViewMember(SourceWriter writer, GeneratedComposite composite, GeneratedMember member, ViewPlan plan)
    {
        CompiledField field = member.Field;
        if (member.IsConditional || field.IsZeroWidthBitfield || !plan.Slots.TryGetValue(field, out ViewSlot? slot))
        {
            return;
        }

        string at = slot.At;
        string bytes = "this.source";
        string name = SourceWriter.Literal(field.Name);
        string typeName = SourceWriter.Literal(field.DisplayTypeSpelling);
        string summary = "/// <summary><c>" + DescribeDeclaration(field) + "</c> at " + slot.Where + ".</summary>";

        // The bytes [start, start + length) of the source: a slice at a build-time offset, a checked take after a runtime-sized array.
        string Slice(string start, string length) => slot.IsDynamic
                                                         ? plan.TakeName + "(" + start + ", " + length + ", " + name + ", " + typeName + ")"
                                                         : bytes + ".Slice(" + start + ", " + length + ")";
        if (field.BitSize > 0)
        {
            int unitSize = field.BitUnitSize ?? field.BitStorageSize ?? field.Codec.Size;
            bool littleEndian = field.BitStorageIsLittleEndian ?? true;
            string cast = "(" + member.TypeName + ")" + (member.Enum is not null ? "(" + member.Enum.UnderlyingType + ")" : string.Empty);
            writer.Line();
            writer.Line(summary);
            writer.Line("public " + member.TypeName + " " + member.PropertyName + " => " + cast + CodecClass + ".ExtractBits(" + CodecClass + ".ReadUnsigned(" + Slice(at, Int(unitSize)) + ", " + this.EndianExpression(field, littleEndian) + "), " + CodecClass + ".BitfieldShift(" + Int(field.BitOffset) + ", " + Int(field.BitSize) + ", " + Int(unitSize * 8) + ", HighBitFirst), " + Int(field.BitSize) + ");");
            return;
        }

        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            this.EmitViewScalar(writer, member, slot, plan, summary, Slice);
            return;
        }

        if (field.PointerDepth > 0 || field.Array.Dimensions.Length != 1)
        {
            return;
        }

        if (member.Composite is not null)
        {
            // An array of fixed-size structs (or unions): one nested view per element, at a stride of the element's size.
            if (field.Array.Kind == CompiledArrayKind.Fixed && field.Array.FixedCount is { } elements)
            {
                this.EmitViewStructArray(writer, member, slot, plan, Slice, elements, null);
            }
            else if (field.Array.Kind == CompiledArrayKind.Runtime && plan.Counts.TryGetValue(field, out (string Method, string Code, bool IsLiteral) structCount))
            {
                // A computed length may run past the source, so a runtime-sized array always slices through the checked take.
                this.EmitViewStructArray(writer, member, slot, plan, (start, length) => plan.TakeName + "(" + start + ", " + length + ", " + name + ", " + typeName + ")", null, structCount);
            }

            return;
        }

        if (field.Array.Kind == CompiledArrayKind.Fixed && field.Array.FixedCount is { } count)
        {
            this.EmitViewArray(writer, composite, member, slot, plan, Slice, count);
        }
        else if (field.Array.Kind == CompiledArrayKind.Runtime && plan.Counts.TryGetValue(field, out (string Method, string Code, bool IsLiteral) runtimeCount))
        {
            // A computed length may run past the source, so a runtime-sized array always slices through the checked take.
            this.EmitViewArray(writer, composite, member, slot, plan, (start, length) => plan.TakeName + "(" + start + ", " + length + ", " + name + ", " + typeName + ")", null, runtimeCount);
        }
    }

    /// <summary>Emits the property of a scalar member: a stored pointer address, a nested view, an enum, a number, an identifier, a custom codec's value, or nothing.</summary>
    /// <param name="writer">The output.</param>
    /// <param name="member">The member.</param>
    /// <param name="slot">Where the member starts.</param>
    /// <param name="plan">The view plan, which names the helpers.</param>
    /// <param name="summary">The member's documentation line.</param>
    /// <param name="slice">Builds the expression for the bytes at an offset and length.</param>
    private void EmitViewScalar(SourceWriter writer, GeneratedMember member, ViewSlot slot, ViewPlan plan, string summary, Func<string, string, string> slice)
    {
        CompiledField field = member.Field;
        string at = slot.At;
        if (field.PointerDepth > 0)
        {
            string addressName = plan.Unique(member.PropertyName + "Address");
            writer.Line();
            writer.Line("/// <summary>The stored address of <c>" + DescribeDeclaration(field) + "</c> at " + slot.Where + (this.request.Surface == GenerationSurface.Views ? "." : " (the target is read by <see cref=\"ToObject\"/>).") + "</summary>");
            writer.Line("public long " + addressName + " => (long)" + CodecClass + ".ReadUnsigned(" + slice(at, "PointerSize") + ", " + this.PointerEndian + ");");
            return;
        }

        if (member.Composite is { } nested)
        {
            if (nested.Composite.Symbol.FixedSize is not { } nestedSize || !this.request.Views)
            {
                return;
            }

            writer.Line();
            writer.Line("/// <summary>A view of <c>" + DescribeDeclaration(field) + "</c> at " + slot.Where + ".</summary>");

            // Both placements hand the nested view the rest of the source, so its pointer targets resolve the same way; at a
            // computed offset the member's own bytes are still checked first.
            string nestedSource = "this.source.Slice(" + at + ")";
            if (slot.IsDynamic)
            {
                plan.TakeRestName ??= plan.Unique("TakeRest");
                nestedSource = plan.TakeRestName + "(" + at + ", " + Int(nestedSize) + ", " + SourceWriter.Literal(field.Name) + ", " + SourceWriter.Literal(field.DisplayTypeSpelling) + ")";
            }

            writer.Line("public " + ViewName(nested) + " " + member.PropertyName + " => new(" + nestedSource + ", " + this.ViewOrderPrefix + "this.options" + this.VariablesArgument + ");");
            return;
        }

        if (member.Enum is not null)
        {
            PrimitiveCodec storage = PrimitiveCodec.Resolve(member.Enum.Compiled.Integer.StorageType, field.Codec.LittleEndian);
            writer.Line();
            writer.Line(summary);
            writer.Line("public " + member.TypeName + " " + member.PropertyName + " => (" + member.TypeName + ")" + NumericDecode(storage, slice(at, Int(storage.Size)), this.EndianOf(field)) + ";");
            return;
        }

        if (field.Codec.IsFixedWidthNumeric || field.Codec.IsIdentifier || field.Codec.IsFixedPoint)
        {
            writer.Line();
            writer.Line(summary);
            writer.Line("public " + member.TypeName + " " + member.PropertyName + " => " + ScalarDecode(field.Codec, slice(at, Int(field.Codec.Size)), this.EndianOf(field)) + ";");
            return;
        }

        if (field.Type.Symbol.IsCustomCodec && field.FixedStorageSize is { } customSize)
        {
            this.EmitViewCustom(writer, member, slot, customSize, slice, plan);
        }
    }

    /// <summary>Emits a custom codec member: its stored bytes, and its decoded value through the codec instance with the reader's failures.</summary>
    /// <param name="writer">The output.</param>
    /// <param name="member">The member.</param>
    /// <param name="slot">Where the member starts.</param>
    /// <param name="size">The codec's declared size in bytes.</param>
    /// <param name="slice">Builds the expression for the bytes at an offset and length.</param>
    /// <param name="plan">The view plan, which names the helpers.</param>
    private void EmitViewCustom(SourceWriter writer, GeneratedMember member, ViewSlot slot, int size, Func<string, string, string> slice, ViewPlan plan)
    {
        CompiledField field = member.Field;
        string at = slot.At;
        string bytesName = plan.Unique(member.PropertyName + "Bytes");
        writer.Line();
        writer.Line("/// <summary>The bytes of <c>" + DescribeDeclaration(field) + "</c> at " + slot.Where + ".</summary>");
        writer.Line("public global::System.ReadOnlySpan<byte> " + bytesName + " => " + slice(at, Int(size)) + ";");
        writer.Line();
        writer.Line("/// <summary><c>" + DescribeDeclaration(field) + "</c> decoded by its custom codec, as the reader does; the codec boxes its value.</summary>");
        writer.Open("public object? " + member.PropertyName);
        writer.Open("get");
        writer.Line("var cursor = new " + Cursor + "(" + bytesName + ", this.options, " + SourceWriter.Literal(field.Name) + ");");
        writer.Open("try");
        writer.Line("return cursor.TakeCustom(CodecInstances.Value[" + Int(this.CodecIndex(field.Type.Symbol.Name)) + "], " + SourceWriter.Literal(field.Name) + ", " + SourceWriter.Literal(field.DisplayTypeSpelling) + ");");
        writer.Close();
        writer.Open("catch (global::CStructSharp.Diagnostics.CStructException exception)");
        writer.Line("cursor.Complete(exception);");
        writer.Line("throw;");
        writer.Close();
        writer.Close();
        writer.Close();
    }

    /// <summary>
    ///     Emits a one-dimensional array member: text as its bytes and a string, numbers as their bytes, one element, a
    ///     bulk copy, and (when the length is computed) the element count.
    /// </summary>
    /// <param name="writer">The output.</param>
    /// <param name="composite">The composite the view reads.</param>
    /// <param name="member">The member.</param>
    /// <param name="slot">Where the member starts.</param>
    /// <param name="plan">The view plan.</param>
    /// <param name="slice">Builds the expression for the bytes at an offset and length.</param>
    /// <param name="fixedCount">The element count when the layout fixes it, or <see langword="null"/>.</param>
    /// <param name="runtimeCount">The count method and expression of a runtime-sized array.</param>
    private void EmitViewArray(SourceWriter writer, GeneratedComposite composite, GeneratedMember member, ViewSlot slot, ViewPlan plan, Func<string, string, string> slice, int? fixedCount, (string Method, string Code, bool IsLiteral)? runtimeCount = null)
    {
        CompiledField field = member.Field;
        string at = slot.At;
        string property = member.PropertyName;
        string count = fixedCount is { } fixedValue ? Int(fixedValue) : runtimeCount!.Value.Method + "()";

        // The slice of a fixed character or numeric array at a build-time offset keeps its established name (a clash is a
        // CSG003 error). CSG003 does not see a member placed at a computed offset, and every other bytes member is new, so a
        // clash only renames it.
        bool established = fixedCount is not null && !slot.IsDynamic && (field.IsCharacterArray || (!BoundedTextCodec.IsType(field.TypeSpelling) && (field.Codec.IsFixedWidthNumeric || member.Enum is not null)));
        string bytesName = established ? property + "Bytes" : plan.Unique(property + "Bytes");
        if (runtimeCount is { } computed && plan.WrittenCounts.Add(computed.Method))
        {
            this.EmitCountMethod(writer, member, computed, plan);
        }

        if (field.IsCharacterArray)
        {
            int width = field.IsWideCharElement ? 2 : 1;
            string length = fixedCount is { } c ? Int(c * width) : "(long)" + count + " * " + Int(width);
            writer.Line();
            writer.Line("/// <summary>The bytes of <c>" + DescribeDeclaration(field) + "</c> at " + slot.Where + ".</summary>");
            writer.Line("public global::System.ReadOnlySpan<byte> " + bytesName + " => " + slice(at, length) + ";");
            writer.Line();
            writer.Line("/// <summary><c>" + DescribeDeclaration(field) + "</c> as text (trailing NULs kept unless the options set <c>TrimFixedText</c>, as the reader does); allocates the string.</summary>");
            string littleEndian = this.WideEndian(field);
            writer.Line("public string " + property + " => " + (field.IsWideCharElement
                                                                    ? CodecClass + ".DecodeWideText(" + bytesName + ", " + littleEndian + ", this.options)"
                                                                    : CodecClass + ".DecodeFixedText(" + bytesName + ", " + CodecClass + ".TrimsFixedText(this.options))") + ";");
            return;
        }

        if (BoundedTextCodec.IsType(field.TypeSpelling))
        {
            this.EmitViewBoundedText(writer, member, slot, slice, fixedCount, count, bytesName);
            return;
        }

        if (!(field.Codec.IsFixedWidthNumeric || member.Enum is not null))
        {
            return;
        }

        PrimitiveCodec element = member.Enum is not null ? PrimitiveCodec.Resolve(member.Enum.Compiled.Integer.StorageType, field.Codec.LittleEndian) : field.Codec;
        string elementType = ElementType(member.TypeName, 1);
        string byteLength = fixedCount is { } n ? Int(n * element.Size) : "(long)" + count + " * " + Int(element.Size);
        writer.Line();
        writer.Line("/// <summary>The bytes of <c>" + DescribeDeclaration(field) + "</c> at " + slot.Where + ".</summary>");
        writer.Line("public global::System.ReadOnlySpan<byte> " + bytesName + " => " + slice(at, byteLength) + ";");
        writer.Line();
        writer.Line("/// <summary>One element of <c>" + DescribeDeclaration(field) + "</c>.</summary>");
        writer.Line("/// <param name=\"index\">The element index.</param>");
        writer.Line("/// <returns>The element.</returns>");
        writer.Open("public " + elementType + " " + property + "(int index)");
        writer.Open("if ((uint)index >= " + (fixedCount is { } limit ? Int(limit) + "u" : "(uint)" + count) + ")");
        writer.Line("throw new global::System.ArgumentOutOfRangeException(nameof(index));");
        writer.Close();
        string elementSlice = fixedCount is not null && !slot.IsDynamic
                                  ? "this.source.Slice(" + at + " + index * " + Int(element.Size) + ", " + Int(element.Size) + ")"
                                  : slice(at + " + (long)index * " + Int(element.Size), Int(element.Size));
        writer.Line("return " + (member.Enum is not null ? "(" + elementType + ")" + NumericDecode(element, elementSlice, this.EndianOf(field)) : ScalarDecode(element, elementSlice, this.EndianOf(field))) + ";");
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>Decodes every element of <c>" + DescribeDeclaration(field) + "</c> into <paramref name=\"destination\"/> in one pass.</summary>");
        writer.Line("/// <param name=\"destination\">The span to fill; it must hold at least every element.</param>");
        writer.Line("/// <exception cref=\"global::System.ArgumentException\"><paramref name=\"destination\"/> is too short.</exception>");
        writer.Open("public void " + plan.Unique("Copy" + property + "To") + "(global::System.Span<" + elementType + "> destination)");
        writer.Line("int length = " + count + ";");
        writer.Open("if (destination.Length < length)");
        writer.Line("throw new global::System.ArgumentException(\"The destination is shorter than the array.\", nameof(destination));");
        writer.Close();

        // Only the array's own elements are decoded: the decode helpers fill the whole span they are given, and the
        // destination may be longer. A runtime-sized array slices from the length just computed instead of counting again.
        writer.Line("global::System.Span<" + elementType + "> output = destination.Slice(0, length);");
        string target = member.Enum is not null
                            ? "global::System.Runtime.InteropServices.MemoryMarshal.Cast<" + elementType + ", " + member.Enum.UnderlyingType + ">(output)"
                            : "output";
        writer.Line("global::System.ReadOnlySpan<byte> bytes = " + (fixedCount is null ? slice(at, "(long)length * " + Int(element.Size)) : bytesName) + ";");
        writer.Line(CopyDecode(element, member.Enum is not null ? member.Enum.UnderlyingType : elementType, target, this.EndianOf(field)));
        writer.Close();
    }

    /// <summary>
    ///     Emits an array of fixed-size structs (or unions): its bytes, and one nested view per element at a stride of the
    ///     element's size, so <c>Entries(i).Field</c> reads a member of one element without building any object. A
    ///     runtime-sized array also has its element count.
    /// </summary>
    /// <param name="writer">The output.</param>
    /// <param name="member">The array member.</param>
    /// <param name="slot">Where the array starts.</param>
    /// <param name="plan">The view plan.</param>
    /// <param name="slice">Builds the expression for the bytes at an offset and length.</param>
    /// <param name="fixedCount">The element count when the layout fixes it, or <see langword="null"/>.</param>
    /// <param name="runtimeCount">The count method and expression of a runtime-sized array.</param>
    private void EmitViewStructArray(SourceWriter writer, GeneratedMember member, ViewSlot slot, ViewPlan plan, Func<string, string, string> slice, int? fixedCount, (string Method, string Code, bool IsLiteral)? runtimeCount)
    {
        CompiledField field = member.Field;
        if (member.Composite!.Composite.Symbol.FixedSize is not { } elementSize)
        {
            return;
        }

        int stride = field.FixedElementSize ?? elementSize;
        if (stride <= 0)
        {
            return;
        }

        string at = slot.At;
        string property = member.PropertyName;
        string count = fixedCount is { } fixedValue ? Int(fixedValue) : runtimeCount!.Value.Method + "()";
        string bytesName = plan.Unique(property + "Bytes");
        string name = SourceWriter.Literal(field.Name);
        string typeName = SourceWriter.Literal(field.DisplayTypeSpelling);
        if (runtimeCount is { } computed && plan.WrittenCounts.Add(computed.Method))
        {
            this.EmitCountMethod(writer, member, computed, plan);
        }

        string byteLength = fixedCount is { } n ? Int(n * stride) : "(long)" + count + " * " + Int(stride);
        writer.Line();
        writer.Line("/// <summary>The bytes of <c>" + DescribeDeclaration(field) + "</c> at " + slot.Where + ", every element after the other.</summary>");
        writer.Line("public global::System.ReadOnlySpan<byte> " + bytesName + " => " + slice(at, byteLength) + ";");
        writer.Line();
        writer.Line("/// <summary>A view of one element of <c>" + DescribeDeclaration(field) + "</c>, which is " + Int(stride) + " bytes after the previous one.</summary>");
        writer.Line("/// <param name=\"index\">The element index.</param>");
        writer.Line("/// <returns>The element's view.</returns>");
        writer.Open("public " + ViewName(member.Composite) + " " + property + "(int index)");
        writer.Open("if ((uint)index >= " + (fixedCount is { } limit ? Int(limit) + "u" : "(uint)" + count) + ")");
        writer.Line("throw new global::System.ArgumentOutOfRangeException(nameof(index));");
        writer.Close();

        // Every element view gets the rest of the source, as a single nested view does, so its pointer targets resolve alike.
        // A runtime-sized array may run past the source, so its elements are checked (and a failure names the array).
        if (slot.IsDynamic || fixedCount is null)
        {
            plan.TakeRestName ??= plan.Unique("TakeRest");
            writer.Line("return new(" + plan.TakeRestName + "(" + at + " + (long)index * " + Int(stride) + ", " + Int(stride) + ", " + name + ", " + typeName + "), " + this.ViewOrderPrefix + "this.options" + this.VariablesArgument + ");");
        }
        else
        {
            writer.Line("return new(this.source.Slice(" + at + " + index * " + Int(stride) + "), " + this.ViewOrderPrefix + "this.options" + this.VariablesArgument + ");");
        }

        writer.Close();
    }

    /// <summary>The statement that decodes a whole array of numbers from <c>bytes</c> into a span.</summary>
    /// <param name="codec">The element codec.</param>
    /// <param name="elementType">The C# element type of the destination (an enum's underlying type for an enum array).</param>
    /// <param name="destination">The expression of the destination span.</param>
    /// <param name="endian">The C# expression of the byte order when it is chosen at run time, or <see langword="null"/> for the codec's own.</param>
    /// <returns>The statement.</returns>
    private static string CopyDecode(PrimitiveCodec codec, string elementType, string destination, string? endian = null)
    {
        // An sbyte span cannot be passed where BulkDecode copies through a byte array constructor, so it is viewed as bytes.
        if (codec.Kind == PrimitiveCodecKind.Int8)
        {
            return "bytes.CopyTo(global::System.Runtime.InteropServices.MemoryMarshal.AsBytes(" + destination + "));";
        }

        return BulkDecode(codec, elementType, "bytes", destination, endian);
    }

    /// <summary>Emits the bytes and the string of an encoded text array (<c>utf8</c>, <c>latin1</c>, <c>cp437</c>, <c>utf16le</c>, <c>utf16be</c>), decoded by the reader's own text read.</summary>
    /// <param name="writer">The output.</param>
    /// <param name="member">The member.</param>
    /// <param name="slot">Where the member starts.</param>
    /// <param name="slice">Builds the expression for the bytes at an offset and length.</param>
    /// <param name="fixedCount">The byte count when the layout fixes it, or <see langword="null"/>.</param>
    /// <param name="count">The C# expression of the byte count.</param>
    /// <param name="bytesName">The name of the member that holds the text's bytes.</param>
    private void EmitViewBoundedText(SourceWriter writer, GeneratedMember member, ViewSlot slot, Func<string, string, string> slice, int? fixedCount, string count, string bytesName)
    {
        CompiledField field = member.Field;
        string property = member.PropertyName;
        writer.Line();
        writer.Line("/// <summary>The bytes of <c>" + DescribeDeclaration(field) + "</c> at " + slot.Where + ".</summary>");
        writer.Line("public global::System.ReadOnlySpan<byte> " + bytesName + " => " + slice(slot.At, fixedCount is { } n ? Int(n) : count) + ";");
        writer.Line();
        writer.Line("/// <summary><c>" + DescribeDeclaration(field) + "</c> decoded as the reader does (strictly, unless the options say otherwise; trailing NULs kept unless the options set <c>TrimFixedText</c>); allocates the string.</summary>");
        writer.Open("public string " + property);
        writer.Open("get");
        writer.Line("global::System.ReadOnlySpan<byte> bytes = " + bytesName + ";");
        writer.Line("var cursor = new " + Cursor + "(bytes, this.options, " + SourceWriter.Literal(field.Name) + ");");
        writer.Open("try");
        writer.Line("return cursor.TakeEncodedText(bytes.Length, " + SourceWriter.Literal(field.TypeSpelling) + ", " + SourceWriter.Literal(field.Name) + ", " + SourceWriter.Literal(field.DisplayTypeSpelling) + ");");
        writer.Close();
        writer.Open("catch (global::CStructSharp.Diagnostics.CStructException exception)");
        writer.Line("cursor.Complete(exception);");
        writer.Line("throw;");
        writer.Close();
        writer.Close();
        writer.Close();
    }

    /// <summary>The expression decoding one fixed-width scalar (numeric, identifier, or fixed point) from a span of its size.</summary>
    /// <param name="codec">The codec.</param>
    /// <param name="span">An expression for exactly the value's bytes.</param>
    /// <param name="endian">The C# expression of the byte order when it is chosen at run time, or <see langword="null"/> for the codec's own.</param>
    /// <returns>The decode expression.</returns>
    private static string ScalarDecode(PrimitiveCodec codec, string span, string? endian = null) => codec.Kind switch
    {
        PrimitiveCodecKind.Uuid => CodecClass + ".ReadGuid(" + span + ", true)",
        PrimitiveCodecKind.Guid => CodecClass + ".ReadGuid(" + span + ", false)",
        _ => FixedPoint(codec.Kind) is not null ? FixedPointDecode(codec, span, endian) : NumericDecode(codec, span, endian),
    };
}
