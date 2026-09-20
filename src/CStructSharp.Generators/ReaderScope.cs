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
    private readonly HashSet<string>? locals;

    public ReaderScope(LayoutEmitter emitter, GeneratedComposite composite)
    {
        this.emitter = emitter;
        this.composite = composite;
        foreach (GeneratedMember member in composite.Members)
        {
            this.members[member.Field] = member;
        }

        // A composite with conditional fields protects its own member names: from its first byte they hide any
        // outer or caller value, and one that has not been read yet (or sits in an unselected arm) is undefined.
        this.locals = composite.Composite.HasDirectConditionalFields
                          ? new HashSet<string>(composite.Composite.ConditionalLocalNames, StringComparer.Ordinal)
                          : null;
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
            string? guard = member.IsConditional ? FlagAccess(member, access) : null;
            this.PublishNested(nested, member.LayoutName, access, guard, 0);
        }
    }

    /// <summary>
    ///     A nested struct's scalars, as the runtime publishes them: qualified (<c>hdr.n</c>, through several levels)
    ///     and under their bare names (the last value read wins), except that a conditional composite's own member
    ///     names are restored after the nested read and so never take a nested value.
    /// </summary>
    private void PublishNested(GeneratedComposite nested, string qualifiedPrefix, string access, string? guard, int depth)
    {
        if (depth > 8)
        {
            return;
        }

        foreach (GeneratedMember inner in nested.Members)
        {
            string innerAccess = access + "." + inner.PropertyName;
            string? qualified = AsInt32Operand(inner, innerAccess);
            if (qualified is not null)
            {
                if (guard is not null)
                {
                    // The nested struct itself sits in a conditional arm: its members exist only when it was read.
                    qualified = "(" + guard + " ? " + qualified + " : global::CStructSharp.Generated.Expressions.Undefined(" + SourceWriter.Literal(inner.LayoutName) + "))";
                }

                this.visible[qualifiedPrefix + "." + inner.LayoutName] = qualified;
                if (this.locals is null || !this.locals.Contains(inner.LayoutName))
                {
                    this.visible[inner.LayoutName] = qualified;
                }
            }

            if (inner.Composite is { IsUnion: false } deeper && inner.Field.PointerDepth == 0 && inner.Field.Array.Kind == CompiledArrayKind.Scalar && !inner.IsConditional)
            {
                this.PublishNested(deeper, qualifiedPrefix + "." + inner.LayoutName, innerAccess, guard, depth + 1);
            }
        }
    }

    private string? Resolve(string name)
    {
        if (this.visible.TryGetValue(name, out string? expression))
        {
            return expression;
        }

        return this.locals is not null && this.locals.Contains(name)
                   ? "global::CStructSharp.Generated.Expressions.Undefined(" + SourceWriter.Literal(name) + ")"
                   : null;
    }

    /// <summary>The Int32 operand for a member: an integer member widened and range-checked, a pointer's address, a bool as 0/1; anything else is not an expression operand.</summary>
    private static string? AsInt32Operand(GeneratedMember member, string access)
    {
        string? operand = Int32Operand(member, access);
        if (operand is null || !member.IsConditional)
        {
            return operand;
        }

        // A conditional member has a value only when its arm was selected; otherwise the name is undefined.
        return "(" + FlagAccess(member, access) + " ? " + operand + " : global::CStructSharp.Generated.Expressions.Undefined(" + SourceWriter.Literal(member.LayoutName) + "))";
    }

    /// <summary>The presence flag next to a conditional member's property: <c>value.HasN</c> for <c>value.N</c>.</summary>
    private static string FlagAccess(GeneratedMember member, string access)
        => access.Substring(0, access.Length - member.PropertyName.Length) + member.HasFlagName;

    private static string? Int32Operand(GeneratedMember member, string access)
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
