namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Syntax;

/// <summary>
///     The readers: one <c>Read&lt;Type&gt;(ref ReadCursor, variables)</c> per composite that mirrors the runtime
///     reader step by step - the same placement cursor per composite, the same per-field placement, the same order
///     of checks and the same failure texts - plus the public <c>Parse</c> overloads that create the cursor.
/// </summary>
internal sealed partial class LayoutEmitter
{
    private const string Cursor = "global::CStructSharp.Generated.ReadCursor";
    private const string CodecClass = "global::CStructSharp.Generated.Codec";
    private const string VariablesType = "global::System.Collections.Generic.IReadOnlyDictionary<string, int>?";

    // The conditional groups whose selector the reader being emitted has already evaluated (cleared per composite).
    private readonly HashSet<ConditionalGroup> decidedGroups = new(ReferenceEqualityComparer.Instance);

    /// <summary>Emits best-effort cursor restoration without replacing the original acquisition or decode failure.</summary>
    /// <param name="writer">The generated source destination, immediately after a stream operation's try block.</param>
    private static void EmitStreamFailureRestoration(SourceWriter writer)
    {
        writer.Open("catch");
        writer.Open("try");
        writer.Open("if (stream.CanSeek)");
        writer.Line("stream.Position = start;");
        writer.Close();
        writer.Close();
        writer.Open("catch");
        writer.Line("// Preserve the original failure if the underlying stream also refuses restoration.");
        writer.Close();
        writer.Line("throw;");
        writer.Close();
    }

    /// <summary>Emits reader entry points and per-composite readers using the compiled layout and fixed parser settings.</summary>
    /// <param name="writer">The generated source destination; emitted cursors own operation state, not caller input.</param>
    private void EmitReaders(SourceWriter writer)
    {
        writer.Line();
        writer.Line("private const bool Aligned = " + Bool(this.request.Aligned) + ";");
        writer.Line("private const bool LittleEndian = " + Bool(this.request.LittleEndian) + ";");
        writer.Line("private const int PointerSize = " + this.request.PointerSize.ToString(CultureInfo.InvariantCulture) + ";");
        writer.Line("private const bool HighBitFirst = " + Bool(this.compilation.HighBitFirst) + ";");
        writer.Line("private const global::CStructSharp.BitfieldPacking Packing = global::CStructSharp.BitfieldPacking." + this.request.BitfieldPacking + ";");
        writer.Line("private const global::CStructSharp.BitfieldAllocation Allocation = global::CStructSharp.BitfieldAllocation." + this.request.BitfieldAllocation + ";");

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

    private static string Bool(bool value) => value ? "true" : "false";

    /// <summary>Emits input adapters that own pooled buffers and restore seekable stream origins on any failure.</summary>
    /// <param name="writer">The generated source destination.</param>
    /// <param name="composite">The composite whose typed readers are emitted.</param>
    /// <param name="isRoot">Whether to include the default-root convenience overloads.</param>
    private void EmitParseOverloads(SourceWriter writer, GeneratedComposite composite, bool isRoot)
    {
        string name = composite.Name;
        string method = "Parse" + name;
        writer.Line();
        writer.Line("/// <summary>Reads one <c>" + composite.LayoutName + "</c> from the start of <paramref name=\"source\"/> with the generated reader; the same value, and the same failures, as the runtime's <c>Parse</c>.</summary>");
        writer.Line("/// <param name=\"source\">The bytes; offset 0 is coordinate zero.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The read options; <see langword=\"null\"/> uses the documented defaults.</param>");
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
        writer.Line("/// <inheritdoc cref=\"" + method + "(global::System.ReadOnlySpan{byte}, " + VariablesType.TrimEnd('?').Replace("<string, int>", "{string, int}") + ", global::CStructSharp.ReadOptions)\"/>");
        writer.Line("public static " + name + " " + method + "(byte[] source, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null)");
        writer.Line("    => " + method + "(new global::System.ReadOnlySpan<byte>(source ?? throw new global::System.ArgumentNullException(nameof(source))), variables, options);");
        writer.Line();
        writer.Line("/// <inheritdoc cref=\"" + method + "(global::System.ReadOnlySpan{byte}, " + VariablesType.TrimEnd('?').Replace("<string, int>", "{string, int}") + ", global::CStructSharp.ReadOptions)\"/>");
        writer.Line("public static " + name + " " + method + "(global::System.ReadOnlyMemory<byte> source, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null)");
        writer.Line("    => " + method + "(source.Span, variables, options);");
        writer.Line();
        writer.Line("/// <summary>Reads one <c>" + composite.LayoutName + "</c> from a sequence of segments: a single segment is read in place, several are copied into a pooled buffer bounded by the total read budget.</summary>");
        writer.Line("/// <param name=\"source\">The bytes; offset 0 is coordinate zero.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The read options; <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Line("/// <returns>The parsed value.</returns>");
        writer.Open("public static " + name + " " + method + "(global::System.Buffers.ReadOnlySequence<byte> source, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null)");
        writer.Open("if (source.IsSingleSegment)");
        writer.Line("return " + method + "(source.FirstSpan, variables, options);");
        writer.Close();
        writer.Line("byte[] buffer = " + Cursor + ".CopySequence(source, options, out int length);");
        writer.Open("try");
        writer.Line("return " + method + "(new global::System.ReadOnlySpan<byte>(buffer, 0, length), variables, options);");
        writer.Close();
        writer.Open("finally");
        writer.Line("global::System.Buffers.ArrayPool<byte>.Shared.Return(buffer);");
        writer.Close();
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>Reads one <c>" + composite.LayoutName + "</c> from <paramref name=\"stream\"/>: the stream is buffered up to the total read budget (or its remaining length) and read through the span reader; a seekable stream is left after the value.</summary>");
        writer.Line("/// <param name=\"stream\">The stream, read from its current position.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The read options; <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Line("/// <returns>The parsed value.</returns>");
        writer.Open("public static " + name + " " + method + "(global::System.IO.Stream stream, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null)");
        writer.Line("global::System.ArgumentNullException.ThrowIfNull(stream);");
        writer.Line("long start = stream.CanSeek ? stream.Position : 0;");
        writer.Open("try");
        writer.Line("byte[] buffer = " + Cursor + ".BufferStream(stream, options, out int length);");
        writer.Open("try");
        writer.Line("return " + method + "Buffered(buffer, length, stream, start, variables, options);");
        writer.Close();
        writer.Open("finally");
        writer.Line("global::System.Buffers.ArrayPool<byte>.Shared.Return(buffer);");
        writer.Close();
        writer.Close();
        EmitStreamFailureRestoration(writer);
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>Reads one <c>" + composite.LayoutName + "</c> from <paramref name=\"stream\"/> with the bytes read by <see cref=\"global::System.IO.Stream.ReadAsync(global::System.Memory{byte}, global::System.Threading.CancellationToken)\"/>: the same buffering and the same reader as the synchronous form; a seekable stream is left after the value (at its origin on failure), a stream that cannot seek is consumed up to the total read budget plus one byte. A stored absolute pointer address counts from the origin, as in the span form.</summary>");
        writer.Line("/// <param name=\"stream\">The stream, read from its current position.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The read options; <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Line("/// <param name=\"cancellationToken\">Ends the read while it waits for bytes or at the next boundary the reader checks; linked with the options' token.</param>");
        writer.Line("/// <returns>The parsed value.</returns>");
        writer.Open("public static async global::System.Threading.Tasks.ValueTask<" + name + "> " + method + "Async(global::System.IO.Stream stream, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null, global::System.Threading.CancellationToken cancellationToken = default)");
        writer.Line("global::System.ArgumentNullException.ThrowIfNull(stream);");
        writer.Line("global::CStructSharp.ReadOptions? effective = " + Cursor + ".WithCancellation(options, cancellationToken, out global::System.Threading.CancellationTokenSource? linked);");
        writer.Open("using (linked)");
        writer.Line("long start = stream.CanSeek ? stream.Position : 0;");
        writer.Open("try");
        writer.Line("(byte[] buffer, int length) = await " + Cursor + ".BufferStreamAsync(stream, effective, effective?.CancellationToken ?? default).ConfigureAwait(false);");
        writer.Open("try");
        writer.Line("return " + method + "Buffered(buffer, length, stream, start, variables, effective);");
        writer.Close();
        writer.Open("finally");
        writer.Line("global::System.Buffers.ArrayPool<byte>.Shared.Return(buffer);");
        writer.Close();
        writer.Close();
        EmitStreamFailureRestoration(writer);
        writer.Close();
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>The synchronous half of the stream forms: the span reader over the buffered bytes, then the stream's final position (after the value, or the origin on failure).</summary>");
        writer.Open("private static " + name + " " + method + "Buffered(byte[] buffer, int length, global::System.IO.Stream stream, long start, " + VariablesType + " variables, global::CStructSharp.ReadOptions? options)");
        writer.Line("var cursor = new " + Cursor + "(new global::System.ReadOnlySpan<byte>(buffer, 0, length), options, " + SourceWriter.Literal(composite.LayoutName) + ");");
        writer.Open("try");
        writer.Line(name + " value = Read" + name + "(ref cursor, variables, null, null);");
        writer.Open("if (stream.CanSeek)");
        writer.Line("stream.Position = start + cursor.Position;");
        writer.Close();
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
            writer.Line("/// <summary>Reads the root declaration (<c>" + composite.LayoutName + "</c>); see <see cref=\"" + method + "(global::System.ReadOnlySpan{byte}, " + VariablesType.TrimEnd('?').Replace("<string, int>", "{string, int}") + ", global::CStructSharp.ReadOptions)\"/>.</summary>");
            writer.Line("/// <param name=\"source\">The bytes; offset 0 is coordinate zero.</param>");
            writer.Line("/// <param name=\"options\">The read options; <see langword=\"null\"/> uses the documented defaults.</param>");
            writer.Line("/// <returns>The parsed value.</returns>");
            writer.Line("public static " + name + " Parse(global::System.ReadOnlySpan<byte> source, global::CStructSharp.ReadOptions? options = null) => " + method + "(source, null, options);");
            writer.Line();
            writer.Line("/// <inheritdoc cref=\"Parse(global::System.ReadOnlySpan{byte}, global::CStructSharp.ReadOptions)\"/>");
            writer.Line("public static " + name + " Parse(byte[] source, global::CStructSharp.ReadOptions? options = null) => " + method + "(source, null, options);");
            writer.Line();
            writer.Line("/// <inheritdoc cref=\"Parse(global::System.ReadOnlySpan{byte}, global::CStructSharp.ReadOptions)\"/>");
            writer.Line("public static " + name + " Parse(global::System.ReadOnlyMemory<byte> source, global::CStructSharp.ReadOptions? options = null) => " + method + "(source.Span, null, options);");
            writer.Line();
            writer.Line("/// <inheritdoc cref=\"" + method + "(global::System.Buffers.ReadOnlySequence{byte}, " + VariablesType.TrimEnd('?').Replace("<string, int>", "{string, int}") + ", global::CStructSharp.ReadOptions)\"/>");
            writer.Line("public static " + name + " Parse(global::System.Buffers.ReadOnlySequence<byte> source, global::CStructSharp.ReadOptions? options = null) => " + method + "(source, null, options);");
            writer.Line();
            writer.Line("/// <inheritdoc cref=\"" + method + "(global::System.IO.Stream, " + VariablesType.TrimEnd('?').Replace("<string, int>", "{string, int}") + ", global::CStructSharp.ReadOptions)\"/>");
            writer.Line("public static " + name + " Parse(global::System.IO.Stream stream, global::CStructSharp.ReadOptions? options = null) => " + method + "(stream, null, options);");
            writer.Line();
            writer.Line("/// <inheritdoc cref=\"" + method + "Async(global::System.IO.Stream, " + VariablesType.TrimEnd('?').Replace("<string, int>", "{string, int}") + ", global::CStructSharp.ReadOptions, global::System.Threading.CancellationToken)\"/>");
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
            writer.Line("/// <summary>Reads one <c>" + composite.LayoutName + "</c> from " + kind + " without throwing for a read, path, or limit failure: <see langword=\"false\"/> and the failure instead. Cancellation and argument errors throw as in <see cref=\"" + method + "(" + type.Replace('<', '{').Replace('>', '}') + ", " + VariablesType.TrimEnd('?').Replace("<string, int>", "{string, int}") + ", global::CStructSharp.ReadOptions)\"/>.</summary>");
            writer.Line("/// <param name=\"" + parameter + "\">The input.</param>");
            writer.Line("/// <param name=\"value\">The parsed value, or <see langword=\"null\"/> when the read failed.</param>");
            writer.Line("/// <param name=\"failure\">The failure the throwing form would have raised, or <see langword=\"null\"/>.</param>");
            writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
            writer.Line("/// <param name=\"options\">The read options; <see langword=\"null\"/> uses the documented defaults.</param>");
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
            writer.Line("/// <inheritdoc cref=\"Try" + method + "(" + type.Replace('<', '{').Replace('>', '}') + ", out " + name + ", out " + Failure.Replace('<', '{').Replace('>', '}') + "?, " + VariablesType.TrimEnd('?').Replace("<string, int>", "{string, int}") + ", global::CStructSharp.ReadOptions)\"/>");
            writer.Line("public static bool Try" + method + "(" + type + " " + parameter + ", " + MaybeNull + "out " + name + " value, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null) => Try" + method + "(" + parameter + ", out value, out _, variables, options);");
            if (isRoot)
            {
                writer.Line();
                writer.Line("/// <inheritdoc cref=\"Try" + method + "(" + type.Replace('<', '{').Replace('>', '}') + ", out " + name + ", out " + Failure.Replace('<', '{').Replace('>', '}') + "?, " + VariablesType.TrimEnd('?').Replace("<string, int>", "{string, int}") + ", global::CStructSharp.ReadOptions)\"/>");
                writer.Line("public static bool TryParse(" + type + " " + parameter + ", " + MaybeNull + "out " + name + " value, out " + Failure + "? failure, global::CStructSharp.ReadOptions? options = null) => Try" + method + "(" + parameter + ", out value, out failure, null, options);");
                writer.Line();
                writer.Line("/// <inheritdoc cref=\"Try" + method + "(" + type.Replace('<', '{').Replace('>', '}') + ", out " + name + ", out " + Failure.Replace('<', '{').Replace('>', '}') + "?, " + VariablesType.TrimEnd('?').Replace("<string, int>", "{string, int}") + ", global::CStructSharp.ReadOptions)\"/>");
                writer.Line("public static bool TryParse(" + type + " " + parameter + ", " + MaybeNull + "out " + name + " value, global::CStructSharp.ReadOptions? options = null) => Try" + method + "(" + parameter + ", out value, out _, null, options);");
            }
        }
    }

    /// <summary>One composite's reader, entered with the cursor at the composite's first byte.</summary>
    private void EmitCompositeReader(SourceWriter writer, GeneratedComposite composite)
    {
        string name = composite.Name;
        writer.Line("/// <summary>Reads one <c>" + composite.LayoutName + "</c> at the cursor's position.</summary>");
        writer.Open("private static " + name + " Read" + name + "(ref " + Cursor + " cursor, " + VariablesType + " variables, string? member, string? memberType)");
        writer.Line("cursor.EnterComposite(member ?? " + SourceWriter.Literal(composite.LayoutName) + ", memberType);");
        writer.Line("var value = new " + name + "();");
        var scope = new ReaderScope(this, composite);
        this.decidedGroups.Clear();
        if (composite.IsUnion)
        {
            this.EmitUnionBody(writer, composite, scope);
        }
        else
        {
            writer.Line("var placement = global::CStructSharp.Generated.CompositeCursor.Start(cursor.Position, Aligned, Packing, Allocation);");
            this.EmitFields(writer, composite.Composite, scope, "value", "placement");
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

        this.EmitFieldBody(writer, field, scope, target, inUnion, placement, promoted, member, memberType, openBlock: field.ConditionalBranches.Length == 0);
        for (int index = 0; index < field.ConditionalBranches.Length; index++)
        {
            writer.Close();
        }
    }

    /// <summary>Evaluates a group's selector into its arm slot: an <c>if</c> selects arm 1 or 0, a <c>switch</c> maps the value through its case table (default is arm -1).</summary>
    private void EmitSelector(SourceWriter writer, ConditionalGroup group, ReaderScope scope, string slot)
    {
        string code = scope.Expressions.Emit(group.Selector);
        string selection;
        if (group.CaseArms is { } cases)
        {
            var arms = new System.Text.StringBuilder("(" + code + ") switch { ");
            foreach (KeyValuePair<int, int> arm in cases.OrderBy(pair => pair.Value))
            {
                arms.Append(Int(arm.Key)).Append(" => ").Append(Int(arm.Value)).Append(", ");
            }

            selection = arms.Append("_ => -1 }").ToString();
        }
        else
        {
            selection = "(" + code + ") != 0 ? 1 : 0";
        }

        // A selector failure is the composite's, not any field's: the enclosing member is what the runtime notes.
        writer.Open("try");
        writer.Line(slot + " = " + selection + ";");
        writer.Close();
        writer.Open("catch (global::System.Exception expressionFailure)");
        writer.Line("throw cursor.FailExpression(expressionFailure, \"conditional selector\", member, memberType);");
        writer.Close();
    }

    private void EmitFieldBody(SourceWriter writer, CompiledField field, ReaderScope scope, string target, bool inUnion, string placement, bool promoted, string member, string memberType, bool openBlock)
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
            writer.Line("cursor.Seek(" + placement + ".AdvanceToField(" + Int(field.Alignment) + "), " + member + ", " + memberType + ");");
            this.EmitOffsetAssertion(writer, field, scope, member, memberType);
        }

        CompiledCompositeType? inline = this.InlineComposite(field);
        if (field.IsUnnamed && inline is not null)
        {
            // Promoted: read the anonymous composite's members straight into this value.
            if (inline.IsUnion)
            {
                this.EmitPromotedUnion(writer, inline, scope, target, member, memberType, placement);
            }
            else
            {
                string inner = placement + "N";
                writer.Line("cursor.EnterComposite(" + member + ", " + memberType + ");");
                writer.Line("var " + inner + " = global::CStructSharp.Generated.CompositeCursor.Start(cursor.Position, Aligned, Packing, Allocation);");
                this.EmitFields(writer, inline, scope, target, inner);
                writer.Line("cursor.Seek(" + inner + ".Finish(" + Int(inline.Symbol.Alignment) + "), " + member + ", " + memberType + ");");
                writer.Line("cursor.ExitComposite();");
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

    private static void CloseBlock(SourceWriter writer, bool openBlock)
    {
        if (openBlock)
        {
            writer.Close();
        }
    }

    private void EmitPromotedUnion(SourceWriter writer, CompiledCompositeType union, ReaderScope scope, string target, string member, string memberType, string placement)
    {
        // The runtime reads the anonymous union as a union value and splices its members into the parent.
        writer.Line("int unionStart = cursor.Position;");
        int? size = union.Symbol.FixedSize;
        writer.Line("cursor.EnterComposite(" + member + ", " + memberType + ");");
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
        writer.Line("cursor.ExitComposite();");
        writer.Line(size is { } end ? "cursor.Position = unionStart + " + Int(end) + ";" : "cursor.Position = unionEnd;");
    }

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
        writer.Line("throw cursor.Fail(" + SourceWriter.Literal("Bitfield exceeds its storage unit: " + field.Name) + ", " + member + ", " + memberType + ");");
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

    private void EmitOffsetAssertion(SourceWriter writer, CompiledField field, ReaderScope scope, string member, string memberType)
    {
        Expr? assertion = field.Declaration.OffsetAssertionExpression;
        if (assertion is null || field.FixedOffset.HasValue)
        {
            return;
        }

        writer.Open(string.Empty);
        writer.Line("int asserted;");
        this.EmitExpression(writer, assertion, scope, "asserted", "offset assertion for " + field.Name, member, memberType, "int");
        writer.Open("if (asserted < 0)");
        writer.Line("throw cursor.FailLayout(" + SourceWriter.Literal("Explicit offset assertion must be non-negative: " + field.Name + " = ") + " + asserted.ToString(global::System.Globalization.CultureInfo.InvariantCulture), " + member + ", " + memberType + ");");
        writer.Close();
        writer.Open("if (asserted != cursor.Position)");
        writer.Line("throw cursor.FailLayout(" + SourceWriter.Literal("Field '" + field.Name + "' asserts offset ") + " + asserted.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \" but computed offset is \" + cursor.Position.ToString(global::System.Globalization.CultureInfo.InvariantCulture) + \".\", " + member + ", " + memberType + ");");
        writer.Close();
        writer.Close();
    }

    /// <summary>Evaluates a layout expression into <paramref name="local"/>, wrapping operator failures as the runtime does.</summary>
    private void EmitExpression(SourceWriter writer, Expr expression, ReaderScope scope, string local, string context, string member, string memberType, string type)
    {
        string code = scope.Expressions.Emit(expression);
        if (ExpressionEmitter.IsInt32Literal(expression))
        {
            // A constant (a fixed count, a folded sizeof) cannot fail.
            writer.Line(local + " = " + code + ";");
            return;
        }

        writer.Open("try");
        writer.Line(local + " = " + code + ";");
        writer.Close();
        writer.Open("catch (global::System.Exception expressionFailure)");
        writer.Line("throw cursor.FailExpression(expressionFailure, " + SourceWriter.Literal(context) + ", " + member + ", " + memberType + ");");
        writer.Close();
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

    private void EmitValue(SourceWriter writer, CompiledField field, GeneratedMember generated, ReaderScope scope, string target, bool inUnion, string member, string memberType)
    {
        string property = target + "." + generated.PropertyName;
        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            writer.Line(property + " = " + this.ScalarRead(field, generated, member, memberType) + ";");
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
            return "Read" + PointerReaderName(field) + "(ref cursor, variables, " + member + ", " + memberType + ")";
        }

        if (generated.Composite is not null)
        {
            return "Read" + generated.Composite.Name + "(ref cursor, variables, " + member + ", " + memberType + ")";
        }

        if (generated.Enum is not null)
        {
            return "(" + generated.Enum.Name + ")" + this.NumericRead(field.Codec, member, memberType);
        }

        return this.PrimitiveRead(field, member, memberType);
    }

    private string PrimitiveRead(CompiledField field, string member, string memberType)
        => this.PrimitiveRead(field.Codec, field.Type.Symbol.Name, member, memberType);

    private string PrimitiveRead(PrimitiveCodec codec, string typeName, string member, string memberType)
    {
        string le = Bool(codec.LittleEndian);
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
            return CodecClass + ".DecodeFixedPoint(" + CodecClass + ".ReadInt32(cursor.Take(4, " + member + ", " + memberType + "), " + le + "), 16)";
        case PrimitiveCodecKind.UFixed16_16:
            return CodecClass + ".DecodeFixedPoint(" + CodecClass + ".ReadUInt32(cursor.Take(4, " + member + ", " + memberType + "), " + le + "), 16)";
        case PrimitiveCodecKind.Fixed2_30:
            return CodecClass + ".DecodeFixedPoint(" + CodecClass + ".ReadInt32(cursor.Take(4, " + member + ", " + memberType + "), " + le + "), 30)";
        case PrimitiveCodecKind.UFixed8_8:
            return CodecClass + ".DecodeFixedPoint(" + CodecClass + ".ReadUInt16(cursor.Take(2, " + member + ", " + memberType + "), " + le + "), 8)";
        case PrimitiveCodecKind.Custom:
            return "cursor.TakeCustom(CodecInstances.Value[" + Int(this.CodecIndex(typeName)) + "], " + member + ", " + memberType + ")";
        default:
            return this.NumericRead(codec, member, memberType);
        }
    }

    /// <summary>The expression that decodes one fixed-width numeric value (including the single-byte character units and wide integers).</summary>
    private string NumericRead(PrimitiveCodec codec, string member, string memberType)
    {
        string le = Bool(codec.LittleEndian);
        string take = "cursor.Take(" + Int(codec.Size) + ", " + member + ", " + memberType + ")";
        return codec.Kind switch
        {
            PrimitiveCodecKind.UInt8 or PrimitiveCodecKind.Latin1 or PrimitiveCodecKind.Cp437 or PrimitiveCodecKind.Utf8Unit or PrimitiveCodecKind.Utf16LeUnit or PrimitiveCodecKind.Utf16BeUnit => take + "[0]",
            PrimitiveCodecKind.Int8 => "unchecked((sbyte)" + take + "[0])",
            PrimitiveCodecKind.Bool => take + "[0] != 0",
            PrimitiveCodecKind.Char => "(char)" + take + "[0]",
            PrimitiveCodecKind.WChar => CodecClass + ".ReadChar(" + take + ", " + le + ")",
            PrimitiveCodecKind.Int16 => CodecClass + ".ReadInt16(" + take + ", " + le + ")",
            PrimitiveCodecKind.UInt16 => CodecClass + ".ReadUInt16(" + take + ", " + le + ")",
            PrimitiveCodecKind.Int24 => CodecClass + ".ReadInt24(" + take + ", " + le + ")",
            PrimitiveCodecKind.UInt24 => CodecClass + ".ReadUInt24(" + take + ", " + le + ")",
            PrimitiveCodecKind.Int32 => CodecClass + ".ReadInt32(" + take + ", " + le + ")",
            PrimitiveCodecKind.UInt32 => CodecClass + ".ReadUInt32(" + take + ", " + le + ")",
            PrimitiveCodecKind.Int48 => CodecClass + ".ReadInt48(" + take + ", " + le + ")",
            PrimitiveCodecKind.UInt48 => CodecClass + ".ReadUInt48(" + take + ", " + le + ")",
            PrimitiveCodecKind.Int64 => CodecClass + ".ReadInt64(" + take + ", " + le + ")",
            PrimitiveCodecKind.UInt64 => CodecClass + ".ReadUInt64(" + take + ", " + le + ")",
            PrimitiveCodecKind.Int128 => CodecClass + ".ReadInt128(" + take + ", " + le + ")",
            PrimitiveCodecKind.UInt128 => CodecClass + ".ReadUInt128(" + take + ", " + le + ")",
            PrimitiveCodecKind.Float16 => CodecClass + ".ReadHalf(" + take + ", " + le + ")",
            PrimitiveCodecKind.Float32 => CodecClass + ".ReadSingle(" + take + ", " + le + ")",
            PrimitiveCodecKind.Float64 => CodecClass + ".ReadDouble(" + take + ", " + le + ")",
            _ => throw new InvalidOperationException("Codec is not a fixed-width numeric: " + codec.Kind),
        };
    }

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

    private static string CharLiteral(char character) => character == '\0' ? "'\\0'" : character == '\n' ? "'\\n'" : "'" + character + "'";

    private static string Int(int value) => value.ToString(CultureInfo.InvariantCulture);

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

    private static string PointerReaderName(CompiledField field) => "Pointer_" + Sanitize(field.TypeSpelling) + "_" + Int(field.PointerDepth);

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
