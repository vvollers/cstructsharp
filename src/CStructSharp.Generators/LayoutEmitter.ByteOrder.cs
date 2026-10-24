namespace CStructSharp.Generators;

using CStructSharp.Codecs;
using CStructSharp.Compilation;
using static CStructSharp.Generators.Emit;

/// <summary>
///     Run-time byte order (<c>RuntimeByteOrder</c>): a neutral value - one the layout spells without a <c>&lt;</c> or
///     <c>&gt;</c> suffix - is decoded with a byte order that the caller chooses when it reads, so one layout stands for
///     the little- and the big-endian pair. The emitter keeps the C# expression that names the order in the code being
///     written (<c>cursor.LittleEndian</c> in a reader, <c>this.littleEndian</c> in a view, <c>littleEndian</c> in a
///     helper with a parameter of that name); every site that decodes a neutral value uses it, and every explicit value
///     keeps its own literal.
/// </summary>
internal sealed partial class LayoutEmitter
{
    /// <summary>The expression naming the run-time byte order in the code being emitted, or <see langword="null"/> when the order is fixed at build time.</summary>
    private string? runtimeOrder;

    /// <summary>Gets a value indicating whether the layout's neutral values follow a byte order given at read time.</summary>
    private bool HasRuntimeByteOrder => this.request.RuntimeByteOrder;

    /// <summary>The parameter a read method takes after its source when the byte order is chosen at read time, or nothing.</summary>
    private string OrderParameter => this.HasRuntimeByteOrder ? "bool littleEndian, " : string.Empty;

    /// <summary>The argument that passes that parameter on, or nothing.</summary>
    private string OrderArgument => this.HasRuntimeByteOrder ? "littleEndian, " : string.Empty;

    /// <summary>The parameter a fixed or bitfield leaf reader takes after its other parameters when the byte order is chosen at read time, or nothing.</summary>
    private string TrailingOrderParameter => this.HasRuntimeByteOrder ? ", bool littleEndian" : string.Empty;

    /// <summary>The argument that passes a helper's own <c>littleEndian</c> parameter on, or nothing.</summary>
    private string TrailingOrderArgument => this.HasRuntimeByteOrder ? ", littleEndian" : string.Empty;

    /// <summary>The documentation line of the <c>littleEndian</c> parameter, or nothing.</summary>
    private string OrderParameterDoc => this.HasRuntimeByteOrder ? "/// <param name=\"littleEndian\">Whether neutral values are little-endian.</param>" : string.Empty;

    /// <summary>The parameter type a documentation reference names after the source when the byte order is chosen at read time, or nothing.</summary>
    private string OrderCref => this.HasRuntimeByteOrder ? "bool, " : string.Empty;

    /// <summary>Writes the documentation of the <c>littleEndian</c> parameter of a read method, when it has one.</summary>
    /// <param name="writer">The output.</param>
    private void EmitOrderDoc(SourceWriter writer)
    {
        if (this.HasRuntimeByteOrder)
        {
            writer.Line(this.OrderParameterDoc);
        }
    }

    /// <summary>The argument that follows a cursor's path when the byte order is chosen at read time, or nothing.</summary>
    private string CursorOrderArgument => this.HasRuntimeByteOrder ? ", littleEndian" : string.Empty;

    /// <summary>
    ///     Whether a type spelling is order-neutral: it has no suffix of its own, so its order is the layout's. A spelling
    ///     is neutral when resolving it for a little-endian layout and for a big-endian one gives opposite orders.
    /// </summary>
    /// <param name="codecName">The field's codec name (<see cref="CompiledField.CodecName"/>).</param>
    /// <returns>Whether the value follows the layout's byte order.</returns>
    private static bool IsOrderNeutral(string? codecName)
        => !string.IsNullOrEmpty(codecName) && PrimitiveCodec.Resolve(codecName, true).LittleEndian != PrimitiveCodec.Resolve(codecName, false).LittleEndian;

    /// <summary>The run-time order expression for a value of a type spelling, or <see langword="null"/> when its order is fixed.</summary>
    /// <param name="codecName">The type spelling.</param>
    /// <returns>The expression, or <see langword="null"/>.</returns>
    private string? EndianOfName(string? codecName) => this.runtimeOrder is not null && IsOrderNeutral(codecName) ? this.runtimeOrder : null;

    /// <summary>The run-time order expression for a field's value, or <see langword="null"/> when its order is fixed (an explicit suffix, or no run-time order).</summary>
    /// <param name="field">The field.</param>
    /// <returns>The expression, or <see langword="null"/>.</returns>
    private string? EndianOf(CompiledField field) => this.runtimeOrder is not null && IsOrderNeutral(field.CodecName) ? this.runtimeOrder : null;

    /// <summary>The order of a value of a named type spelling, as a C# expression: the run-time one for a neutral spelling, otherwise the literal.</summary>
    /// <param name="codecName">The type spelling.</param>
    /// <param name="compiled">The order the layout compiled the value with.</param>
    /// <returns>The expression.</returns>
    private string EndianExpression(string? codecName, bool compiled) => this.runtimeOrder is not null && IsOrderNeutral(codecName) ? this.runtimeOrder : Bool(compiled);

    /// <summary>The order of a field's value (or of its bitfield storage), as a C# expression.</summary>
    /// <param name="field">The field.</param>
    /// <param name="compiled">The order the layout compiled the value with.</param>
    /// <returns>The expression.</returns>
    private string EndianExpression(CompiledField field, bool compiled) => this.EndianExpression(field.CodecName, compiled);

    /// <summary>The order of a wide-character field's code units, as a C# expression: the field's own suffix, or else the layout's (the run-time one when it is chosen at read time).</summary>
    /// <param name="field">The <c>wchar</c> field or element.</param>
    /// <returns>The expression.</returns>
    private string WideEndian(CompiledField field)
        => field.ExplicitWideCharacterEncoding is null ? this.runtimeOrder ?? Bool(this.request.Settings.LittleEndian) : Bool(field.Codec.LittleEndian);

    /// <summary>The order that decodes a pointer's stored address, as a C# expression: the run-time one, or the generated class's constant.</summary>
    private string PointerEndian => this.runtimeOrder ?? "LittleEndian";

    /// <summary>Sets the expression that names the run-time byte order for the code emitted next; nothing when the order is fixed.</summary>
    /// <param name="expression">The expression, such as <c>cursor.LittleEndian</c>.</param>
    private void UseRuntimeOrder(string expression) => this.runtimeOrder = this.HasRuntimeByteOrder ? expression : null;
}
