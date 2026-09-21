namespace CStructSharp.Generators;

using System.Globalization;
using CStructSharp.Diagnostics;

/// <summary>
///     Record sequences: <c>Records&lt;Name&gt;</c> over memory, a segmented sequence, or a stream and
///     <c>Records&lt;Name&gt;Async</c> over a stream (the root forms <c>Records</c>/<c>RecordsAsync</c>), each an
///     enumeration of one composite after another until the input ends, driven by the runtime's
///     <c>RecordSequence</c> over a generated reader that parses one record as its own region; and, for a composite
///     with a static size, the <c>foreach</c>-able <c>&lt;Name&gt;View.Enumerate</c> over views with no allocation.
/// </summary>
internal sealed partial class LayoutEmitter
{
    private const string Sequence = "global::CStructSharp.Generated.RecordSequence";
    private const string Enumerable = "global::System.Collections.Generic.IEnumerable<";
    private const string AsyncEnumerable = "global::System.Collections.Generic.IAsyncEnumerable<";

    /// <summary>
    ///     The stride a sequence of the composite advances by, when the layout itself fixes it: the size
    ///     <c>GetStructSizeInBytes</c> reports (a count that is a <c>#define</c> or a constant expression counts as
    ///     fixed, as it does there), or <see langword="null"/> for a composite whose extent depends on an operation
    ///     variable or on its own data - the runtime's <c>ParseMany</c> draws the line at the same place.
    /// </summary>
    private int? RecordStride(GeneratedComposite composite)
    {
        if (composite.Composite.Symbol.FixedSize is { } known)
        {
            return known;
        }

        try
        {
            return this.compilation.GetStructSizeInBytes(composite.LayoutName);
        }
        catch (CStructException)
        {
            return null;
        }
    }

    /// <summary>The size expression a sequence advances by: the <c>Sizes</c> constant, the evaluated size, or <c>null</c>.</summary>
    private string RecordSize(GeneratedComposite composite)
        => composite.Composite.Symbol.FixedSize is not null ? "Sizes." + composite.Name : this.RecordStride(composite) is { } stride ? stride.ToString(CultureInfo.InvariantCulture) : "null";

    private void EmitRecords(SourceWriter writer, GeneratedComposite composite, bool isRoot)
    {
        string name = composite.Name;
        string method = "Records" + name;
        string layout = SourceWriter.Literal(composite.LayoutName);
        string size = this.RecordSize(composite);
        string reader = "global::CStructSharp.Generated.RecordReader<" + name + ">";
        string cref = VariablesType.TrimEnd('?').Replace("<string, int>", "{string, int}");
        int? stride = this.RecordStride(composite);
        string pointerRule = stride is null
            ? "a runtime-sized <c>" + composite.LayoutName + "</c> advances by the bytes the previous record consumed"
            : "each record is " + stride.Value.ToString(CultureInfo.InvariantCulture) + " bytes";
        writer.Line();
        writer.Line("/// <summary>Reads the records of <paramref name=\"source\"/> - one <c>" + composite.LayoutName + "</c> after another until the memory ends - on the enumeration step that reaches each; " + pointerRule + ". Trailing bytes shorter than one record fail on the step that meets them; a failure names the record by its index before the path (<c>[3]." + composite.LayoutName + "</c>). Each record is its own region: the read limits apply per record and a stored absolute pointer address counts from the record's first byte.</summary>");
        writer.Line("/// <param name=\"source\">The bytes of the records, with nothing else after them.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The read options each record is read with; <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Line("/// <returns>The records, read as they are enumerated.</returns>");
        writer.Line("public static " + Enumerable + name + "> " + method + "(global::System.ReadOnlyMemory<byte> source, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null)");
        writer.Line("    => " + Sequence + ".FromMemory(source, " + size + ", " + layout + ", options, " + name + "RecordReader(variables));");
        writer.Line();
        writer.Line("/// <summary>Reads the records of a sequence of segments (one segment in place, several through one pooled copy that lives as long as the enumeration); see <see cref=\"" + method + "(global::System.ReadOnlyMemory{byte}, " + cref + ", global::CStructSharp.ReadOptions)\"/>.</summary>");
        writer.Line("/// <param name=\"source\">The bytes of the records, with nothing else after them.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The read options each record is read with; <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Line("/// <returns>The records, read as they are enumerated.</returns>");
        writer.Line("public static " + Enumerable + name + "> " + method + "(global::System.Buffers.ReadOnlySequence<byte> source, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null)");
        writer.Line("    => " + Sequence + ".FromSequence(source, " + size + ", " + layout + ", options, " + name + "RecordReader(variables));");
        writer.Line();
        writer.Line("/// <summary>Reads the records of a stream from its current position: " + (stride is null
            ? "through a pooled window of the bytes left (at most the total read budget plus one) that refills from the start of a record it could not hold, which needs a seekable stream"
            : "exactly one record at a time, byte-exact, from any readable stream") + "; a seekable stream sits at the record's end after each step. See <see cref=\"" + method + "(global::System.ReadOnlyMemory{byte}, " + cref + ", global::CStructSharp.ReadOptions)\"/>.</summary>");
        writer.Line("/// <param name=\"stream\">The stream, read from its current position.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The read options each record is read with; <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Line("/// <returns>The records, read as they are enumerated.</returns>");
        writer.Open("public static " + Enumerable + name + "> " + method + "(global::System.IO.Stream stream, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null)");
        this.EmitRecordStreamChecks(writer, composite);
        writer.Line("return " + Sequence + ".FromStream(stream, " + size + ", " + layout + ", options, " + name + "RecordReader(variables));");
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>Reads the records of a stream with <see cref=\"global::System.IO.Stream.ReadAsync(global::System.Memory{byte}, global::System.Threading.CancellationToken)\"/>, for <c>await foreach</c>; the rules of <see cref=\"" + method + "(global::System.IO.Stream, " + cref + ", global::CStructSharp.ReadOptions)\"/>, with the token linked to the options' token.</summary>");
        writer.Line("/// <param name=\"stream\">The stream, read from its current position.</param>");
        writer.Line("/// <param name=\"variables\">Values for the layout's free identifiers, or <see langword=\"null\"/>.</param>");
        writer.Line("/// <param name=\"options\">The read options each record is read with; <see langword=\"null\"/> uses the documented defaults.</param>");
        writer.Line("/// <param name=\"cancellationToken\">Ends the enumeration while it waits for bytes, between records, or at the next boundary the reader checks.</param>");
        writer.Line("/// <returns>The records, read as they are enumerated.</returns>");
        writer.Open("public static " + AsyncEnumerable + name + "> " + method + "Async(global::System.IO.Stream stream, " + VariablesType + " variables = null, global::CStructSharp.ReadOptions? options = null, global::System.Threading.CancellationToken cancellationToken = default)");
        this.EmitRecordStreamChecks(writer, composite);
        writer.Line("return " + Sequence + ".FromStreamAsync(stream, " + size + ", " + layout + ", options, " + name + "RecordReader(variables), cancellationToken);");
        writer.Close();
        if (isRoot)
        {
            foreach ((string type, string parameter) in new[] { ("global::System.ReadOnlyMemory<byte>", "source"), ("global::System.Buffers.ReadOnlySequence<byte>", "source"), ("global::System.IO.Stream", "stream"), })
            {
                writer.Line();
                writer.Line("/// <inheritdoc cref=\"" + method + "(" + type.Replace('<', '{').Replace('>', '}') + ", " + cref + ", global::CStructSharp.ReadOptions)\"/>");
                writer.Line("public static " + Enumerable + name + "> Records(" + type + " " + parameter + ", global::CStructSharp.ReadOptions? options = null) => " + method + "(" + parameter + ", null, options);");
            }

            writer.Line();
            writer.Line("/// <inheritdoc cref=\"" + method + "Async(global::System.IO.Stream, " + cref + ", global::CStructSharp.ReadOptions, global::System.Threading.CancellationToken)\"/>");
            writer.Line("public static " + AsyncEnumerable + name + "> RecordsAsync(global::System.IO.Stream stream, global::CStructSharp.ReadOptions? options = null, global::System.Threading.CancellationToken cancellationToken = default) => " + method + "Async(stream, null, options, cancellationToken);");
        }

        writer.Line();
        writer.Line("/// <summary>The record reader of <c>" + composite.LayoutName + "</c> without variables, shared by every enumeration that has none.</summary>");
        writer.Line("private static readonly " + reader + " " + name + "RecordsWithoutVariables = (global::System.ReadOnlyMemory<byte> source, int offset, int index, long shift, global::CStructSharp.ReadOptions? options, out int consumed) => Read" + name + "Record(source, offset, index, shift, options, null, out consumed);");
        writer.Line();
        writer.Line("/// <summary>The record reader of <c>" + composite.LayoutName + "</c> for <paramref name=\"variables\"/>.</summary>");
        writer.Line("private static " + reader + " " + name + "RecordReader(" + VariablesType + " variables)");
        writer.Line("    => variables is null ? " + name + "RecordsWithoutVariables : (global::System.ReadOnlyMemory<byte> source, int offset, int index, long shift, global::CStructSharp.ReadOptions? options, out int consumed) => Read" + name + "Record(source, offset, index, shift, options, variables, out consumed);");
        writer.Line();
        writer.Line("/// <summary>Reads record <paramref name=\"index\"/> from <paramref name=\"offset\"/> as its own region; a failure names the record and carries the input's coordinate.</summary>");
        writer.Open("private static " + name + " Read" + name + "Record(global::System.ReadOnlyMemory<byte> source, int offset, int index, long shift, global::CStructSharp.ReadOptions? options, " + VariablesType + " variables, out int consumed)");
        writer.Line("var cursor = new " + Cursor + "(source.Span.Slice(offset), options, " + layout + ");");
        writer.Open("try");
        writer.Line(name + " value = Read" + name + "(ref cursor, variables, null, null);");
        writer.Line("consumed = cursor.Position;");
        writer.Line("return value;");
        writer.Close();
        writer.Open("catch (global::CStructSharp.Diagnostics.CStructException exception)");
        writer.Line("cursor.Complete(exception);");
        writer.Line(Sequence + ".Complete(exception, index, shift + offset);");
        writer.Line("throw;");
        writer.Close();
        writer.Close();
    }

    /// <summary>The argument checks of the stream forms: readable, and seekable when the record has no static size.</summary>
    private void EmitRecordStreamChecks(SourceWriter writer, GeneratedComposite composite)
    {
        writer.Line("global::System.ArgumentNullException.ThrowIfNull(stream);");
        writer.Open("if (!stream.CanRead)");
        writer.Line("throw new global::System.ArgumentException(\"Reading requires a readable stream.\", nameof(stream));");
        writer.Close();
        if (this.RecordStride(composite) is null)
        {
            writer.Open("if (!stream.CanSeek)");
            writer.Line("throw new global::System.ArgumentException(\"Records of a runtime-sized struct are read through a window that refills from a record's start, which needs a seekable stream; a fixed-size root is read from any stream.\", nameof(stream));");
            writer.Close();
        }
    }

    /// <summary>The <c>foreach</c>-able enumeration of views over consecutive records, for a composite with a static size.</summary>
    private void EmitViewEnumerator(SourceWriter writer, GeneratedComposite composite, int size)
    {
        string view = ViewName(composite);
        string layout = SourceWriter.Literal(composite.LayoutName);
        string stride = size.ToString(CultureInfo.InvariantCulture);
        writer.Line();
        writer.Line("/// <summary>The records <see cref=\"" + view + ".Enumerate\"/> walks: a <c>foreach</c> source whose enumerator yields one <see cref=\"" + view + "\"/> per <c>" + composite.LayoutName + "</c>, allocating nothing.</summary>");
        writer.Open("public readonly ref struct " + view + "Enumerable");
        writer.Line("private readonly global::System.ReadOnlySpan<byte> source;");
        writer.Line("private readonly global::CStructSharp.ReadOptions? options;");
        writer.Line();
        writer.Line("/// <summary>Creates the enumeration over <paramref name=\"source\"/>.</summary>");
        writer.Line("/// <param name=\"source\">The bytes of the records, with nothing else after them.</param>");
        writer.Line("/// <param name=\"options\">The read options each view's <c>ToObject</c> uses.</param>");
        writer.Open("public " + view + "Enumerable(global::System.ReadOnlySpan<byte> source, global::CStructSharp.ReadOptions? options)");
        writer.Line("this.source = source;");
        writer.Line("this.options = options;");
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>The enumerator <c>foreach</c> uses.</summary>");
        writer.Line("/// <returns>An enumerator before the first record.</returns>");
        writer.Line("public " + view + "Enumerator GetEnumerator() => new " + view + "Enumerator(this.source, this.options);");
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>Walks consecutive <c>" + composite.LayoutName + "</c> records of " + stride + " bytes; trailing bytes shorter than one record fail on the step that meets them, with the record's index in the path.</summary>");
        writer.Open("public ref struct " + view + "Enumerator");
        writer.Line("private readonly global::System.ReadOnlySpan<byte> source;");
        writer.Line("private readonly global::CStructSharp.ReadOptions? options;");
        writer.Line("private int offset;");
        writer.Line("private int index;");
        writer.Line();
        writer.Line("/// <summary>Creates an enumerator before the first record of <paramref name=\"source\"/>.</summary>");
        writer.Line("/// <param name=\"source\">The bytes of the records, with nothing else after them.</param>");
        writer.Line("/// <param name=\"options\">The read options each view's <c>ToObject</c> uses.</param>");
        writer.Open("public " + view + "Enumerator(global::System.ReadOnlySpan<byte> source, global::CStructSharp.ReadOptions? options)");
        writer.Line("this.source = source;");
        writer.Line("this.options = options;");
        writer.Line("this.offset = 0;");
        writer.Line("this.index = -1;");
        writer.Close();
        writer.Line();
        writer.Line("/// <summary>Gets the view of the current record.</summary>");
        writer.Line("public readonly " + view + " Current => new " + view + "(this.source.Slice(this.offset), this.options);");
        writer.Line();
        writer.Line("/// <summary>Advances to the next record.</summary>");
        writer.Line("/// <returns><see langword=\"false\"/> when the source ends exactly after the last record.</returns>");
        writer.Open("public bool MoveNext()");
        writer.Line("int next = this.index < 0 ? 0 : this.offset + " + stride + ";");
        writer.Open("if (next >= this.source.Length)");
        writer.Line("return false;");
        writer.Close();
        writer.Open("if (this.source.Length - next < " + stride + ")");
        writer.Line("throw " + Sequence + ".Partial(this.source.Length - next, " + stride + ", " + layout + ", this.index + 1, next);");
        writer.Close();
        writer.Line("this.offset = next;");
        writer.Line("this.index++;");
        writer.Line("return true;");
        writer.Close();
        writer.Close();
    }
}
