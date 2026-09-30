namespace CStructSharpWeb.Wasm;

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using CStructSharp;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Syntax;

/// <summary>
///     Describes a fully fixed root composite (its static read plan) as JSON so the JavaScript side can
///     read such layouts with <c>DataView</c> alone. The description is the compiler's own operation list — offsets,
///     codecs, counts, nested plans, enum member tables — not a second grammar; the JavaScript executor reproduces
///     the JSON projection's value shapes and falls back to WebAssembly for anything the plan does not cover.
/// </summary>
[SupportedOSPlatform("browser")]
public partial class CStructExports
{
    /// <summary>
    ///     Returns the static read plan of the selected root in a <c>staticPlan</c> envelope. The envelope's
    ///     <c>data</c> is <c>{"root": ..., "plan": ...}</c> when the root has a plan, and null when the root is not a
    ///     fully fixed struct, its plan exceeds the read limits, or the layout or root fails with a library, argument,
    ///     or invalid-operation error (compilation failures are left to the parse itself, which reports them in its
    ///     own error envelope).
    /// </summary>
    /// <param name="definition">The CStruct layout definition text.</param>
    /// <param name="optionsJson">
    ///     The JSON options object (<see cref="InteropOptionsDto"/>); its <c>root</c> selects the struct to describe
    ///     and its read limits decide whether the plan is covered.
    /// </param>
    /// <returns>
    ///     The JSON text of the <c>staticPlan</c> envelope. Malformed options JSON and rejected browser input (an empty
    ///     or oversized definition, an out-of-range option) produce a failure envelope with the usual error codes.
    /// </returns>
    [JSExport]
    public static string GetStaticPlan(string definition, string optionsJson)
    {
        InteropOptionsDto? options = null;
        CStruct cstruct;
        string root;
        StaticReadPlan? plan;
        try
        {
            options = ParseOptions(optionsJson);
            cstruct = CreateCStruct(definition, options);
            root = ResolveRoot(cstruct, options);
            plan = FindStaticPlan(cstruct, root, CreateReadOptions(options));
        }
        catch (Exception exception) when (exception is CStructException or ArgumentException or InvalidOperationException)
        {
            // The layout has no describable plan; the parse itself runs on the engine and reports any error.
            return SerializeNoStaticPlan(options);
        }
        catch (Exception exception)
        {
            return SerializeFailure("staticPlan", exception, options);
        }

        if (plan is null)
        {
            return SerializeNoStaticPlan(options, root);
        }

        try
        {
            InteropJsonWriter writer = StartEnvelope("staticPlan", success: true, root);
            writer.WriteRawBytes("{\"root\":"u8);
            writer.WriteString(root);
            writer.WriteRawBytes(",\"plan\":"u8);
            WritePlan(writer, plan, cstruct);
            writer.WriteRawBytes("}"u8);
            return FinishEnvelope(writer);
        }
        catch (InvalidOperationException)
        {
            // A plan operation or codec this bridge cannot describe; the next envelope resets the partial output.
            return SerializeNoStaticPlan(options, root);
        }
    }

    /// <summary>
    ///     The static read plan of <paramref name="root"/>, or <see langword="null"/> when the root is not a fully
    ///     fixed struct or the read's own limits do not cover the plan (the parse then runs on the engine).
    /// </summary>
    /// <param name="cstruct">The compiled layout.</param>
    /// <param name="root">The root declaration.</param>
    /// <param name="options">The read's options, as the parse will use them.</param>
    /// <returns>The plan, or <see langword="null"/>.</returns>
    private static StaticReadPlan? FindStaticPlan(CStruct cstruct, string root, ReadOptions options)
    {
        if (!cstruct.CompiledModel.Symbols.TryGetValue(root, out CompiledTypeReference entry) ||
            entry.Symbol.Definition is not CompiledCompositeType composite ||
            composite.Symbol.Declaration is not Struct { IsUnion: false } ||
            composite.StaticPlan is not StaticReadPlan plan)
        {
            return null;
        }

        return ReadOperationSettings.SnapshotReadOptions(options).CoversPlan(plan) ? plan : null;
    }

    /// <summary>Writes the successful <c>staticPlan</c> envelope of a layout without a describable plan.</summary>
    /// <param name="options">The parsed options, or null when they could not be read.</param>
    /// <param name="root">The resolved root, or null to echo the <c>root</c> option.</param>
    /// <returns>The envelope, whose <c>data</c> is null.</returns>
    private static string SerializeNoStaticPlan(InteropOptionsDto? options, string? root = null)
    {
        InteropJsonWriter writer = StartEnvelope("staticPlan", success: true, root ?? options?.Root);
        writer.WriteNull();
        return FinishEnvelope(writer);
    }

    /// <summary>
    ///     Writes one plan as <c>{"size": bytes, "ops": [...]}</c>. Each operation has its field <c>name</c>, byte
    ///     offset <c>o</c> within the struct, and kind <c>k</c>: <c>n</c> numeric, <c>e</c> enum, <c>c</c> character
    ///     array, <c>a</c> numeric array, <c>s</c> nested struct, <c>sa</c> nested struct array; counts are <c>n</c>
    ///     elements, nested plans <c>p</c>.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="plan">The plan.</param>
    /// <param name="cstruct">The compiled layout, which resolves enum member tables.</param>
    /// <exception cref="InvalidOperationException">The plan contains an operation kind or codec the bridge cannot describe.</exception>
    private static void WritePlan(InteropJsonWriter writer, StaticReadPlan plan, CStruct cstruct)
    {
        writer.WriteRawBytes("{\"size\":"u8);
        writer.WriteSafeInteger(plan.Size);
        writer.WriteRawBytes(",\"ops\":["u8);
        bool first = true;
        foreach (StaticReadOperation operation in plan.Operations)
        {
            writer.WriteRawBytes(first ? "{\"name\":"u8 : ",{\"name\":"u8);
            first = false;
            writer.WriteString(operation.Field.Declaration.Name.Name);
            writer.WriteRawBytes(",\"o\":"u8);
            writer.WriteSafeInteger(operation.Offset);
            switch (operation.Kind)
            {
            case StaticReadKind.Numeric:
                writer.WriteRawBytes(",\"k\":\"n\""u8);
                WriteCodec(writer, operation.Field.Codec);
                break;
            case StaticReadKind.Enum:
                writer.WriteRawBytes(",\"k\":\"e\""u8);
                WriteCodec(writer, operation.Field.Codec);
                WriteEnum(writer, operation.Field, cstruct);
                break;
            case StaticReadKind.CharArray:
                writer.WriteRawBytes(",\"k\":\"c\",\"n\":"u8);
                writer.WriteSafeInteger(operation.Count);
                break;
            case StaticReadKind.NumericArray:
                writer.WriteRawBytes(",\"k\":\"a\",\"n\":"u8);
                writer.WriteSafeInteger(operation.Count);
                WriteCodec(writer, operation.Field.Codec);
                break;
            case StaticReadKind.Nested:
                writer.WriteRawBytes(",\"k\":\"s\",\"p\":"u8);
                WritePlan(writer, operation.NestedPlan!, cstruct);
                break;
            case StaticReadKind.NestedArray:
                writer.WriteRawBytes(",\"k\":\"sa\",\"n\":"u8);
                writer.WriteSafeInteger(operation.Count);
                writer.WriteRawBytes(",\"p\":"u8);
                WritePlan(writer, operation.NestedPlan!, cstruct);
                break;
            default:
                throw new InvalidOperationException("Unknown static read operation kind: " + operation.Kind);
            }

            writer.WriteRawBytes("}"u8);
        }

        writer.WriteRawBytes("]}"u8);
    }

    /// <summary>Writes a fixed-width numeric codec as its type code <c>t</c> and byte order <c>le</c>.</summary>
    /// <param name="writer">The writer.</param>
    /// <param name="codec">The field's codec.</param>
    /// <exception cref="InvalidOperationException">The codec is not a fixed-width numeric.</exception>
    private static void WriteCodec(InteropJsonWriter writer, PrimitiveCodec codec)
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
        writer.WriteRawBytes(",\"t\":"u8);
        writer.WriteString(kind);
        writer.WriteRawBytes(",\"le\":"u8);
        writer.WriteBoolean(codec.LittleEndian);
    }

    /// <summary>
    ///     Writes the enum's name and its members as the values the projection writes (safe integers as numbers, larger
    ///     ones as decimal strings), keeping the first member per raw bit pattern.
    /// </summary>
    /// <param name="writer">The writer.</param>
    /// <param name="field">The enum field.</param>
    /// <param name="cstruct">The compiled layout that owns the enum.</param>
    private static void WriteEnum(InteropJsonWriter writer, CompiledField field, CStruct cstruct)
    {
        var declaration = (CStructSharp.Syntax.Enum)field.Type.Symbol.Declaration!;
        CompiledEnumType compiled = cstruct.GetCompiledEnumForInterop(declaration);
        writer.WriteRawBytes(",\"enum\":"u8);
        writer.WriteString(declaration.Name.Name);
        writer.WriteRawBytes(",\"members\":["u8);
        var seen = new HashSet<ulong>();
        bool first = true;
        foreach (CompiledEnumMember member in compiled.Members)
        {
            if (!seen.Add(member.RawBits))
            {
                continue;
            }

            writer.WriteRawBytes(first ? "{\"v\":"u8 : ",{\"v\":"u8);
            first = false;
            writer.WriteSafeInteger(compiled.Integer.FromRawBits(member.RawBits));
            writer.WriteRawBytes(",\"n\":"u8);
            writer.WriteString(member.Name);
            writer.WriteRawBytes("}"u8);
        }

        writer.WriteRawBytes("]"u8);
    }
}
