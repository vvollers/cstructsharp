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

/// <summary>Array members: the element count (fixed, from an expression, to the end of the input, or terminated), the runtime's limits, and the element loop or bulk decode.</summary>
internal sealed partial class LayoutEmitter
{
    private readonly HashSet<string> pointerReaders = new(StringComparer.Ordinal);
    private readonly List<CompiledField> pendingPointerReaders = new();

    /// <summary>Emits the read of one array field: its count, then a bulk decode, a text read, or an element loop (addresses only for a deferred pointer array).</summary>
    private void EmitArray(SourceWriter writer, CompiledField field, GeneratedMember generated, ReaderScope scope, string property, bool inUnion, string member, string memberType)
    {
        // The element count, as the runtime derives it before reading.
        writer.Line("int count;");
        switch (field.Array.Kind)
        {
        case CompiledArrayKind.Fixed when field.Array.Dimensions.Length > 1 || field.Array.CountExpression is null:
            {
                int total = field.Array.TotalFixedElementCount ?? throw new InvalidOperationException("Fixed array without a count: " + field.Name);
                writer.Line("count = " + Int(total) + ";");
                if (field.Array.Dimensions.Length > 1)
                {
                    writer.Line("cursor.RequireArrayLength(count, " + member + ", " + memberType + ");");
                }

                break;
            }

        case CompiledArrayKind.Fixed:
        case CompiledArrayKind.Runtime:
            this.EmitCount(writer, field.Name, field.Array.CountExpression!, scope, "array length for " + field.Name, member, memberType, validatedCountIsReturned: true);
            break;
        case CompiledArrayKind.ToEnd:
            writer.Line("count = cursor.CountToEnd(" + Int(field.FixedElementSize ?? 0) + ", " + SourceWriter.Literal(field.Name) + ", " + member + ", " + memberType + ");");
            break;
        case CompiledArrayKind.Terminated:
            writer.Line("count = cursor.CountTerminated(" + Int(field.FixedElementSize ?? 0) + ", " + SourceWriter.Literal(field.Name) + ", " + member + ", " + memberType + ");");
            break;
        case CompiledArrayKind.Flexible:
            // char name[] is a terminated string: the compiled field carries the terminated codec.
            writer.Line(property + " = " + this.TerminatedRead(field, member, memberType) + ";");
            return;
        default:
            throw new InvalidOperationException("Unsupported array kind: " + field.Array.Kind);
        }

        PrimitiveCodec codec = field.Codec;
        if (field.PointerDepth == 0 && field.IsCharacterArray)
        {
            this.EmitCharacterArray(writer, field, property, member, memberType);
        }
        else if (field.PointerDepth == 0 && BoundedTextCodec.IsType(field.TypeSpelling))
        {
            writer.Line(property + " = cursor.TakeEncodedText(count, " + SourceWriter.Literal(field.TypeSpelling) + ", " + member + ", " + memberType + ");");
        }
        else
        {
            string elementType = ElementType(generated.TypeName, field.Array.Dimensions.Length == 0 ? 1 : field.Array.Dimensions.Length);
            bool bulk = field.PointerDepth == 0 && generated.Composite is null && generated.Enum is null && field.Name.Length > 0 && codec.IsFixedWidthNumeric;
            if (bulk)
            {
                writer.Line("var elements = new " + elementType + "[count];");
                writer.Open("if (count > 0)");

                // The runtime's bulk reader: one typed read for a one-dimensional array (its own short-read text), the
                // block reader for a multidimensional one, the per-element loop inside a union.
                string take = inUnion ? "TakeElements" : field.Array.Dimensions.Length > 1 ? "TakeInBlocks" : "TakeArray";
                writer.Line("global::System.ReadOnlySpan<byte> bytes = cursor." + take + "(count, " + Int(codec.Size) + ", " + member + ", " + memberType + ");");
                writer.Line(BulkDecode(codec, elementType, "bytes", "elements"));
                writer.Close();
            }
            else if (!inUnion && this.deferredPointers.TryGetValue(field, out DeferredPointer? deferred))
            {
                // A deferred pointer array takes only its addresses here; the struct reader follows them after its
                // last field, from the recorded first address.
                deferred.Bind(property, member, memberType);
                writer.Line("var elements = new " + elementType + "[count];");
                writer.Line(deferred.Position + " = cursor.Position;");
                writer.Open("for (int index = 0; index < count; index++)");
                writer.Line("elements[index] = " + this.DeferredAddressRead(field, generated, member, memberType) + ";");
                writer.Close();
                writer.Line(deferred.Elements + " = elements;");
            }
            else
            {
                writer.Line("var elements = new " + elementType + "[count];");
                writer.Open("for (int index = 0; index < count; index++)");
                writer.Line("elements[index] = " + this.ScalarRead(field, generated, member, memberType) + ";");
                writer.Close();
            }

            writer.Line(property + " = " + Reshape("elements", field.Array.Dimensions, elementType) + ";");
        }

        if (field.Array.Kind == CompiledArrayKind.Terminated)
        {
            // The all-zero terminator element belongs to the field but not to its value.
            writer.Line("cursor.Skip(" + Int(field.FixedElementSize ?? 0) + ", " + member + ", " + memberType + ");");
        }
    }

    /// <summary>
    ///     Emits the read of a fixed character array as text: one string, or for a multidimensional array a table of
    ///     strings, one per innermost row. Wide characters use the field's explicit byte order or the layout's.
    /// </summary>
    /// <param name="writer">The output.</param>
    /// <param name="field">The character array field.</param>
    /// <param name="property">The expression the text is assigned to.</param>
    /// <param name="member">The member-name expression for failures.</param>
    /// <param name="memberType">The member-type expression for failures.</param>
    private void EmitCharacterArray(SourceWriter writer, CompiledField field, string property, string member, string memberType)
    {
        bool wide = field.IsWideCharElement;
        string littleEndian = Bool(field.ExplicitWideCharacterEncoding is null ? this.request.Settings.LittleEndian : field.Codec.LittleEndian);
        string readRow = wide
                             ? "cursor.TakeWideText(rowLength, " + littleEndian + ", " + member + ", " + memberType + ")"
                             : "cursor.TakeFixedText(rowLength, " + member + ", " + memberType + ")";
        if (field.Array.Dimensions.Length <= 1)
        {
            writer.Line("int rowLength = count;");
            writer.Line(property + " = " + readRow + ";");
            return;
        }

        // A table of fixed text: the innermost dimension is one string, the outer dimensions nest around the rows.
        int rowLength = field.Array.Dimensions[field.Array.Dimensions.Length - 1].FixedCount ?? throw new InvalidOperationException("Character table without a fixed row length: " + field.Name);
        writer.Line("int rowLength = " + Int(rowLength) + ";");
        writer.Line("var rows = new string[count / rowLength];");
        writer.Open("for (int index = 0; index < rows.Length; index++)");
        writer.Line("rows[index] = " + readRow + ";");
        writer.Close();
        writer.Line(property + " = " + Reshape("rows", field.Array.Dimensions.RemoveAt(field.Array.Dimensions.Length - 1), "string") + ";");
    }

    /// <summary>Nests a flat element array into jagged arrays for every dimension but the innermost.</summary>
    private static string Reshape(string flat, System.Collections.Immutable.ImmutableArray<CompiledArrayDimension> dimensions, string elementType)
    {
        if (dimensions.Length <= 1)
        {
            return flat;
        }

        string expression = flat;
        string type = elementType;
        for (int dimension = dimensions.Length - 1; dimension >= 1; dimension--)
        {
            int inner = dimensions[dimension].FixedCount ?? throw new InvalidOperationException("Multidimensional array without a fixed inner dimension.");
            expression = "Split<" + type + ">(" + expression + ", " + Int(inner) + ")";
            type += "[]";
        }

        return expression;
    }

    private static string ElementType(string arrayType, int dimensions)
    {
        string element = arrayType;
        for (int dimension = 0; dimension < dimensions && element.EndsWith("[]", StringComparison.Ordinal); dimension++)
        {
            element = element.Substring(0, element.Length - 2);
        }

        return element;
    }

    /// <summary>The expression that reads a terminated string with the field's encoding and terminator.</summary>
    /// <param name="field">The terminated-text field.</param>
    /// <param name="member">The member-name expression for failures.</param>
    /// <param name="memberType">The member-type expression for failures.</param>
    /// <returns>The read expression.</returns>
    private string TerminatedRead(CompiledField field, string member, string memberType)
    {
        string name = PrimitiveCatalog.CanonicalNames[field.TerminatedCodecId];
        PrimitiveCodec codec = PrimitiveCodec.Resolve(name, this.request.Settings.LittleEndian);
        return "cursor.TakeTerminatedString(" + TerminatedEncoding(codec) + ", " + CharLiteral(codec.Terminator) + ", " + member + ", " + memberType + ")";
    }

    /// <summary>Registers the pointer reader a field needs; the readers are emitted once per (type, depth) after the composites.</summary>
    private void RequirePointerReader(CompiledField field)
    {
        if (this.pointerReaders.Add(PointerReaderName(field)))
        {
            this.pendingPointerReaders.Add(field);
        }
    }

    private void EmitPointerReaders(SourceWriter writer)
    {
        // A depth-2 pointer's target is a depth-1 pointer of the same type: the loop drains what the readers add.
        for (int index = 0; index < this.pendingPointerReaders.Count; index++)
        {
            CompiledField field = this.pendingPointerReaders[index];
            writer.Line();
            this.EmitPointerReader(writer, field);
        }
    }

    /// <summary>
    ///     Emits the two readers of one pointer shape: <c>ReadPointer_*</c> takes the stored address and follows it at
    ///     once (union members and inner pointer levels), and <c>FollowPointer_*</c> follows an address a struct
    ///     reader took earlier and deferred until its last field. A counted target's readers take the evaluated
    ///     <c>@count</c> as a parameter because the count is a struct-level expression.
    /// </summary>
    private void EmitPointerReader(SourceWriter writer, CompiledField field)
    {
        GeneratedMember shape = this.model.DescribePointer(field);

        // A pointer array (`node *items[2]`) shares the reader of one element: strip the array levels from its type.
        string type = ElementType(shape.TypeName, field.Array.Kind == CompiledArrayKind.Scalar ? 0 : Math.Max(1, field.Array.Dimensions.Length));
        string targetType = type.Substring("global::CStructSharp.Generated.Pointer<".Length, type.Length - "global::CStructSharp.Generated.Pointer<".Length - 1);
        bool isVoid = field.PointerDepth == 1 && field.Type.TerminalName == "void";
        bool counted = field.HasCountedTarget;
        string countParameter = counted ? "int count, " : string.Empty;
        string countArgument = counted ? "count, " : string.Empty;
        string fixedTargetSize = FixedPointerTargetSize(field, this.request.Settings.PointerSize);
        string declaration = DescribeDeclaration(field).Replace(" " + field.Name, string.Empty);

        writer.Line("/// <summary>Reads a <c>" + declaration + "</c> pointer: the address, then the target when pointers are followed.</summary>");
        writer.Open("private static " + type + " Read" + PointerReaderName(field) + "(ref " + Cursor + " cursor, " + VariablesType + " variables, " + countParameter + "string? member, string? memberType)");
        writer.Line("long address = cursor.TakePointerAddress(PointerSize, LittleEndian, member ?? " + SourceWriter.Literal(field.Name) + ", memberType);");
        writer.Line("return Follow" + PointerReaderName(field) + "(ref cursor, variables, address, " + countArgument + "member, memberType);");
        writer.Close();
        writer.Line();

        writer.Line("/// <summary>Follows a <c>" + declaration + "</c> pointer whose address was already read, when pointers are followed; the cursor returns to its position.</summary>");
        writer.Open("private static " + type + " Follow" + PointerReaderName(field) + "(ref " + Cursor + " cursor, " + VariablesType + " variables, long address, " + countParameter + "string? member, string? memberType)");
        if (isVoid)
        {
            // A void * is an opaque address: there is nothing typed to read at its target.
            writer.Line("return new " + type + "(address, 1);");
            writer.Close();
            return;
        }

        writer.Open("if (address == 0 || !cursor.FollowsPointers)");
        writer.Line("return new " + type + "(address, " + Int(field.PointerDepth) + ");");
        writer.Close();
        writer.Line("int resume = cursor.EnterPointer(address, " + Int(field.PointerDepth) + ", " + fixedTargetSize + ", " + SourceWriter.Literal(field.TypeSpelling) + ", member ?? " + SourceWriter.Literal(field.Name) + ", memberType);");
        writer.Open("try");
        string read;
        if (field.PointerDepth > 1)
        {
            // A counted target keeps its element type at every level; only a plain character pointer ends in a terminated string.
            string? terminated = !counted && PrimitiveCatalog.CanonicalNames.Length > field.TerminatedCodecId && field.TerminatedCodecId >= 0 ? PrimitiveCatalog.CanonicalNames[field.TerminatedCodecId] : null;
            CompiledField inner = field.SelectPointerTarget(field.PointerDepth - 1, terminated, this.request.Settings.PointerSize);
            this.RequirePointerReader(inner);
            read = "Read" + PointerReaderName(inner) + "(ref cursor, variables, " + countArgument + "member, memberType)";
        }
        else
        {
            read = counted ? this.CountedTargetRead(writer, field, shape, targetType) : this.PointerTargetRead(field, shape, targetType);
        }

        writer.Line("return new " + type + "(address, " + Int(field.PointerDepth) + ", " + read + ", true);");
        writer.Close();
        writer.Open("finally");
        writer.Line("cursor.ExitPointer(resume);");
        writer.Close();
        writer.Close();
    }

    /// <summary>
    ///     The C# expression for the byte budget check of one pointer level: the pointer width above the last level,
    ///     the element size times <c>count</c> for a counted target, the target's fixed size, or <c>null</c> when the
    ///     target has no fixed size.
    /// </summary>
    private static string FixedPointerTargetSize(CompiledField field, int pointerSize)
    {
        if (field.PointerDepth > 1)
        {
            return pointerSize.ToString(CultureInfo.InvariantCulture) + "L";
        }

        if (field.HasCountedTarget)
        {
            return field.Type.Symbol.FixedSize is { } elementSize ? "(long)count * " + Int(elementSize) : "null";
        }

        return field.HasTerminatedCodec || field.Type.Symbol.FixedSize is not { } size ? "null" : size.ToString(CultureInfo.InvariantCulture) + "L";
    }

    /// <summary>
    ///     Emits the read of a counted target's <c>count</c> elements into a local and returns that local: characters
    ///     as one string, fixed-width numbers in one block, and every other element type one at a time, matching
    ///     the runtime's counted-target reader.
    /// </summary>
    private string CountedTargetRead(SourceWriter writer, CompiledField field, GeneratedMember shape, string targetType)
    {
        string member = "member ?? " + SourceWriter.Literal(field.Name);
        const string memberType = "memberType";
        CompiledField element = field.CountedElement(this.request.Settings.PointerSize);
        if (element.IsCharElement || element.IsWideCharElement)
        {
            string littleEndian = Bool(element.ExplicitWideCharacterEncoding is null ? this.request.Settings.LittleEndian : element.Codec.LittleEndian);
            return element.IsWideCharElement
                       ? "cursor.TakeWideText(count, " + littleEndian + ", " + member + ", " + memberType + ")"
                       : "cursor.TakeFixedText(count, " + member + ", " + memberType + ")";
        }

        string elementType = targetType.Substring(0, targetType.Length - 2);
        writer.Line("var elements = new " + elementType + "[count];");
        if (element.Codec.IsFixedWidthNumeric && shape.Enum is null && shape.Composite is null)
        {
            writer.Open("if (count > 0)");
            writer.Line("global::System.ReadOnlySpan<byte> bytes = cursor.TakeArray(count, " + Int(element.Codec.Size) + ", " + member + ", " + memberType + ");");
            writer.Line(BulkDecode(element.Codec, elementType, "bytes", "elements"));
            writer.Close();
            return "elements";
        }

        writer.Open("for (int index = 0; index < count; index++)");
        writer.Line("elements[index] = " + this.PointerTargetRead(field, shape, elementType) + ";");
        writer.Close();
        return "elements";
    }

    /// <summary>The C# expression that reads one pointer target value at the cursor.</summary>
    private string PointerTargetRead(CompiledField field, GeneratedMember shape, string targetType)
    {
        string member = "member ?? " + SourceWriter.Literal(field.Name);
        const string memberType = "memberType";
        if (shape.Composite is not null)
        {
            return "Read" + shape.Composite.Name + "(ref cursor, variables, " + member + ", " + memberType + ")";
        }

        if (field.HasTerminatedCodec)
        {
            return this.TerminatedRead(field, member, memberType);
        }

        if (field.Type.Symbol.IsCustomCodec)
        {
            return "cursor.TakeCustom(CodecInstances.Value[" + Int(this.CodecIndex(field.Type.Symbol.Name)) + "], " + member + ", " + memberType + ")";
        }

        if (shape.Enum is not null)
        {
            // An enum target is stored as its integer type.
            PrimitiveCodec storage = PrimitiveCodec.Resolve(shape.Enum.Compiled.Integer.StorageType, this.request.Settings.LittleEndian);
            return "(" + shape.Enum.Name + ")" + NumericRead(storage, member, memberType);
        }

        PrimitiveCodec target = PrimitiveCodec.Resolve(field.Type.Symbol.Name, this.request.Settings.LittleEndian);
        return this.PrimitiveRead(target, field.Type.Symbol.Name, member, memberType);
    }
}
