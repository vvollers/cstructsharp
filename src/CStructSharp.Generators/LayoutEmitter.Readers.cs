namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;
using static CStructSharp.Generators.Emit;

/// <summary>
///     The readers: one <c>Read&lt;Type&gt;(ref ReadCursor, variables)</c> per composite that mirrors the runtime
///     reader step by step - the same placement cursor per composite, the same per-field placement, the same order
///     of checks and the same failure texts - plus the public <c>Parse</c> overloads that create the cursor.
/// </summary>
internal sealed partial class LayoutEmitter
{
    private const string Cursor = "global::CStructSharp.Generated.ReadCursor";
    private const string VariablesType = "global::System.Collections.Generic.IReadOnlyDictionary<string, int>?";

    // The variables parameter's type as a cref names it: generic arguments in braces, no nullable marker.
    private const string VariablesCref = "global::System.Collections.Generic.IReadOnlyDictionary{string, int}";

    // The documentation lines every generated read method shares.
    private const string VariablesDoc = "/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>";
    private const string ReadOptionsDoc = "/// <param name=\"options\">The read options; <see langword=\"null\"/> uses the documented defaults.</param>";

    // The conditional groups whose selector the reader being emitted has already evaluated (cleared per composite).
    private readonly HashSet<CompiledConditionalGroup> decidedGroups = new(ReferenceEqualityComparer.Instance);

    // The pointer fields of the struct reader being emitted whose targets are followed after its last field, in
    // declaration order (cleared per composite); the runtime defers the same fields.
    private readonly Dictionary<CompiledField, DeferredPointer> deferredPointers = new(ReferenceEqualityComparer.Instance);
    private readonly List<DeferredPointer> deferredPointerOrder = new();

    /// <summary>Emits reader entry points and per-composite readers using the compiled layout and fixed parser settings.</summary>
    /// <param name="writer">The generated source destination; emitted cursors own operation state, not caller input.</param>
    private void EmitReaders(SourceWriter writer)
    {
        writer.Line();
        writer.Line("private const bool Aligned = " + Bool(this.request.Settings.Aligned) + ";");
        writer.Line("private const bool LittleEndian = " + Bool(this.request.Settings.LittleEndian) + ";");
        writer.Line("private const int PointerSize = " + this.request.Settings.PointerSize.ToString(CultureInfo.InvariantCulture) + ";");
        writer.Line("private const bool HighBitFirst = " + Bool(this.compilation.HighBitFirst) + ";");
        writer.Line("private const global::CStructSharp.BitfieldPacking Packing = global::CStructSharp.BitfieldPacking." + this.request.Settings.BitfieldPacking + ";");
        writer.Line("private const global::CStructSharp.BitfieldAllocation Allocation = global::CStructSharp.BitfieldAllocation." + this.request.Settings.BitfieldAllocation + ";");

        GeneratedComposite? root = this.model.Composites.FirstOrDefault(composite => composite.IsDeclared && composite.LayoutName == this.rootName);
        foreach (GeneratedComposite composite in this.model.Composites)
        {
            if (composite.IsDeclared)
            {
                this.EmitParseOverloads(writer, composite, composite == root);
            }
        }

        foreach (GeneratedComposite composite in this.model.Composites)
        {
            writer.Line();
            this.EmitCompositeReader(writer, composite);
        }

        this.EmitFixedReaders(writer);
        this.EmitPointerReaders(writer);
        writer.Line();
        writer.Line("/// <summary>Groups a flat element array into rows of <paramref name=\"inner\"/> elements (one nesting level of a multidimensional array).</summary>");
        writer.Open("private static T[][] Split<T>(T[] flat, int inner)");
        writer.Line("var rows = new T[inner == 0 ? 0 : flat.Length / inner][];");
        writer.Open("for (int row = 0; row < rows.Length; row++)");
        writer.Line("rows[row] = new T[inner];");
        writer.Line("global::System.Array.Copy(flat, row * inner, rows[row], 0, inner);");
        writer.Close();
        writer.Line("return rows;");
        writer.Close();
    }

    /// <summary>Emits input adapters that own pooled buffers and restore seekable stream origins on any failure.</summary>
    /// <param name="writer">The generated source destination.</param>
    /// <param name="composite">The composite whose typed readers are emitted.</param>
    /// <param name="isRoot">Whether to include the default-root convenience overloads.</param>
    private void EmitParseOverloads(SourceWriter writer, GeneratedComposite composite, bool isRoot)
    {
        string name = composite.Name;
        string method = "Parse" + name;
        string reader = composite.BufferedReaderName;
        writer.Line();
        writer.Line("/// <summary>Reads one <c>" + composite.LayoutName + "</c> from the start of <paramref name=\"source\"/> with the generated reader; the same value, and the same failures, as the runtime's <c>Parse</c>.</summary>");
        writer.Line("/// <param name=\"source\">The bytes; offset 0 is coordinate zero.</param>");
        writer.Line(VariablesDoc);
        writer.Line(ReadOptionsDoc);
        writer.Line("/// <returns>The parsed value.</returns>");
        writer.Open("public static " + name + " " + method + "(global::System.ReadOnlySpan<byte> source, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null)");
        writer.Line("var cursor = new " + Cursor + "(source, options, " + SourceWriter.Literal(composite.LayoutName) + ");");
        writer.Open("try");
        writer.Line("return Read" + name + "(ref cursor, variables, null, null);");
        writer.Close();
        writer.Open("catch (global::CStructSharp.Diagnostics.CStructException exception)");
        writer.Line("cursor.Complete(exception);");
        writer.Line("throw;");
        writer.Close();
        writer.Close();
        writer.Line();
        writer.Line("/// <inheritdoc cref=\"" + method + "(global::System.ReadOnlySpan{byte}, " + VariablesCref + ", global::CStructSharp.ReadOptions)\"/>");
        writer.Line("public static " + name + " " + method + "(byte[] source, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null)");
        writer.Line("    => " + method + "(new global::System.ReadOnlySpan<byte>(source ?? throw new global::System.ArgumentNullException(nameof(source))), variables, options);");
        writer.Line();
        writer.Line("/// <inheritdoc cref=\"" + method + "(global::System.ReadOnlySpan{byte}, " + VariablesCref + ", global::CStructSharp.ReadOptions)\"/>");
        writer.Line("public static " + name + " " + method + "(global::System.ReadOnlyMemory<byte> source, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null)");
        writer.Line("    => " + method + "(source.Span, variables, options);");
        writer.Line();
        writer.Line("/// <summary>Reads one <c>" + composite.LayoutName + "</c> from a sequence of segments: a single segment is read in place, several are copied into a pooled buffer - first the total read budget plus one byte - that grows while the reader needs bytes past it, so the result is the span form's for the whole sequence.</summary>");
        writer.Line("/// <param name=\"source\">The bytes; offset 0 is coordinate zero.</param>");
        writer.Line(VariablesDoc);
        writer.Line(ReadOptionsDoc);
        writer.Line("/// <returns>The parsed value.</returns>");
        writer.Open("public static " + name + " " + method + "(global::System.Buffers.ReadOnlySequence<byte> source, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null)");
        writer.Open("if (source.IsSingleSegment)");
        writer.Line("return " + method + "(source.FirstSpan, variables, options);");
        writer.Close();
        writer.Line("return " + Cursor + ".ReadSequence<" + reader + ", " + name + ">(source, new " + reader + "(variables), options);");
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>Reads one <c>" + composite.LayoutName + "</c> from <paramref name=\"stream\"/>: the stream is buffered - first up to the total read budget plus one byte (or its remaining length) - and read through the span reader, and the buffer grows while the reader needs bytes past it; a seekable stream is left after the value (at its origin on failure).</summary>");
        writer.Line("/// <param name=\"stream\">The stream, read from its current position.</param>");
        writer.Line(VariablesDoc);
        writer.Line(ReadOptionsDoc);
        writer.Line("/// <returns>The parsed value.</returns>");
        writer.Line("public static " + name + " " + method + "(global::System.IO.Stream stream, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null)");
        writer.Line("    => " + Cursor + ".ReadStream<" + reader + ", " + name + ">(stream, new " + reader + "(variables), options);");
        writer.Line();
        writer.Line("/// <summary>Reads one <c>" + composite.LayoutName + "</c> from <paramref name=\"stream\"/> with the bytes read by <see cref=\"global::System.IO.Stream.ReadAsync(global::System.Memory{byte}, global::System.Threading.CancellationToken)\"/>: the same buffering and the same reader as the synchronous form; a seekable stream is left after the value (at its origin on failure), a stream that cannot seek is consumed by what was buffered - more than the total read budget plus one byte only when the value addresses bytes past it. A stored absolute pointer address counts from the origin, as in the span form.</summary>");
        writer.Line("/// <param name=\"stream\">The stream, read from its current position.</param>");
        writer.Line(VariablesDoc);
        writer.Line(ReadOptionsDoc);
        writer.Line("/// <param name=\"cancellationToken\">Ends the read while it waits for bytes or at the next boundary the reader checks; linked with the options' token.</param>");
        writer.Line("/// <returns>The parsed value.</returns>");
        writer.Line("public static global::System.Threading.Tasks.ValueTask<" + name + "> " + method + "Async(global::System.IO.Stream stream, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null, global::System.Threading.CancellationToken cancellationToken = default)");
        writer.Line("    => " + Cursor + ".ReadStreamAsync<" + reader + ", " + name + ">(stream, new " + reader + "(variables), options, cancellationToken);");
        writer.Line();
        writer.Line("/// <summary>The span reader the buffered forms run: a struct carrying the layout variables, so each run is a direct call and a read allocates nothing for it.</summary>");
        writer.Open("private readonly struct " + reader + " : global::CStructSharp.Generated.IBufferedReader<" + name + ">");
        writer.Line("private readonly " + VariablesType + " variables;");
        writer.Line();
        writer.Line("/// <summary>Creates the reader for one operation.</summary>");
        writer.Line(VariablesDoc);
        writer.Line("public " + reader + "(" + VariablesType + " variables) => this.variables = variables;");
        writer.Line();
        writer.Line("/// <inheritdoc/>");
        writer.Line("public " + name + " Read(global::System.ReadOnlySpan<byte> source, global::CStructSharp.ReadOptions? options, out long consumed) => " + method + "Buffered(source, this.variables, options, out consumed);");
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>One run of the buffered forms: the span reader over the buffered bytes, reporting where the value ended.</summary>");
        writer.Open("private static " + name + " " + method + "Buffered(global::System.ReadOnlySpan<byte> source, " + VariablesType + " variables, global::CStructSharp.ReadOptions? options, out long consumed)");
        writer.Line("var cursor = new " + Cursor + "(source, options, " + SourceWriter.Literal(composite.LayoutName) + ");");
        writer.Open("try");
        writer.Line(name + " value = Read" + name + "(ref cursor, variables, null, null);");
        writer.Line("consumed = cursor.Position;");
        writer.Line("return value;");
        writer.Close();
        writer.Open("catch (global::CStructSharp.Diagnostics.CStructException exception)");
        writer.Line("cursor.Complete(exception);");
        writer.Line("throw;");
        writer.Close();
        writer.Close();
        if (isRoot)
        {
            writer.Line();
            writer.Line("/// <summary>Reads the root declaration (<c>" + composite.LayoutName + "</c>); see " + Cref(method + "(global::System.ReadOnlySpan{byte}, " + VariablesCref + ", global::CStructSharp.ReadOptions)") + ".</summary>");
            writer.Line("/// <param name=\"source\">The bytes; offset 0 is coordinate zero.</param>");
            writer.Line(ReadOptionsDoc);
            writer.Line("/// <returns>The parsed value.</returns>");
            writer.Line("public static " + name + " Parse(global::System.ReadOnlySpan<byte> source, global::CStructSharp.ReadOptions? options = null) => " + method + "(source, null, options);");
            writer.Line();
            writer.Line("/// <inheritdoc cref=\"Parse(global::System.ReadOnlySpan{byte}, global::CStructSharp.ReadOptions)\"/>");
            writer.Line("public static " + name + " Parse(byte[] source, global::CStructSharp.ReadOptions? options = null) => " + method + "(source, null, options);");
            writer.Line();
            writer.Line("/// <inheritdoc cref=\"Parse(global::System.ReadOnlySpan{byte}, global::CStructSharp.ReadOptions)\"/>");
            writer.Line("public static " + name + " Parse(global::System.ReadOnlyMemory<byte> source, global::CStructSharp.ReadOptions? options = null) => " + method + "(source.Span, null, options);");
            writer.Line();
            writer.Line("/// <inheritdoc cref=\"" + method + "(global::System.Buffers.ReadOnlySequence{byte}, " + VariablesCref + ", global::CStructSharp.ReadOptions)\"/>");
            writer.Line("public static " + name + " Parse(global::System.Buffers.ReadOnlySequence<byte> source, global::CStructSharp.ReadOptions? options = null) => " + method + "(source, null, options);");
            writer.Line();
            writer.Line("/// <inheritdoc cref=\"" + method + "(global::System.IO.Stream, " + VariablesCref + ", global::CStructSharp.ReadOptions)\"/>");
            writer.Line("public static " + name + " Parse(global::System.IO.Stream stream, global::CStructSharp.ReadOptions? options = null) => " + method + "(stream, null, options);");
            writer.Line();
            writer.Line("/// <inheritdoc cref=\"" + method + "Async(global::System.IO.Stream, " + VariablesCref + ", global::CStructSharp.ReadOptions, global::System.Threading.CancellationToken)\"/>");
            writer.Line("public static global::System.Threading.Tasks.ValueTask<" + name + "> ParseAsync(global::System.IO.Stream stream, global::CStructSharp.ReadOptions? options = null, global::System.Threading.CancellationToken cancellationToken = default) => " + method + "Async(stream, null, options, cancellationToken);");
        }

        this.EmitTryParse(writer, composite, isRoot);
        this.EmitRecords(writer, composite, isRoot);
    }

    /// <summary>
    ///     The non-throwing readers: <c>TryParse&lt;Name&gt;(source, out value)</c> and the form with an
    ///     <c>out CStructException? failure</c>, for every input kind, catching the categorized read, path, and limit
    ///     failures only (cancellation and argument errors pass through, as the runtime's <c>TryReadValue</c>); a
    ///     stream is back at its origin after a failure.
    /// </summary>
    private void EmitTryParse(SourceWriter writer, GeneratedComposite composite, bool isRoot)
    {
        string name = composite.Name;
        string method = "Parse" + name;
        const string Failure = "global::CStructSharp.Diagnostics.CStructException";
        const string MaybeNull = "[global::System.Diagnostics.CodeAnalysis.MaybeNullWhen(false)] ";
        (string Type, string Parameter, string Kind)[] inputs =
        [
            ("global::System.ReadOnlySpan<byte>", "source", "the bytes"),
            ("byte[]", "source", "the array"),
            ("global::System.ReadOnlyMemory<byte>", "source", "the memory"),
            ("global::System.Buffers.ReadOnlySequence<byte>", "source", "the sequence"),
            ("global::System.IO.Stream", "stream", "the stream (left at its origin after a failure)"),
        ];
        foreach ((string type, string parameter, string kind) in inputs)
        {
            writer.Line();
            writer.Line("/// <summary>Reads one <c>" + composite.LayoutName + "</c> from " + kind + " without throwing for a read, path, or limit failure: <see langword=\"false\"/> and the failure instead. Cancellation and argument errors throw as in " + Cref(method + "(" + type.Replace('<', '{').Replace('>', '}') + ", " + VariablesCref + ", global::CStructSharp.ReadOptions)") + ".</summary>");
            writer.Line("/// <param name=\"" + parameter + "\">The input.</param>");
            writer.Line("/// <param name=\"value\">The parsed value, or <see langword=\"null\"/> when the read failed.</param>");
            writer.Line("/// <param name=\"failure\">The failure the throwing form would have raised, or <see langword=\"null\"/>.</param>");
            writer.Line(VariablesDoc);
            writer.Line(ReadOptionsDoc);
            writer.Line("/// <returns>Whether the read succeeded.</returns>");
            writer.Open("public static bool Try" + method + "(" + type + " " + parameter + ", " + MaybeNull + "out " + name + " value, out " + Failure + "? failure, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null)");
            writer.Open("try");
            writer.Line("value = " + method + "(" + parameter + ", variables, options);");
            writer.Line("failure = null;");
            writer.Line("return true;");
            writer.Close();
            writer.Open("catch (" + Failure + " exception)");
            writer.Line("value = null;");
            writer.Line("failure = exception;");
            writer.Line("return false;");
            writer.Close();
            writer.Close();
            writer.Line();
            writer.Line("/// <inheritdoc cref=\"Try" + method + "(" + type.Replace('<', '{').Replace('>', '}') + ", out " + name + ", out " + Failure.Replace('<', '{').Replace('>', '}') + "?, " + VariablesCref + ", global::CStructSharp.ReadOptions)\"/>");
            writer.Line("public static bool Try" + method + "(" + type + " " + parameter + ", " + MaybeNull + "out " + name + " value, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null) => Try" + method + "(" + parameter + ", out value, out _, variables, options);");
            if (isRoot)
            {
                writer.Line();
                writer.Line("/// <inheritdoc cref=\"Try" + method + "(" + type.Replace('<', '{').Replace('>', '}') + ", out " + name + ", out " + Failure.Replace('<', '{').Replace('>', '}') + "?, " + VariablesCref + ", global::CStructSharp.ReadOptions)\"/>");
                writer.Line("public static bool TryParse(" + type + " " + parameter + ", " + MaybeNull + "out " + name + " value, out " + Failure + "? failure, global::CStructSharp.ReadOptions? options = null) => Try" + method + "(" + parameter + ", out value, out failure, null, options);");
                writer.Line();
                writer.Line("/// <inheritdoc cref=\"Try" + method + "(" + type.Replace('<', '{').Replace('>', '}') + ", out " + name + ", out " + Failure.Replace('<', '{').Replace('>', '}') + "?, " + VariablesCref + ", global::CStructSharp.ReadOptions)\"/>");
                writer.Line("public static bool TryParse(" + type + " " + parameter + ", " + MaybeNull + "out " + name + " value, global::CStructSharp.ReadOptions? options = null) => Try" + method + "(" + parameter + ", out value, out _, null, options);");
            }
        }
    }

    /// <summary>
    ///     One composite's reader, entered with the cursor at the composite's first byte. A composite with a fixed reader
    ///     gets a small <c>Read&lt;Type&gt;</c> that tries the fixed reader and otherwise calls a separate
    ///     <c>Read&lt;Type&gt;Members</c> with the member-by-member steps.
    /// </summary>
    /// <remarks>
    ///     The split keeps the fixed path cheap: the JIT inlines a hot <c>Read&lt;Type&gt;</c> into its caller, and a
    ///     member-by-member body in the same method would be inlined with it and use up the inlining budget, so the
    ///     span and codec helpers of the fixed reader would stay calls. For the same reason the method that holds the
    ///     member-by-member steps is never inlined (<see cref="Emit.NoInlining"/>).
    /// </remarks>
    /// <param name="writer">The generated source destination.</param>
    /// <param name="composite">The composite whose reader is emitted.</param>
    private void EmitCompositeReader(SourceWriter writer, GeneratedComposite composite)
    {
        string name = composite.Name;
        string parameters = "(ref " + Cursor + " cursor, " + VariablesType + " variables, string? member, string? memberType)";
        FixedPlan? plan = this.FixedPlanOf(composite, 0);
        writer.Line("/// <summary>Reads one <c>" + composite.LayoutName + "</c> at the cursor's position.</summary>");
        if (plan is null)
        {
            writer.Line(NoInlining);
        }

        writer.Open("private static " + name + " Read" + name + parameters);
        if (plan is not null)
        {
            this.EmitFixedReaderShortcut(writer, composite, plan);
            writer.Line("return Read" + name + "Members(ref cursor, variables, member, memberType);");
            writer.Close();
            writer.Line();
            writer.Line("/// <summary>Reads one <c>" + composite.LayoutName + "</c> member by member at the cursor's position, when " + Cref("Read" + name) + " cannot use the fixed reader.</summary>");
            writer.Line(NoInlining);
            writer.Open("private static " + name + " Read" + name + "Members" + parameters);
        }

        writer.Line("cursor.EnterComposite(member ?? " + SourceWriter.Literal(composite.LayoutName) + ", memberType);");
        writer.Line("var value = new " + name + "();");
        var scope = new ReaderScope(this, composite);
        this.decidedGroups.Clear();
        this.deferredPointers.Clear();
        this.deferredPointerOrder.Clear();
        if (composite.IsUnion)
        {
            this.EmitUnionBody(writer, composite, scope);
        }
        else
        {
            writer.Line("var placement = global::CStructSharp.Generated.CompositeCursor.Start(cursor.Position, Aligned, Packing, Allocation);");
            this.PrepareDeferredPointers(writer, composite.Composite);
            this.EmitFields(writer, composite.Composite, scope, "value", "placement");
            this.EmitDeferredPointerFollows(writer, scope);
            writer.Line("cursor.Seek(placement.Finish(" + composite.Composite.Symbol.Alignment.ToString(CultureInfo.InvariantCulture) + "), member, memberType);");
        }

        writer.Line("cursor.ExitComposite();");
        writer.Line("return value;");
        writer.Close();
    }

    private void EmitUnionBody(SourceWriter writer, GeneratedComposite composite, ReaderScope scope)
    {
        CompiledCompositeType union = composite.Composite;
        int? fixedSize = union.Symbol.FixedSize;
        writer.Line("int unionStart = cursor.Position;");
        if (fixedSize is { } size)
        {
            writer.Line("value.RawStorage = cursor.Take(" + size.ToString(CultureInfo.InvariantCulture) + ", member, memberType).ToArray();");
        }
        else
        {
            // A union with a runtime-sized member: its extent is the widest member's, measured by reading them.
            writer.Line("int unionEnd = unionStart;");
        }

        writer.Line("cursor.EnterUnion();");
        EmitConditionalSlots(writer, union, "union");
        foreach (CompiledField field in union.Fields)
        {
            writer.Line("cursor.Position = unionStart;");
            this.EmitField(writer, field, union, scope, "value", inUnion: true, "union");
            if (fixedSize is null)
            {
                writer.Line("unionEnd = global::System.Math.Max(unionEnd, cursor.Position);");
            }
        }

        writer.Line("cursor.ExitUnion();");
        if (fixedSize is null)
        {
            writer.Line("cursor.Position = unionStart;");
            writer.Line("value.RawStorage = cursor.Peek(unionEnd - unionStart, member ?? " + SourceWriter.Literal(composite.LayoutName) + ", memberType).ToArray();");
            writer.Line("cursor.Position = unionEnd;");
        }
        else
        {
            writer.Line("cursor.Position = unionStart + " + fixedSize.Value.ToString(CultureInfo.InvariantCulture) + ";");
        }
    }

    /// <summary>The fields of a struct body in order; a promoted anonymous composite's fields are read in place into the same value.</summary>
    private void EmitFields(SourceWriter writer, CompiledCompositeType composite, ReaderScope scope, string target, string placement)
    {
        EmitConditionalSlots(writer, composite, placement);
        foreach (CompiledField field in composite.Fields)
        {
            this.EmitField(writer, field, composite, scope, target, inUnion: false, placement);
        }
    }

    /// <summary>
    ///     One local per conditional group of the composite, holding the selected arm once the group is reached
    ///     (<c>int.MinValue</c> until then): the runtime decides each group once per instance, at its first field.
    /// </summary>
    private static void EmitConditionalSlots(SourceWriter writer, CompiledCompositeType composite, string prefix)
    {
        for (int slot = 0; slot < composite.ConditionalGroupCount; slot++)
        {
            writer.Line("int " + ArmSlot(prefix, slot) + " = int.MinValue;");
        }
    }

    private static string ArmSlot(string prefix, int slot) => prefix + "Arm" + Int(slot);

    /// <summary>
    ///     Emits the read of one declared field: the selector of each conditional group it belongs to (once per group), the
    ///     test for its arms, and then its body.
    /// </summary>
    /// <param name="writer">The output.</param>
    /// <param name="field">The field.</param>
    /// <param name="composite">The struct or union that declares the field.</param>
    /// <param name="scope">The expression scope for counts and conditions.</param>
    /// <param name="target">The expression holding the value being built.</param>
    /// <param name="inUnion">Whether the field is a union member, placed at the union's start.</param>
    /// <param name="placement">The composite cursor's local.</param>
    private void EmitField(SourceWriter writer, CompiledField field, CompiledCompositeType composite, ReaderScope scope, string target, bool inUnion, string placement)
    {
        // A promoted anonymous composite has no name of its own: a failure inside it is attributed to the enclosing
        // named member (the reader's parameters), as the runtime's per-field context does.
        bool promoted = field.IsUnnamed && field.IsInlineComposite;
        string member = promoted ? "member" : SourceWriter.Literal(field.Name.Length == 0 && field.BitSize == 0 ? "_" : field.Name);
        string memberType = promoted ? "memberType" : SourceWriter.Literal(field.DisplayTypeSpelling);
        writer.Line("// " + DescribeDeclaration(field));

        // A conditional field: each enclosing group (outermost first) is decided when its first field is reached -
        // the selector is evaluated once per instance, and never while an outer arm is inactive - and the field is
        // read only when every group selected its arm. Every later field of a group is reached only after that first
        // field's block ran, so it tests the decision without repeating it.
        foreach (CompiledConditionalBranch branch in field.ConditionalBranches)
        {
            string slot = ArmSlot(placement, branch.Slot);
            if (this.decidedGroups.Add(branch.Group))
            {
                this.EmitSelector(writer, branch.Group, scope, slot);
            }

            writer.Open("if (" + slot + " == " + Int(branch.Arm) + ")");
        }

        this.EmitFieldBody(writer, field, scope, target, inUnion, placement, promoted, member, memberType, openBlock: field.ConditionalBranches.Length == 0, this.StartsAtCursor(composite, field));
        for (int index = 0; index < field.ConditionalBranches.Length; index++)
        {
            writer.Close();
        }
    }

    /// <summary>
    ///     Evaluates a group's selector into its arm slot: an <c>if</c> selects arm 1 or 0, a <c>switch</c> compares the
    ///     value with each case label in the 128-bit domain (default is arm -1).
    /// </summary>
    /// <param name="writer">The output.</param>
    /// <param name="group">The conditional group whose selector is evaluated.</param>
    /// <param name="scope">The expression scope for the selector.</param>
    /// <param name="slot">The local that receives the selected arm.</param>
    private void EmitSelector(SourceWriter writer, CompiledConditionalGroup group, ReaderScope scope, string slot)
    {
        string code = scope.Expressions.Emit(group.Selector);

        // A selector failure is the composite's, not any field's: the enclosing member is what the runtime notes.
        writer.Open("try");
        if (group.CaseArms is { } cases)
        {
            // C# has no constant patterns for Int128, so the case table is a chain of comparisons in arm order.
            var arms = new System.Text.StringBuilder();
            foreach (KeyValuePair<System.Int128, int> arm in cases.OrderBy(pair => pair.Value))
            {
                arms.Append("caseSelector == ").Append(ExpressionEmitter.DomainConstant((System.Numerics.BigInteger)arm.Key)).Append(" ? ").Append(Int(arm.Value)).Append(" : ");
            }

            writer.Line("global::System.Int128 caseSelector = " + code + ";");
            writer.Line(slot + " = " + arms.Append("-1").ToString() + ";");
        }
        else
        {
            writer.Line(slot + " = (" + code + ") != 0 ? 1 : 0;");
        }

        writer.Close();
        writer.Open("catch (global::System.Exception expressionFailure)");
        writer.Line("throw cursor.FailExpression(expressionFailure, \"conditional selector\", member, memberType);");
        writer.Close();
    }

    /// <summary>
    ///     Emits the read of one field: its placement (a separator, a bitfield's storage unit, or an aligned field with
    ///     its offset assertion) and then its value, element by element for an array.
    /// </summary>
    /// <param name="writer">The output.</param>
    /// <param name="field">The field.</param>
    /// <param name="scope">The expression scope for counts and conditions.</param>
    /// <param name="target">The expression holding the value being built.</param>
    /// <param name="inUnion">Whether the field is a union member, placed at the union's start.</param>
    /// <param name="placement">The composite cursor's local.</param>
    /// <param name="promoted">Whether the field is an anonymous member promoted into the parent's value.</param>
    /// <param name="member">The member-name expression for failures.</param>
    /// <param name="memberType">The member-type expression for failures.</param>
    /// <param name="openBlock">Whether to wrap the emitted code in its own block.</param>
    /// <param name="startsAtCursor">Whether the field starts where the cursor is, so it needs no placement step (see <see cref="StartsAtCursor"/>).</param>
    private void EmitFieldBody(SourceWriter writer, CompiledField field, ReaderScope scope, string target, bool inUnion, string placement, bool promoted, string member, string memberType, bool openBlock, bool startsAtCursor)
    {
        if (openBlock)
        {
            writer.Open(string.Empty);
        }

        if (field.IsZeroWidthBitfield)
        {
            if (!inUnion)
            {
                // The runtime moves past a separator before entering any field: a failure there names no member.
                writer.Line("cursor.Seek(" + placement + ".AdvanceToSeparator(" + Int(field.BitStorageSize ?? 1) + ", " + Int(field.Alignment) + ", " + Int(field.BitRunBits) + "), null, null);");
            }

            CloseBlock(writer, openBlock);
            return;
        }

        if (field.BitSize > 0)
        {
            this.EmitBitfield(writer, field, scope, target, inUnion, member, memberType, placement);
            CloseBlock(writer, openBlock);
            return;
        }

        if (!inUnion)
        {
            if (!startsAtCursor)
            {
                writer.Line("cursor.Seek(" + placement + ".AdvanceToField(" + Int(field.Alignment) + "), " + member + ", " + memberType + ");");
            }

            EmitOffsetAssertion(writer, field, placement, member, memberType);
        }

        CompiledCompositeType? inline = this.InlineComposite(field);
        if (field.IsUnnamed && inline is not null)
        {
            // Promoted: read the anonymous composite's members straight into this value. Its members belong to this
            // struct, so it enters no nesting level of its own, as in the runtime.
            if (inline.IsUnion)
            {
                this.EmitPromotedUnion(writer, inline, scope, target, member, memberType, placement);
            }
            else
            {
                string inner = placement + "N";
                writer.Line("var " + inner + " = global::CStructSharp.Generated.CompositeCursor.Start(cursor.Position, Aligned, Packing, Allocation);");
                this.EmitFields(writer, inline, scope, target, inner);
                writer.Line("cursor.Seek(" + inner + ".Finish(" + Int(inline.Symbol.Alignment) + "), " + member + ", " + memberType + ");");
            }
        }
        else if (field.IsUnnamed)
        {
            // Padding (`uint16 _;`): read with the field's own rules and discarded, as the runtime reads it without a slot.
            this.EmitDiscardedValue(writer, field, scope, inUnion, member, memberType);
        }
        else
        {
            GeneratedMember generated = scope.Member(field) ?? throw new InvalidOperationException("No generated member for field " + field.Name);
            this.EmitValue(writer, field, generated, scope, target, inUnion, member, memberType);
            if (generated.IsConditional)
            {
                writer.Line(target + "." + generated.HasFlagName + " = true;");
            }
        }

        if (!inUnion)
        {
            writer.Line(placement + ".CompleteField(cursor.Position);");
        }

        CloseBlock(writer, openBlock);
    }

    /// <summary>
    ///     Whether a struct field (not a bitfield or a union member) starts exactly where the cursor is when it is
    ///     reached, so the placement step before it - <c>AdvanceToField</c> and the <c>Seek</c> to its result - would
    ///     change nothing and is left out. That holds when the field needs no alignment (a packed layout, or an
    ///     alignment of 1) and its struct has no bitfield or separator: then no bitfield run is open for
    ///     <c>AdvanceToField</c> to close, and the placement position is the one the previous field's
    ///     <c>CompleteField</c> recorded from the cursor (or the struct's start), which nothing moves in between.
    /// </summary>
    /// <param name="composite">The struct whose fields are being emitted with one placement cursor.</param>
    /// <param name="field">The field.</param>
    /// <returns><see langword="true"/> when the placement step can be left out.</returns>
    private bool StartsAtCursor(CompiledCompositeType composite, CompiledField field)
    {
        if (this.request.Settings.Aligned && field.Alignment != 1)
        {
            return false;
        }

        foreach (CompiledField other in composite.Fields)
        {
            if (other.BitSize > 0 || other.IsZeroWidthBitfield)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Closes the block <see cref="EmitFieldBody"/> opened for a field, when it opened one.</summary>
    /// <param name="writer">The output.</param>
    /// <param name="openBlock">Whether a block was opened.</param>
    private static void CloseBlock(SourceWriter writer, bool openBlock)
    {
        if (openBlock)
        {
            writer.Close();
        }
    }

    /// <summary>
    ///     Emits the read of an anonymous union inside a struct: every member is decoded from the union's start into the
    ///     parent's value, pointers inside are not followed, and the cursor ends past the union's extent. The union's
    ///     members belong to the parent, so it enters no nesting level of its own.
    /// </summary>
    /// <param name="writer">The source being emitted.</param>
    /// <param name="union">The anonymous union.</param>
    /// <param name="scope">The generated members of the enclosing reader.</param>
    /// <param name="target">The expression naming the parent's value.</param>
    /// <param name="member">The diagnostic member expression of the enclosing field.</param>
    /// <param name="memberType">The diagnostic type expression of the enclosing field.</param>
    /// <param name="placement">The parent's placement cursor variable.</param>
    private void EmitPromotedUnion(SourceWriter writer, CompiledCompositeType union, ReaderScope scope, string target, string member, string memberType, string placement)
    {
        // The runtime reads the anonymous union as a union value and splices its members into the parent.
        writer.Line("int unionStart = cursor.Position;");
        int? size = union.Symbol.FixedSize;
        if (size is { } fixedSize)
        {
            writer.Line("_ = cursor.Take(" + Int(fixedSize) + ", " + member + ", " + memberType + ");");
        }
        else
        {
            writer.Line("int unionEnd = unionStart;");
        }

        writer.Line("cursor.EnterUnion();");
        EmitConditionalSlots(writer, union, placement + "U");
        foreach (CompiledField field in union.Fields)
        {
            writer.Line("cursor.Position = unionStart;");
            this.EmitField(writer, field, union, scope, target, inUnion: true, placement + "U");
            if (size is null)
            {
                writer.Line("unionEnd = global::System.Math.Max(unionEnd, cursor.Position);");
            }
        }

        writer.Line("cursor.ExitUnion();");
        writer.Line(size is { } end ? "cursor.Position = unionStart + " + Int(end) + ";" : "cursor.Position = unionEnd;");
    }

    /// <summary>
    ///     Emits the read of one bitfield: its storage unit (placed by the composite cursor, or at the union's start),
    ///     the unit's bytes, and the field's bits, with the runtime's failure when the bits overrun the unit.
    /// </summary>
    /// <param name="writer">The output.</param>
    /// <param name="field">The bitfield.</param>
    /// <param name="scope">The expression scope the field's value is published to.</param>
    /// <param name="target">The expression holding the value being built.</param>
    /// <param name="inUnion">Whether the field is a union member.</param>
    /// <param name="member">The member-name expression for failures.</param>
    /// <param name="memberType">The member-type expression for failures.</param>
    /// <param name="placement">The composite cursor's local.</param>
    private void EmitBitfield(SourceWriter writer, CompiledField field, ReaderScope scope, string target, bool inUnion, string member, string memberType, string placement)
    {
        int declaredSize = field.BitStorageSize ?? field.Codec.Size;
        bool littleEndian = field.BitStorageIsLittleEndian ?? true;
        if (inUnion)
        {
            // A union member bitfield always opens its own unit at the union's start.
            writer.Line("var slot = new global::CStructSharp.Generated.BitfieldSlot(cursor.Position, " + Int(declaredSize) + ", 0);");
        }
        else
        {
            writer.Line("var slot = " + placement + ".AdvanceToBitfield(" + Int(declaredSize) + ", " + Int(field.Alignment) + ", " + Int(field.BitSize) + ", " + Int(field.BitRunBits) + ", " + Bool(littleEndian) + ", " + member + ");");
            writer.Line("cursor.Seek(slot.UnitStart, " + member + ", " + memberType + ");");
        }

        writer.Line("ulong unit = " + CodecClass + ".ReadUnsigned(cursor.Take(slot.UnitSize, " + member + ", " + memberType + "), " + Bool(littleEndian) + ");");
        writer.Open("if (slot.BitOffset + " + Int(field.BitSize) + " > slot.UnitSize * 8)");
        writer.Line("throw cursor.Fail(" + SourceWriter.Literal(LayoutFailures.BitfieldExceedsUnit(field.Name)) + ", " + member + ", " + memberType + ");");
        writer.Close();
        writer.Line("ulong bits = " + CodecClass + ".ExtractBits(unit, " + CodecClass + ".BitfieldShift(slot.BitOffset, " + Int(field.BitSize) + ", slot.UnitSize * 8, HighBitFirst), " + Int(field.BitSize) + ");");
        if (!inUnion)
        {
            // While later bitfields may share the unit, the runtime keeps its position at the unit's start (its failures report that offset).
            writer.Open("if ((slot.BitOffset + " + Int(field.BitSize) + ") / 8 + 1 <= slot.UnitSize)");
            writer.Line("cursor.Position = (int)slot.UnitStart;");
            writer.Close();
        }

        if (field.Name.Length > 0)
        {
            GeneratedMember generated = scope.Member(field) ?? throw new InvalidOperationException("No generated member for bitfield " + field.Name);
            writer.Line(target + "." + generated.PropertyName + " = (" + generated.TypeName + ")" + (generated.Enum is not null ? "(" + generated.Enum.UnderlyingType + ")" : string.Empty) + "bits;");
            if (generated.IsConditional)
            {
                writer.Line(target + "." + generated.HasFlagName + " = true;");
            }

            if (!inUnion)
            {
                scope.Publish(generated, target + "." + generated.PropertyName);
            }
        }
    }

    /// <summary>
    ///     Emits the check of a field's <c>@N</c> offset assertion when the layout could not check it (the field's offset
    ///     depends on the data). The composite cursor measures from the composite's start and returns the runtime's text.
    /// </summary>
    /// <param name="writer">The output.</param>
    /// <param name="field">The field, already placed at <c>cursor.Position</c>.</param>
    /// <param name="placement">The composite cursor's local.</param>
    /// <param name="member">The member-name expression for the failure.</param>
    /// <param name="memberType">The member-type expression for the failure.</param>
    private static void EmitOffsetAssertion(SourceWriter writer, CompiledField field, string placement, string member, string memberType)
    {
        if (field.AssertedOffset is not int asserted || field.FixedOffset.HasValue)
        {
            return;
        }

        // A block, so each check's pattern variable has its own scope.
        writer.Open(string.Empty);
        writer.Open("if (" + placement + ".CheckOffset(cursor.Position, " + Int(asserted) + ", " + SourceWriter.Literal(field.Name) + ") is { } offsetFailure)");
        writer.Line("throw cursor.FailLayout(offsetFailure, " + member + ", " + memberType + ");");
        writer.Close();
        writer.Close();
    }

    /// <summary>
    ///     Evaluates an element count (an array length or a pointer's <c>@count</c>) into the <c>int</c> local
    ///     <c>count</c> with the runtime's checks: the expression's own failures, a negative count, and the
    ///     <c>MaxArrayElements</c> limit. The expression is evaluated in the 128-bit domain, so a count read from a
    ///     <c>uint64</c> field fails the limit with its exact value instead of wrapping.
    /// </summary>
    /// <param name="writer">The output.</param>
    /// <param name="fieldName">The field, named by the negative-length failure.</param>
    /// <param name="expression">The count expression.</param>
    /// <param name="scope">The expression scope.</param>
    /// <param name="context">What is evaluated, for the expression failure.</param>
    /// <param name="member">The member-name expression for failures.</param>
    /// <param name="memberType">The member-type expression for failures.</param>
    /// <param name="validatedCountIsReturned">
    ///     Whether the cursor's <c>RequireArrayLength</c> returns the validated count (the read cursor does; the write
    ///     cursor only checks it).
    /// </param>
    private void EmitCount(SourceWriter writer, string fieldName, Expr expression, ReaderScope scope, string context, string member, string memberType, bool validatedCountIsReturned)
    {
        string code = scope.Expressions.Emit(expression);
        if (ExpressionEmitter.IsInt32Literal(expression))
        {
            // A constant (a fixed count, a folded sizeof) cannot fail, and layout compilation already rejected a
            // negative one.
            writer.Line("count = " + code + ";");
            writer.Line("cursor.RequireArrayLength(count, " + member + ", " + memberType + ");");
            return;
        }

        writer.Line("global::System.Int128 countValue;");
        writer.Open("try");
        writer.Line("countValue = " + code + ";");
        writer.Close();
        writer.Open("catch (global::System.Exception expressionFailure)");
        writer.Line("throw cursor.FailExpression(expressionFailure, " + SourceWriter.Literal(context) + ", " + member + ", " + memberType + ");");
        writer.Close();
        writer.Open("if (countValue < 0)");
        writer.Line("throw cursor.Fail(" + SourceWriter.Literal(LayoutFailures.NegativeArrayLength(fieldName)) + ", " + member + ", " + memberType + ");");
        writer.Close();
        if (validatedCountIsReturned)
        {
            writer.Line("count = cursor.RequireArrayLength(countValue, " + member + ", " + memberType + ");");
        }
        else
        {
            writer.Line("cursor.RequireArrayLength(countValue, " + member + ", " + memberType + ");");
            writer.Line("count = (int)countValue;");
        }
    }

    /// <summary>A padding field's value: the same read as a named field's, into a discard.</summary>
    private void EmitDiscardedValue(SourceWriter writer, CompiledField field, ReaderScope scope, bool inUnion, string member, string memberType)
    {
        GeneratedMember padding = this.model.Describe(field, "_");
        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            writer.Line("_ = " + this.ScalarRead(field, padding, member, memberType) + ";");
            return;
        }

        writer.Open(string.Empty);
        this.EmitArray(writer, field, padding, scope, "_", inUnion, member, memberType);
        writer.Close();
    }

    /// <summary>Emits the read of a named field into its property and publishes it to later expressions; a deferred pointer reads only its address.</summary>
    private void EmitValue(SourceWriter writer, CompiledField field, GeneratedMember generated, ReaderScope scope, string target, bool inUnion, string member, string memberType)
    {
        string property = target + "." + generated.PropertyName;
        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            // A deferred pointer takes only its address here; its target is followed after the struct's last field.
            string? deferred = inUnion ? null : this.DeferredAddressRead(field, generated, member, memberType);
            writer.Line(property + " = " + (deferred ?? this.ScalarRead(field, generated, member, memberType)) + ";");
            if (deferred is not null)
            {
                this.RecordDeferredScalar(writer, field, property, member, memberType);
            }
        }
        else
        {
            this.EmitArray(writer, field, generated, scope, property, inUnion, member, memberType);
        }

        if (!inUnion)
        {
            // Later expressions may name this member; a union's members are not visible outside it.
            scope.Publish(generated, property);
        }
    }

    /// <summary>The expression that reads one scalar of the field's type at the cursor.</summary>
    private string ScalarRead(CompiledField field, GeneratedMember generated, string member, string memberType)
    {
        if (field.PointerDepth > 0)
        {
            this.RequirePointerReader(field);

            // Only a union member reads a counted pointer in place, and a union never follows it, so its count is unused.
            return "Read" + PointerReaderName(field) + "(ref cursor, variables, " + (field.HasCountedTarget ? "0, " : string.Empty) + member + ", " + memberType + ")";
        }

        if (generated.Composite is not null)
        {
            return "Read" + generated.Composite.Name + "(ref cursor, variables, " + member + ", " + memberType + ")";
        }

        if (generated.Enum is not null)
        {
            return "(" + generated.Enum.Name + ")" + NumericRead(field.Codec, member, memberType);
        }

        return this.PrimitiveRead(field, member, memberType);
    }

    private string PrimitiveRead(CompiledField field, string member, string memberType)
        => this.PrimitiveRead(field.Codec, field.Type.Symbol.Name, member, memberType);

    /// <summary>The expression that reads one primitive at the cursor: text, an identifier, a LEB128 or fixed-point number, a custom codec's value, or a fixed-width numeric.</summary>
    /// <param name="codec">The codec.</param>
    /// <param name="typeName">The layout type name, which selects a custom codec's instance.</param>
    /// <param name="member">The member-name expression for failures.</param>
    /// <param name="memberType">The member-type expression for failures.</param>
    /// <returns>The read expression.</returns>
    private string PrimitiveRead(PrimitiveCodec codec, string typeName, string member, string memberType)
    {
        switch (codec.Kind)
        {
        case PrimitiveCodecKind.TerminatedAscii:
        case PrimitiveCodecKind.TerminatedUtf8:
        case PrimitiveCodecKind.TerminatedUtf16:
            return "cursor.TakeTerminatedString(" + TerminatedEncoding(codec) + ", " + CharLiteral(codec.Terminator) + ", " + member + ", " + memberType + ")";
        case PrimitiveCodecKind.Uuid:
            return "cursor.TakeGuid(true, " + member + ", " + memberType + ")";
        case PrimitiveCodecKind.Guid:
            return "cursor.TakeGuid(false, " + member + ", " + memberType + ")";
        case PrimitiveCodecKind.ULeb128_32:
            return "(uint)cursor.TakeLeb128(32, false, " + member + ", " + memberType + ")";
        case PrimitiveCodecKind.ULeb128_64:
            return "cursor.TakeLeb128(64, false, " + member + ", " + memberType + ")";
        case PrimitiveCodecKind.SLeb128_32:
            return "unchecked((int)cursor.TakeLeb128(32, true, " + member + ", " + memberType + "))";
        case PrimitiveCodecKind.SLeb128_64:
            return "unchecked((long)cursor.TakeLeb128(64, true, " + member + ", " + memberType + "))";
        case PrimitiveCodecKind.Fixed16_16:
        case PrimitiveCodecKind.UFixed16_16:
        case PrimitiveCodecKind.Fixed2_30:
        case PrimitiveCodecKind.UFixed8_8:
            return FixedPointDecode(codec, "cursor.Take(" + Int(codec.Size) + ", " + member + ", " + memberType + ")");
        case PrimitiveCodecKind.Custom:
            return "cursor.TakeCustom(CodecInstances.Value[" + Int(this.CodecIndex(typeName)) + "], " + member + ", " + memberType + ")";
        default:
            return NumericRead(codec, member, memberType);
        }
    }

    /// <summary>The expression that decodes one fixed-width numeric value (including the single-byte character units and wide integers).</summary>
    private static string NumericRead(PrimitiveCodec codec, string member, string memberType)
        => NumericDecode(codec, "cursor.Take(" + Int(codec.Size) + ", " + member + ", " + memberType + ")");

    private static string TerminatedEncoding(PrimitiveCodec codec)
    {
        const string T = "global::CStructSharp.Generated.TerminatedTextEncoding.";
        return codec.Kind switch
        {
            PrimitiveCodecKind.TerminatedAscii => T + "Ascii",
            PrimitiveCodecKind.TerminatedUtf8 => T + "Utf8",
            _ => codec.LittleEndian ? T + "Utf16LittleEndian" : T + "Utf16BigEndian",
        };
    }

    /// <summary>A character as a C# literal; the terminators <c>\0</c> and <c>\n</c> are escaped.</summary>
    /// <param name="character">A terminator character.</param>
    /// <returns>The literal.</returns>
    private static string CharLiteral(char character) => character == '\0' ? "'\\0'" : character == '\n' ? "'\\n'" : "'" + character + "'";

    /// <summary>The composite a field declares in place (an anonymous or tagged inline struct or union), or <see langword="null"/>.</summary>
    /// <param name="field">The field.</param>
    /// <returns>The inline composite.</returns>
    private CompiledCompositeType? InlineComposite(CompiledField field)
    {
        if (field.Declaration is not Struct inline)
        {
            return null;
        }

        // An anonymous inline composite is keyed by its declaration; a tagged one declared in place
        // (`union tag { ... };` as a member) is reached through the field's type symbol.
        return this.compilation.CompiledModel.Composites.TryGetValue(inline, out CompiledTypeSymbol? symbol)
                   ? symbol.Definition as CompiledCompositeType
                   : field.Type.Symbol.Definition as CompiledCompositeType;
    }

    /// <summary>The name suffix of the readers for one pointer shape: target type, depth, and whether the target is counted.</summary>
    private static string PointerReaderName(CompiledField field) => "Pointer_" + Sanitize(field.TypeSpelling) + "_" + Int(field.PointerDepth) + (field.HasCountedTarget ? "_Counted" : string.Empty);

    private static string Sanitize(string name)
    {
        var builder = new System.Text.StringBuilder(name.Length);
        foreach (char character in name)
        {
            builder.Append(char.IsLetterOrDigit(character) ? character : character == '<' ? "Le" : character == '>' ? "Be" : "_");
        }

        return builder.ToString();
    }
}
