namespace CStructSharp.Generators;

using System.Globalization;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using static CStructSharp.Generators.Emit;

/// <summary>
///     <c>Shape = Scalar</c>: a layout whose root struct holds one number, enum or identifier becomes a static <c>Parse</c> that
///     returns that value. No class, no properties and no per-struct reader family are generated: a project with hundreds of
///     one-field layouts (a stored integer, a word, a magic number) writes a short method for each instead of a class and three
///     readers.
/// </summary>
internal sealed partial class LayoutEmitter
{
    /// <summary>Gets a value indicating whether the layout generates a scalar reader instead of types.</summary>
    private bool UsesScalarShape => this.request.Shape == GenerationShape.Scalar && this.ScalarProblem() is null;

    /// <summary>
    ///     Why the layout cannot be a scalar, or <see langword="null"/> when it can: the surface has no readers, or the layout is
    ///     not one struct whose one member is a number, enum or identifier at offset 0 that fills the struct.
    /// </summary>
    /// <returns>The reason, as the end of the sentence "'Class' sets Shape = Scalar, which ...", or <see langword="null"/>.</returns>
    private string? ScalarProblem()
    {
        if (this.request.Surface is not (GenerationSurface.Reader or GenerationSurface.Parser))
        {
            return "needs the Reader or Parser surface, because it generates one static Parse and nothing else";
        }

        if (this.model.Composites.Count != 1 || this.model.Composites[0].IsUnion)
        {
            return "needs exactly one struct (the root), but the layout declares " + (this.model.Composites.Count == 0 ? "none" : this.model.Composites.Count.ToString(CultureInfo.InvariantCulture) + " structs or unions");
        }

        GeneratedComposite root = this.model.Composites[0];
        if (root.Composite.Fields.Length != 1 || root.Members.Count != 1)
        {
            return "needs a struct with exactly one named member, but '" + root.LayoutName + "' has " + root.Composite.Fields.Length.ToString(CultureInfo.InvariantCulture);
        }

        GeneratedMember member = root.Members[0];
        CompiledField field = member.Field;
        if (member.IsConditional || field.PointerDepth > 0 || field.Array.Kind != CompiledArrayKind.Scalar || field.BitSize > 0 || member.Composite is not null || field.Type.Symbol.IsCustomCodec)
        {
            return "needs the member '" + field.Name + "' to be one number, enum or identifier (not an array, bitfield, pointer, struct, conditional or custom value)";
        }

        bool supported = member.Enum is not null || field.Codec.IsFixedWidthNumeric || field.Codec.IsIdentifier || field.Codec.IsFixedPoint;
        if (!supported)
        {
            return "needs the member '" + field.Name + "' to be a number, enum or identifier, but its type has no fixed size";
        }

        if (field.FixedOffset != 0 || field.FixedStorageSize is not { } stored || root.Composite.Symbol.FixedSize != stored)
        {
            return "needs the member '" + field.Name + "' to fill the struct from offset 0, without padding or an alignment override";
        }

        return null;
    }

    /// <summary>Emits the scalar reader: the layout's enums, the readers, and the constants.</summary>
    /// <param name="writer">The output.</param>
    private void EmitScalar(SourceWriter writer)
    {
        foreach (GeneratedEnum generatedEnum in this.model.Enums)
        {
            writer.Line();
            EmitEnum(writer, generatedEnum);
        }

        GeneratedComposite root = this.model.Composites[0];
        GeneratedMember member = root.Members[0];
        CompiledField field = member.Field;

        // One member that fills the struct: the facts the member-by-member reader would charge are the member's own.
        int stored = field.FixedStorageSize!.Value;
        var plan = new FixedPlan(stored, 1, stored, 1, 0);
        string type = member.TypeName;
        string layout = SourceWriter.Literal(root.LayoutName);
        const string Limits = "global::CStructSharp.ReadLimits";
        const string Failure = "global::CStructSharp.Diagnostics.CStructException";
        bool tryForms = this.request.Surface == GenerationSurface.Reader;
        string size = Int(plan.Size);
        string valueDoc = "the value of <c>" + DescribeDeclaration(field) + "</c>";

        // The fast path decodes from the fixed bytes; the member-by-member path decodes from the cursor and reports any failure as the struct's reader does.
        string? savedOrder = this.runtimeOrder;
        this.UseRuntimeOrder("cursor.LittleEndian");
        string fixedDecode = this.MatchDecode(member, "fixedBytes");
        string memberDecode = this.ScalarRead(field, member, SourceWriter.Literal(field.Name), SourceWriter.Literal(field.DisplayTypeSpelling));
        this.runtimeOrder = savedOrder;

        // Both the conditions and the read use the value as a local named like the member.
        var scope = new ReaderScope(this, root);
        scope.Publish(member, "value");
        writer.Line();
        writer.Line("/// <summary>Reads the member reader's value and checks its conditions: used when the fixed bytes are not handed over, so every failure is the one the struct's reader reports.</summary>");
        writer.Line("/// <param name=\"cursor\">The cursor at the struct's first byte.</param>");
        writer.Line("/// <returns>" + char.ToUpperInvariant(valueDoc[0]) + valueDoc.Substring(1) + ".</returns>");
        writer.Line(NoInlining);
        writer.Open("private static " + type + " ReadMembers(ref " + Cursor + " cursor)");
        writer.Line("cursor.EnterComposite(" + layout + ", null);");
        writer.Line(type + " value = " + memberDecode + ";");
        this.EmitScalarConstraints(writer, field, scope, null, false);
        writer.Line("cursor.ExitComposite();");
        writer.Line("return value;");
        writer.Close();

        bool constrained = !field.Constraints.IsDefaultOrEmpty;
        writer.Line();
        writer.Line("/// <summary>Reads " + valueDoc + ": from the struct's bytes when the cursor can hand them over, otherwise member by member, which reports any failure.</summary>");
        writer.Line("/// <param name=\"cursor\">The cursor at the struct's first byte.</param>");
        if (constrained)
        {
            writer.Line("/// <param name=\"value\">The value; assigned even when a condition is not satisfied.</param>");
            writer.Line("/// <param name=\"failure\">The first condition that is not satisfied, or <see langword=\"null\"/>.</param>");
            writer.Line("/// <returns><see langword=\"false\"/> when a condition stopped the read.</returns>");
            writer.Open("private static bool ReadValue(ref " + Cursor + " cursor, out " + type + " value, out global::CStructSharp.Diagnostics.CStructConstraintException? failure)");
            writer.Line("failure = null;");
            writer.Open("if (" + this.TakeFixedCall(plan) + ")");
            writer.Line("value = " + fixedDecode + ";");
            this.EmitScalarConstraints(writer, field, scope, size, true);
            writer.Line("return true;");
            writer.Close();
            writer.Line("value = ReadMembers(ref cursor);");
            writer.Line("return true;");
            writer.Close();
        }
        else
        {
            // Nothing to check after the bytes are decoded, so the reader returns the value itself and has no failure to carry.
            writer.Line("/// <returns>" + char.ToUpperInvariant(valueDoc[0]) + valueDoc.Substring(1) + ".</returns>");
            writer.Line("[global::System.Runtime.CompilerServices.MethodImpl(global::System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]");
            writer.Open("private static " + type + " ReadValue(ref " + Cursor + " cursor)");
            writer.Open("if (" + this.TakeFixedCall(plan) + ")");
            writer.Line("return " + fixedDecode + ";");
            writer.Close();
            writer.Line("return ReadMembers(ref cursor);");
            writer.Close();
        }

        this.EmitScalarParse(writer, layout, type, valueDoc, Limits, tryForms, Failure, constrained);
    }

    /// <summary>Emits the <c>@expect</c> and <c>@supports</c> checks of the scalar's member after its value is in the local <c>value</c>.</summary>
    /// <param name="writer">The output.</param>
    /// <param name="field">The member's field.</param>
    /// <param name="scope">The expression scope, which has published the member as <c>value</c>.</param>
    /// <param name="failurePosition">The C# expression of the position to report in the fast path, or <see langword="null"/> for the cursor's own.</param>
    /// <param name="returnsFailure">Whether a failing condition returns the failure (the fast path) rather than throws it.</param>
    private void EmitScalarConstraints(SourceWriter writer, CompiledField field, ReaderScope scope, string? failurePosition, bool returnsFailure)
    {
        if (field.Constraints.IsDefaultOrEmpty)
        {
            return;
        }

        // A condition may name a caller variable or a define the caller can override; a scalar reader takes none, so the defaults apply.
        writer.Line(NoVariables);
        foreach (CompiledConstraint constraint in field.Constraints)
        {
            EmitConstraintBlock(writer, field, constraint, scope.Expressions.Emit(constraint.Expression), SourceWriter.Literal(field.Name), SourceWriter.Literal(field.DisplayTypeSpelling), failurePosition, returnsFailure);
        }
    }

    /// <summary>Emits the public <c>Parse</c> overloads (and, on the reader surface, <c>TryParse</c>) of a scalar layout.</summary>
    /// <param name="writer">The output.</param>
    /// <param name="layout">The layout name literal, for the cursor's path.</param>
    /// <param name="type">The C# type of the value.</param>
    /// <param name="valueDoc">The documentation words naming the value.</param>
    /// <param name="limits">The <c>ReadLimits</c> type name.</param>
    /// <param name="tryForms">Whether the non-throwing forms are emitted.</param>
    /// <param name="failureType">The base failure type.</param>
    /// <param name="constrained">Whether the reader carries a constraint failure (<c>ReadValue</c> returns a flag) or returns the value alone.</param>
    private void EmitScalarParse(SourceWriter writer, string layout, string type, string valueDoc, string limits, bool tryForms, string failureType, bool constrained)
    {
        string orderParameter = this.OrderParameter;
        string orderArgument = this.CursorOrderArgument;
        string[] sources = ["source, options, " + layout + orderArgument, "source, in limits, " + layout + orderArgument];
        string[] parameters = [orderParameter + "global::CStructSharp.ReadOptions? options = null", orderParameter + "in " + limits + " limits"];
        string[] docs = [ReadOptionsDoc, "/// <param name=\"limits\">The limits of the read.</param>"];
        for (int form = 0; form < 2; form++)
        {
            writer.Line();
            writer.Line("/// <summary>Reads " + valueDoc + " from the start of <paramref name=\"source\"/>; the same value, and the same failures, as the runtime's <c>Parse</c> of the struct.</summary>");
            writer.Line("/// <param name=\"source\">The bytes; offset 0 is the struct's first byte.</param>");
            this.EmitOrderDoc(writer);
            writer.Line(docs[form]);
            writer.Line("/// <returns>The value.</returns>");
            writer.Open("public static " + type + " Parse(global::System.ReadOnlySpan<byte> source, " + parameters[form] + ")");
            writer.Line("var cursor = new " + Cursor + "(" + sources[form] + ");");
            writer.Open("try");
            if (constrained)
            {
                writer.Open("if (!ReadValue(ref cursor, out " + type + " value, out var failure))");
                writer.Line("throw failure!;");
                writer.Close();
                writer.Line("return value;");
            }
            else
            {
                writer.Line("return ReadValue(ref cursor);");
            }

            writer.Close();
            writer.Open("catch (" + failureType + " exception)");
            writer.Line("cursor.Complete(exception);");
            writer.Line("throw;");
            writer.Close();
            writer.Close();
        }

        if (!tryForms)
        {
            return;
        }

        for (int form = 0; form < 2; form++)
        {
            writer.Line();
            writer.Line("/// <summary>Reads " + valueDoc + " without throwing for a read, path, limit or constraint failure: <see langword=\"false\"/> and the failure instead. A condition that is not satisfied is returned without an exception being thrown. Cancellation and argument errors throw.</summary>");
            writer.Line("/// <param name=\"source\">The bytes; offset 0 is the struct's first byte.</param>");
            this.EmitOrderDoc(writer);
            writer.Line("/// <param name=\"value\">The value, or the default when the read failed.</param>");
            writer.Line("/// <param name=\"failure\">The failure the throwing form would have raised, or <see langword=\"null\"/>.</param>");
            writer.Line(docs[form]);
            writer.Line("/// <returns>Whether the read succeeded.</returns>");
            string tryParameters = form == 0
                                       ? orderParameter + "out " + type + " value, out " + failureType + "? failure, global::CStructSharp.ReadOptions? options = null"
                                       : orderParameter + "out " + type + " value, out " + failureType + "? failure, in " + limits + " limits";
            writer.Open("public static bool TryParse(global::System.ReadOnlySpan<byte> source, " + tryParameters + ")");
            writer.Line("var cursor = new " + Cursor + "(" + sources[form] + ");");
            writer.Open("try");
            if (constrained)
            {
                writer.Open("if (ReadValue(ref cursor, out value, out var stopped))");
                writer.Line("failure = null;");
                writer.Line("return true;");
                writer.Close();
                writer.Line("cursor.Complete(stopped!);");
                writer.Line("value = default;");
                writer.Line("failure = stopped;");
                writer.Line("return false;");
            }
            else
            {
                writer.Line("value = ReadValue(ref cursor);");
                writer.Line("failure = null;");
                writer.Line("return true;");
            }

            writer.Close();
            writer.Open("catch (" + failureType + " exception)");
            writer.Line("cursor.Complete(exception);");
            writer.Line("value = default;");
            writer.Line("failure = exception;");
            writer.Line("return false;");
            writer.Close();
            writer.Close();
        }
    }
}
