namespace CStructSharp.Generators;

using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using static CStructSharp.Generators.Emit;

/// <summary>
///     <c>Shape = Struct</c>: every struct of the layout becomes a C# <c>struct</c> whose members are properties, so a read
///     allocates no object for the value. A fixed one-dimensional array of numbers is a generated inline array type
///     (<c>InlineArrays.ByteArray4</c>), so it needs no <c>T[]</c> either. Only layouts whose every member has a fixed
///     size and no reference-typed or data-dependent shape qualify; the others are reported as CSG014.
/// </summary>
internal sealed partial class LayoutEmitter
{
    /// <summary>The nested class that holds the generated inline array types.</summary>
    private const string InlineArraysClass = "InlineArrays";

    /// <summary>Gets a value indicating whether the layout's structs are emitted as value types.</summary>
    private bool UsesValueTypes => this.request.ValueTypes && this.request.Surface is GenerationSurface.Reader or GenerationSurface.Parser;

    /// <summary>
    ///     Why the layout cannot be generated as value types, or <see langword="null"/> when it can: the surface writes or
    ///     has no types, or a struct holds a union, a conditional member, a pointer, a runtime-sized array, a terminated
    ///     string, a custom codec or a shape (a table, an array of structs or enums) that a value type cannot hold here.
    /// </summary>
    /// <returns>The reason, as the end of the sentence "'Class' sets Shape = Struct, which ...", or <see langword="null"/>.</returns>
    private string? ValueTypeProblem()
    {
        if (this.request.Surface == GenerationSurface.Full)
        {
            return "needs the Reader or Parser surface: the full surface's writers, Update setters and Records need classes";
        }

        if (this.request.Surface == GenerationSurface.Views)
        {
            return "has nothing to shape on the Views surface, which generates no struct or class";
        }

        foreach (GeneratedComposite composite in this.model.Composites)
        {
            if (composite.IsUnion)
            {
                return "cannot hold the union '" + composite.LayoutName + "' (an untagged union has no value-type form)";
            }

            foreach (GeneratedMember member in composite.Members)
            {
                if (ValueTypeMemberProblem(member) is { } problem)
                {
                    return "cannot hold '" + composite.LayoutName + "." + member.Field.Name + "': " + problem;
                }
            }

            if (composite.Composite.Symbol.FixedSize is null)
            {
                return "needs fixed-size structs, but '" + composite.LayoutName + "' has a size that depends on its data";
            }
        }

        return null;
    }

    /// <summary>Why one member does not fit a value type, or <see langword="null"/> when it does.</summary>
    /// <param name="member">The member.</param>
    /// <returns>The reason, or <see langword="null"/>.</returns>
    private static string? ValueTypeMemberProblem(GeneratedMember member)
    {
        CompiledField field = member.Field;
        if (member.IsConditional)
        {
            return "it is conditional";
        }

        if (field.PointerDepth > 0)
        {
            return "it is a pointer";
        }

        if (field.Type.Symbol.IsCustomCodec)
        {
            return "it uses a custom codec";
        }

        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            if (member.Composite is not null || member.Enum is not null || field.BitSize > 0)
            {
                return null;
            }

            return field.Codec.IsFixedWidthNumeric || field.Codec.IsIdentifier || field.Codec.IsFixedPoint ? null : "its type has no fixed size";
        }

        if (field.Array.Kind != CompiledArrayKind.Fixed || field.Array.Dimensions.Length != 1 || field.Array.FixedCount is not { } count)
        {
            return "it is an array whose length or shape is not a single fixed count";
        }

        if (field.IsCharacterArray || BoundedTextCodec.IsType(field.TypeSpelling))
        {
            return null;
        }

        if (member.Composite is not null || member.Enum is not null)
        {
            return "it is an array of " + (member.Composite is not null ? "structs" : "enums") + " (only arrays of numbers are supported)";
        }

        if (count < 1)
        {
            return "it is an array of zero elements";
        }

        return field.Codec.IsFixedWidthNumeric && field.Codec.Kind != PrimitiveCodecKind.Int8 ? null : "it is an array of " + field.TypeSpelling + " (only arrays of numbers are supported)";
    }

    /// <summary>The inline array type that holds a member's array when the layout is emitted as value types, or <see langword="null"/> for any other member.</summary>
    /// <param name="member">The member.</param>
    /// <returns>The type, such as <c>InlineArrays.ByteArray4</c>.</returns>
    private string? InlineArrayOf(GeneratedMember member)
    {
        if (!this.UsesValueTypes)
        {
            return null;
        }

        CompiledField field = member.Field;
        bool numericArray = field.PointerDepth == 0 && field.Array.Kind == CompiledArrayKind.Fixed && field.Array.Dimensions.Length == 1 && field.Array.FixedCount is > 0
                            && member.Composite is null && member.Enum is null && !field.IsCharacterArray && !BoundedTextCodec.IsType(field.TypeSpelling)
                            && field.Codec.IsFixedWidthNumeric;
        return numericArray ? InlineArraysClass + "." + InlineArrayName(ElementType(member.TypeName, 1), field.Array.FixedCount!.Value) : null;
    }

    /// <summary>The name of the inline array type of an element type and length: <c>ByteArray4</c>, <c>UInt16Array3</c>.</summary>
    /// <param name="elementType">The C# element type.</param>
    /// <param name="length">The element count.</param>
    /// <returns>The type's name.</returns>
    private static string InlineArrayName(string elementType, int length) => ClrName(elementType) + "Array" + length.ToString(CultureInfo.InvariantCulture);

    /// <summary>The CLR name of a C# numeric keyword, so a type is named by what it holds.</summary>
    /// <param name="keyword">The C# type.</param>
    /// <returns>The CLR type name.</returns>
    private static string ClrName(string keyword) => keyword switch
    {
        "byte" => "Byte",
        "sbyte" => "SByte",
        "short" => "Int16",
        "ushort" => "UInt16",
        "int" => "Int32",
        "uint" => "UInt32",
        "long" => "Int64",
        "ulong" => "UInt64",
        "float" => "Single",
        "double" => "Double",
        "bool" => "Boolean",
        "char" => "Char",
        _ => keyword.Substring(keyword.LastIndexOf('.') + 1),
    };

    /// <summary>Emits the inline array types the layout's structs use, once each, in a nested class.</summary>
    /// <param name="writer">The output.</param>
    private void EmitInlineArrayTypes(SourceWriter writer)
    {
        var needed = new SortedDictionary<string, (string Element, int Length)>(System.StringComparer.Ordinal);
        foreach (GeneratedComposite composite in this.model.Composites)
        {
            foreach (GeneratedMember member in composite.Members)
            {
                if (this.InlineArrayOf(member) is not null)
                {
                    string element = ElementType(member.TypeName, 1);
                    int length = member.Field.Array.FixedCount!.Value;
                    needed[InlineArrayName(element, length)] = (element, length);
                }
            }
        }

        if (needed.Count == 0)
        {
            return;
        }

        writer.Line();
        writer.Line("/// <summary>The inline array types of the layout's fixed arrays of numbers: a value-type array of a fixed length that lives inside its struct.</summary>");
        writer.Open("public static class " + InlineArraysClass);
        bool first = true;
        foreach (KeyValuePair<string, (string Element, int Length)> type in needed)
        {
            if (!first)
            {
                writer.Line();
            }

            first = false;
            string length = type.Value.Length.ToString(CultureInfo.InvariantCulture);
            writer.Line("/// <summary>" + length + " elements of <c>" + type.Value.Element + "</c> stored inline in the struct that holds them.</summary>");
            writer.Line("[global::System.Runtime.CompilerServices.InlineArray(" + length + ")]");
            writer.Open("public struct " + type.Key);
            writer.Line("private " + type.Value.Element + " element0;");
            writer.Line();
            writer.Line("/// <summary>Gets the number of elements.</summary>");
            writer.Line("public const int Length = " + length + ";");
            writer.Line();
            writer.Line("/// <summary>Views the elements as a span over this value's own storage.</summary>");
            writer.Line("/// <returns>A span of every element; it is valid as long as this value is.</returns>");
            writer.Line("[global::System.Diagnostics.CodeAnalysis.UnscopedRef]");
            writer.Line("public global::System.Span<" + type.Value.Element + "> AsSpan() => global::System.Runtime.InteropServices.MemoryMarshal.CreateSpan(ref this.element0, Length);");
            writer.Line();
            writer.Line("/// <summary>Copies the elements into a new array.</summary>");
            writer.Line("/// <returns>The array.</returns>");
            writer.Line("public " + type.Value.Element + "[] ToArray() => this.AsSpan().ToArray();");
            writer.Close();
        }

        writer.Close();
    }
}
