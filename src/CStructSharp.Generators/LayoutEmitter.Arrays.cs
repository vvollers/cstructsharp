namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Syntax;

/// <summary>Array members: the element count (fixed, from an expression, to the end of the input, or terminated), the runtime's limits, and the element loop or bulk decode.</summary>
internal sealed partial class LayoutEmitter
{
    private readonly HashSet<string> pointerReaders = new(StringComparer.Ordinal);
    private readonly List<CompiledField> pendingPointerReaders = new();

    private void EmitArray(SourceWriter writer, CompiledField field, GeneratedMember generated, ReaderScope scope, string property, bool inUnion, string member, string memberType)
    {
        // The element count, as the runtime derives it before reading.
        writer.Line("int count;");
        switch (field.Array.Kind)
        {
        case CompiledArrayKind.Fixed when field.Array.Dimensions.Length > 1 || field.Array.CountExpression is null:
            {
                int total = field.Array.TotalFixedElementCount ?? field.FixedArrayCount ?? throw new InvalidOperationException("Fixed array without a count: " + field.Name);
                writer.Line("count = " + Int(total) + ";");
                if (field.Array.Dimensions.Length > 1)
                {
                    writer.Line("cursor.RequireArrayLength(count, " + member + ", " + memberType + ");");
                }

                break;
            }

        case CompiledArrayKind.Fixed:
        case CompiledArrayKind.Runtime:
            this.EmitExpression(writer, field.Array.CountExpression!, scope, "count", "array length for " + field.Name, member, memberType, "int");
            if (!ExpressionEmitter.IsInt32Literal(field.Array.CountExpression!))
            {
                writer.Open("if (count < 0)");
                writer.Line("throw cursor.Fail(" + SourceWriter.Literal("Array length cannot be negative: " + field.Name) + ", " + member + ", " + memberType + ");");
                writer.Close();
            }

            writer.Line("cursor.RequireArrayLength(count, " + member + ", " + memberType + ");");
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
                writer.Line(BulkDecode(codec, elementType));
                writer.Close();
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

    private void EmitCharacterArray(SourceWriter writer, CompiledField field, string property, string member, string memberType)
    {
        bool wide = field.IsWideCharElement;
        string littleEndian = Bool(field.ExplicitWideCharacterEncoding is null ? this.request.LittleEndian : field.Codec.LittleEndian);
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

    /// <summary>Builds a bulk array decoder using a writable destination on both older and newer compiler hosts.</summary>
    /// <param name="codec">The element codec, including its byte order.</param>
    /// <param name="elementType">The generated C# element type.</param>
    /// <returns>A statement that decodes bytes into the existing elements array.</returns>
    private static string BulkDecode(PrimitiveCodec codec, string elementType)
    {
        string le = Bool(codec.LittleEndian);
        return codec.Kind switch
        {
            PrimitiveCodecKind.UInt8 => "bytes.CopyTo(elements);",

            // An explicit writable span avoids C# 14 preferring the ReadOnlySpan overload for an array.
            PrimitiveCodecKind.Int8 => "bytes.CopyTo(global::System.Runtime.InteropServices.MemoryMarshal.AsBytes<sbyte>(new global::System.Span<sbyte>(elements)));",
            PrimitiveCodecKind.Bool => CodecClass + ".DecodeBooleans(bytes, elements);",
            PrimitiveCodecKind.Int24 => CodecClass + ".DecodeInt24(bytes, elements, " + le + ");",
            PrimitiveCodecKind.UInt24 => CodecClass + ".DecodeUInt24(bytes, elements, " + le + ");",
            _ => CodecClass + ".DecodeIntegers<" + elementType + ">(bytes, elements, " + le + ");",
        };
    }

    private string TerminatedRead(CompiledField field, string member, string memberType)
    {
        string name = PrimitiveCatalog.CanonicalNames[field.TerminatedCodecId];
        PrimitiveCodec codec = PrimitiveCodec.Resolve(name, this.request.LittleEndian);
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

    private void EmitPointerReader(SourceWriter writer, CompiledField field)
    {
        GeneratedMember shape = this.model.DescribePointer(field);
        string type = shape.TypeName;
        string targetType = type.Substring("global::CStructSharp.Generated.Pointer<".Length, type.Length - "global::CStructSharp.Generated.Pointer<".Length - 1);
        bool isVoid = field.PointerDepth == 1 && field.Type.TerminalName == "void";
        long? fixedTargetSize = field.PointerDepth > 1 ? this.request.PointerSize : field.HasTerminatedCodec ? null : field.Type.Symbol.FixedSize;
        writer.Line("/// <summary>Reads a <c>" + DescribeDeclaration(field).Replace(" " + field.Name, string.Empty) + "</c> pointer: the address, then the target when pointers are followed.</summary>");
        writer.Open("private static " + type + " Read" + PointerReaderName(field) + "(ref " + Cursor + " cursor, " + VariablesType + " variables, string? member, string? memberType)");
        writer.Line("long address = cursor.TakePointerAddress(PointerSize, LittleEndian, member ?? " + SourceWriter.Literal(field.Name) + ", memberType);");
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
        writer.Line("int resume = cursor.EnterPointer(address, " + Int(field.PointerDepth) + ", " + (fixedTargetSize is { } size ? size.ToString(CultureInfo.InvariantCulture) + "L" : "null") + ", " + SourceWriter.Literal(field.TypeSpelling) + ", member ?? " + SourceWriter.Literal(field.Name) + ", memberType);");
        writer.Open("try");
        string read;
        if (field.PointerDepth > 1)
        {
            CompiledField inner = field.SelectPointerTarget(field.PointerDepth - 1, PrimitiveCatalog.CanonicalNames.Length > field.TerminatedCodecId && field.TerminatedCodecId >= 0 ? PrimitiveCatalog.CanonicalNames[field.TerminatedCodecId] : null, this.request.PointerSize);
            this.RequirePointerReader(inner);
            read = "Read" + PointerReaderName(inner) + "(ref cursor, variables, member, memberType)";
        }
        else
        {
            read = this.PointerTargetRead(field, shape, targetType);
        }

        writer.Line("return new " + type + "(address, " + Int(field.PointerDepth) + ", " + read + ", true);");
        writer.Close();
        writer.Open("finally");
        writer.Line("cursor.ExitPointer(resume);");
        writer.Close();
        writer.Close();
    }

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
            PrimitiveCodec storage = PrimitiveCodec.Resolve(shape.Enum.Compiled.Integer.StorageType, this.request.LittleEndian);
            return "(" + shape.Enum.Name + ")" + this.NumericRead(storage, member, memberType);
        }

        PrimitiveCodec target = PrimitiveCodec.Resolve(field.Type.Symbol.Name, this.request.LittleEndian);
        return this.PrimitiveRead(target, field.Type.Symbol.Name, member, memberType);
    }
}
