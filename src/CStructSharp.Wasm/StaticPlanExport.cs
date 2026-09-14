namespace CStructSharpWeb.Wasm;

using System;
using System.Globalization;
using System.Numerics;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using CStructSharp;
using CStructSharp.Structure;

/// <summary>
///     E3.9: describes a fully fixed root composite (its static read plan, E2.5) as JSON so the JavaScript side can
///     read such layouts with <c>DataView</c> alone. The description is the compiler's own operation list — offsets,
///     codecs, counts, nested plans, enum member tables — not a second grammar; the JavaScript executor reproduces
///     the JSON projection's value shapes and falls back to WebAssembly for anything the plan does not cover.
/// </summary>
[SupportedOSPlatform("browser")]
public partial class CStructExports
{
    private const long MaximumSafeInteger = 9_007_199_254_740_991;

    /// <summary>
    ///     Returns the static read plan of the selected root as JSON, or an empty string when the root is not a fully
    ///     fixed struct, its plan exceeds the default read limits, or the definition does not compile (compilation
    ///     failures are left to the parse itself so the error envelope is unchanged).
    /// </summary>
    [JSExport]
    public static string GetStaticPlan(string definition, string optionsJson)
    {
        try
        {
            InteropOptionsDto options = ParseOptions(optionsJson);
            CStruct cstruct = CreateCStruct(definition, options);
            string root = string.IsNullOrWhiteSpace(options.RootTypeName)
                              ? ResolveDefaultRootTypeName(cstruct)
                              : options.RootTypeName;
            return DescribeStaticPlan(cstruct, root) ?? string.Empty;
        }
        catch (Exception exception) when (exception is CStructException or ArgumentException or InvalidOperationException)
        {
            return string.Empty;
        }
    }

    internal static string? DescribeStaticPlan(CStruct cstruct, string root)
    {
        if (!cstruct.CompiledModel.Symbols.TryGetValue(root, out CompiledTypeReference entry) ||
            entry.Symbol.Definition is not CompiledCompositeType composite ||
            composite.Symbol.Declaration is not Struct { IsUnion: false } ||
            composite.StaticPlan is not StaticReadPlan plan)
        {
            return null;
        }

        var defaults = new ReadOptions();
        if (plan.NestingDepth > defaults.MaxNestingDepth || plan.MaximumArrayCount > defaults.MaxArrayElements ||
            plan.Size > defaults.MaxTotalBytesRead)
        {
            return null;
        }

        var builder = new StringBuilder();
        builder.Append("{\"root\":");
        AppendString(builder, root);
        builder.Append(",\"plan\":");
        AppendPlan(builder, plan, cstruct);
        builder.Append('}');
        return builder.ToString();
    }

    private static void AppendPlan(StringBuilder builder, StaticReadPlan plan, CStruct cstruct)
    {
        builder.Append("{\"size\":").Append(plan.Size.ToString(CultureInfo.InvariantCulture)).Append(",\"ops\":[");
        bool first = true;
        foreach (StaticReadOperation operation in plan.Operations)
        {
            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            builder.Append("{\"name\":");
            AppendString(builder, operation.Field.Declaration.Name.Name);
            builder.Append(",\"o\":").Append(operation.Offset.ToString(CultureInfo.InvariantCulture));
            switch (operation.Kind)
            {
            case StaticReadKind.Numeric:
                builder.Append(",\"k\":\"n\"");
                AppendCodec(builder, operation.Field.Codec);
                break;
            case StaticReadKind.Enum:
                builder.Append(",\"k\":\"e\"");
                AppendCodec(builder, operation.Field.Codec);
                AppendEnum(builder, operation.Field, cstruct);
                break;
            case StaticReadKind.CharArray:
                builder.Append(",\"k\":\"c\",\"n\":").Append(operation.Count.ToString(CultureInfo.InvariantCulture));
                break;
            case StaticReadKind.NumericArray:
                builder.Append(",\"k\":\"a\",\"n\":").Append(operation.Count.ToString(CultureInfo.InvariantCulture));
                AppendCodec(builder, operation.Field.Codec);
                break;
            case StaticReadKind.Nested:
                builder.Append(",\"k\":\"s\",\"p\":");
                AppendPlan(builder, operation.NestedPlan!, cstruct);
                break;
            case StaticReadKind.NestedArray:
                builder.Append(",\"k\":\"sa\",\"n\":").Append(operation.Count.ToString(CultureInfo.InvariantCulture)).Append(",\"p\":");
                AppendPlan(builder, operation.NestedPlan!, cstruct);
                break;
            default:
                throw new InvalidOperationException("Unknown static read operation kind: " + operation.Kind);
            }

            builder.Append('}');
        }

        builder.Append("]}");
    }

    private static void AppendCodec(StringBuilder builder, PrimitiveCodec codec)
    {
        string kind = codec.Kind switch
        {
            PrimitiveCodecKind.UInt8 => "u8",
            PrimitiveCodecKind.Int8 => "i8",
            PrimitiveCodecKind.Bool => "bool",
            PrimitiveCodecKind.Int16 => "i16",
            PrimitiveCodecKind.UInt16 => "u16",
            PrimitiveCodecKind.Int24 => "i24",
            PrimitiveCodecKind.UInt24 => "u24",
            PrimitiveCodecKind.Int32 => "i32",
            PrimitiveCodecKind.UInt32 => "u32",
            PrimitiveCodecKind.Int64 => "i64",
            PrimitiveCodecKind.UInt64 => "u64",
            PrimitiveCodecKind.Float32 => "f32",
            PrimitiveCodecKind.Float64 => "f64",
            _ => throw new InvalidOperationException("Codec is not a fixed-width numeric: " + codec.Kind),
        };
        builder.Append(",\"t\":\"").Append(kind).Append("\",\"le\":").Append(codec.LittleEndian ? "true" : "false");
    }

    /// <summary>The enum's name and its members as the values the projection writes (safe integers as numbers, larger ones as decimal strings), first member per raw bits.</summary>
    private static void AppendEnum(StringBuilder builder, CompiledField field, CStruct cstruct)
    {
        var declaration = (CStructSharp.Structure.Enum)field.Type.Symbol.Declaration!;
        CompiledEnumType compiled = cstruct.GetCompiledEnumForInterop(declaration);
        builder.Append(",\"enum\":");
        AppendString(builder, declaration.Name.Name);
        builder.Append(",\"members\":[");
        var seen = new System.Collections.Generic.HashSet<ulong>();
        bool first = true;
        foreach (CompiledEnumMember member in compiled.Members)
        {
            if (!seen.Add(member.RawBits))
            {
                continue;
            }

            if (!first)
            {
                builder.Append(',');
            }

            first = false;
            builder.Append("{\"v\":");
            AppendSafeInteger(builder, compiled.Integer.FromRawBits(member.RawBits));
            builder.Append(",\"n\":");
            AppendString(builder, member.Name);
            builder.Append('}');
        }

        builder.Append(']');
    }

    private static void AppendSafeInteger(StringBuilder builder, BigInteger value)
    {
        if (value >= -MaximumSafeInteger && value <= MaximumSafeInteger)
        {
            builder.Append(value.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            AppendString(builder, value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AppendString(StringBuilder builder, string value)
    {
        builder.Append('"');
        foreach (char character in value)
        {
            switch (character)
            {
            case '"':
                builder.Append("\\\"");
                break;
            case '\\':
                builder.Append("\\\\");
                break;
            case < ' ':
                builder.Append("\\u").Append(((int)character).ToString("X4", CultureInfo.InvariantCulture));
                break;
            default:
                builder.Append(character);
                break;
            }
        }

        builder.Append('"');
    }
}
