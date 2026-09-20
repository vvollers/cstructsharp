namespace CStructSharp.Generators;

using System.Globalization;
using System.Numerics;
using CStructSharp.Compilation;

/// <summary>The types: one C# enum per layout enum, one class per struct or union, properties typed per §1.4.</summary>
internal sealed partial class LayoutEmitter
{
    private void EmitTypes(SourceWriter writer)
    {
        foreach (GeneratedEnum generatedEnum in this.model.Enums)
        {
            writer.Line();
            EmitEnum(writer, generatedEnum);
        }

        foreach (GeneratedComposite composite in this.model.Composites)
        {
            writer.Line();
            this.EmitComposite(writer, composite);
        }
    }

    private static void EmitEnum(SourceWriter writer, GeneratedEnum generatedEnum)
    {
        writer.Line("/// <summary>The layout " + (generatedEnum.Compiled.IsFlag ? "flag" : "enum") + " <c>" + generatedEnum.LayoutName + "</c>, stored as <c>" + generatedEnum.Compiled.Integer.StorageType + "</c>. A stored value with no member keeps its number.</summary>");
        if (generatedEnum.Compiled.IsFlag)
        {
            writer.Line("[global::System.Flags]");
        }

        writer.Open("public enum " + generatedEnum.Name + " : " + generatedEnum.UnderlyingType);
        foreach (GeneratedEnumMember member in generatedEnum.Members)
        {
            if (member.Name != member.LayoutName)
            {
                writer.Line("/// <summary><c>" + member.LayoutName + "</c>.</summary>");
            }

            writer.Line(member.Name + " = " + EnumLiteral(member.Value, generatedEnum.UnderlyingType) + ",");
        }

        writer.Close();
    }

    private static string EnumLiteral(BigInteger value, string underlyingType)
    {
        string text = value.ToString(CultureInfo.InvariantCulture);
        return underlyingType switch
        {
            "ulong" => text + "UL",
            "long" => text + "L",
            "uint" => text + "U",
            _ => text,
        };
    }

    private void EmitComposite(SourceWriter writer, GeneratedComposite composite)
    {
        string kind = composite.IsUnion ? "union" : "struct";
        writer.Line("/// <summary>The layout " + kind + " <c>" + composite.LayoutName + "</c>" + (composite.IsDeclared ? string.Empty : " (declared inline)") + ".</summary>");
        writer.Open("public sealed partial class " + composite.Name);
        bool first = true;
        if (composite.IsUnion)
        {
            writer.Line("/// <summary>The member a write stores, or <see langword=\"null\"/> for the first member with a value; a read leaves it <see langword=\"null\"/> because an untagged union does not say which view is active.</summary>");
            writer.Line("public string? SelectedMember { get; set; }");
            writer.Line();
            writer.Line("/// <summary>The union's stored bytes as read; a write with <see cref=\"SelectedMember\"/> unset and no member value writes them back.</summary>");
            writer.Line("public byte[]? RawStorage { get; set; }");
            first = false;
        }

        foreach (GeneratedMember member in composite.Members)
        {
            if (!first)
            {
                writer.Line();
            }

            first = false;
            writer.Line("/// <summary><c>" + DescribeDeclaration(member.Field) + "</c>" + (member.IsConditional ? " (conditional)" : string.Empty) + ".</summary>");
            writer.Line("public " + member.TypeName + " " + member.PropertyName + " { get; set; }" + Initializer(member));
        }

        writer.Close();
    }

    /// <summary>The field as the layout spells it: type, name, array suffixes, bit width.</summary>
    private static string DescribeDeclaration(CompiledField field)
    {
        string text = field.TypeSpelling + " ";
        for (int depth = 0; depth < field.PointerDepth; depth++)
        {
            text += "*";
        }

        text += field.Name;
        foreach (CompiledArrayDimension dimension in field.Array.Dimensions)
        {
            text += dimension.FixedCount is { } count ? "[" + count.ToString(CultureInfo.InvariantCulture) + "]" : "[...]";
        }

        if (field.Array.Kind == CompiledArrayKind.ToEnd)
        {
            text += "[EOF]";
        }
        else if (field.Array.Kind is CompiledArrayKind.Flexible or CompiledArrayKind.Terminated && field.Array.Dimensions.Length == 0)
        {
            text += "[]";
        }

        if (field.BitSize > 0)
        {
            text += " : " + field.BitSize.ToString(CultureInfo.InvariantCulture);
        }

        return text;
    }

    /// <summary>Reference-typed properties start non-null so a freshly constructed value serializes without a null check per member.</summary>
    private static string Initializer(GeneratedMember member)
    {
        string type = member.TypeName;
        if (type.EndsWith("[]", System.StringComparison.Ordinal))
        {
            return " = global::System.Array.Empty<" + type.Substring(0, type.Length - 2) + ">();";
        }

        if (type == "string")
        {
            return " = string.Empty;";
        }

        if (member.Composite is not null && member.Field.PointerDepth == 0)
        {
            return " = new();";
        }

        return string.Empty;
    }
}
