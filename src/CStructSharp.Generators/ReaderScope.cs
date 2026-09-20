namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Syntax;

/// <summary>
///     What a layout expression inside one composite's reader can see: the members read so far (registered as the
///     emitter passes them, so a later field can size an array by an earlier one, including the qualified members
///     of a nested struct), then the defines and caller variables the <see cref="ExpressionEmitter"/> resolves.
/// </summary>
internal sealed class ReaderScope
{
    private readonly LayoutEmitter emitter;
    private readonly GeneratedComposite composite;
    private readonly Dictionary<string, string> visible = new(StringComparer.Ordinal);
    private readonly Dictionary<CompiledField, GeneratedMember> members = new(ReferenceEqualityComparer.Instance);

    public ReaderScope(LayoutEmitter emitter, GeneratedComposite composite)
    {
        this.emitter = emitter;
        this.composite = composite;
        foreach (GeneratedMember member in composite.Members)
        {
            this.members[member.Field] = member;
        }

        this.Expressions = new ExpressionEmitter(emitter.StaticVariables, emitter.Definitions, this.Resolve);
    }

    public ExpressionEmitter Expressions { get; }

    /// <summary>The generated member for a compiled field of this composite (or of a promoted composite spliced into it).</summary>
    public GeneratedMember? Member(CompiledField field) => this.members.TryGetValue(field, out GeneratedMember? member) ? member : null;

    /// <summary>Makes a member's value visible to later expressions as the runtime's variable capture does.</summary>
    public void Publish(GeneratedMember member, string access)
    {
        string? expression = AsInt32Operand(member, access);
        if (expression is not null)
        {
            this.visible[member.LayoutName] = expression;
        }

        if (member.Composite is { IsUnion: false } nested && member.Field.PointerDepth == 0 && member.Field.Array.Kind == CompiledArrayKind.Scalar)
        {
            // A nested struct's scalars are addressable as `hdr.n`.
            foreach (GeneratedMember inner in nested.Members)
            {
                string? qualified = AsInt32Operand(inner, access + "." + inner.PropertyName);
                if (qualified is not null)
                {
                    this.visible[member.LayoutName + "." + inner.LayoutName] = qualified;
                }
            }
        }
    }

    private string? Resolve(string name) => this.visible.TryGetValue(name, out string? expression) ? expression : null;

    /// <summary>The Int32 operand for a member: an integer member widened and range-checked, a pointer's address, a bool as 0/1; anything else is not an expression operand.</summary>
    private static string? AsInt32Operand(GeneratedMember member, string access)
    {
        CompiledField field = member.Field;
        string name = SourceWriter.Literal(member.LayoutName);
        if (field.Array.Kind != CompiledArrayKind.Scalar || field.Composite is not null)
        {
            return null;
        }

        if (field.PointerDepth > 0)
        {
            return "global::CStructSharp.Generated.Expressions.RequireInt32(" + access + ".Address, " + name + ")";
        }

        if (member.Enum is not null)
        {
            return "global::CStructSharp.Generated.Expressions.RequireInt32((long)(" + member.Enum.UnderlyingType + ")" + access + ", " + name + ")";
        }

        return field.Codec.Kind switch
        {
            PrimitiveCodecKind.Bool => "(" + access + " ? 1 : 0)",
            PrimitiveCodecKind.UInt64 or PrimitiveCodecKind.ULeb128_64 or PrimitiveCodecKind.UInt48 => "global::CStructSharp.Generated.Expressions.RequireInt32((ulong)" + access + ", " + name + ")",
            PrimitiveCodecKind.UInt8 or PrimitiveCodecKind.Int8 or PrimitiveCodecKind.Char or PrimitiveCodecKind.WChar or PrimitiveCodecKind.Latin1 or PrimitiveCodecKind.Cp437 or PrimitiveCodecKind.Utf8Unit or PrimitiveCodecKind.Utf16LeUnit or PrimitiveCodecKind.Utf16BeUnit
                or PrimitiveCodecKind.Int16 or PrimitiveCodecKind.UInt16 or PrimitiveCodecKind.Int24 or PrimitiveCodecKind.UInt24 or PrimitiveCodecKind.Int32 or PrimitiveCodecKind.UInt32 or PrimitiveCodecKind.Int48 or PrimitiveCodecKind.Int64
                or PrimitiveCodecKind.ULeb128_32 or PrimitiveCodecKind.SLeb128_32 or PrimitiveCodecKind.SLeb128_64 => "global::CStructSharp.Generated.Expressions.RequireInt32((long)" + access + ", " + name + ")",
            _ => null,
        };
    }
}
