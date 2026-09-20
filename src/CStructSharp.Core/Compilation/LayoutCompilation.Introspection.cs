namespace CStructSharp.Compilation;

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using CStructSharp;
using CStructSharp.Introspection;
using CStructSharp.Syntax;
using CstructEnum = CStructSharp.Syntax.Enum;

/// <summary>The description of a compiled layout (<see cref="Layout"/>) and its rendering back to Portable text.</summary>
internal sealed partial class LayoutCompilation
{
    /// <summary>
    ///     Renders the compiled layout back to Portable text: every declaration (including a prelude's), evaluated
    ///     enum values, resolved typedef targets, and the recorded includes and defines. The result is semantically
    ///     equivalent to the source, not identical to it, and compiles with the same options.
    /// </summary>
    /// <returns>The layout as Portable definition text.</returns>
    public string ToDefinition()
    {
        var builder = new StringBuilder();
        foreach (string include in this.Includes)
        {
            builder.Append("#include ").Append(include.Contains('"') ? '<' + include + '>' : '"' + include + '"').AppendLine();
        }

        foreach (KeyValuePair<string, CStructElement> declaration in this.CompiledModel.OrderedDeclarations)
        {
            switch (declaration.Value)
            {
            case Defines define when this.CompiledModel.Declarations.ContainsKey(define.Name.Name):
                builder.Append("#define ").Append(define.Name.Name).Append(' ').Append(ExpressionPrinter.Print(define.Value)).AppendLine();
                break;
            case ConstantDefinition constant:
                builder.AppendLine(new LayoutConstant(constant.Name.Name, constant.Kind, constant.Value).ToString());
                break;
            case CstructEnum enm:
                builder.Append(enm.IsFlag ? "flag " : "enum ").Append(enm.Name.Name).Append(" : ").Append(enm.Type.Name).AppendLine(" {");
                foreach (EnumValue member in enm.Values)
                {
                    builder.Append("    ").Append(member.Name.Name).Append(" = ").Append(ExpressionPrinter.Print(member.Value)).AppendLine(",");
                }

                builder.AppendLine("};");
                break;
            case Typedef { Struct: { } tagged, } typedef:
                if (tagged.Name.Name == typedef.Name.Name)
                {
                    this.RenderComposite(builder, tagged, string.Empty);
                }
                else
                {
                    builder.Append("typedef ").Append(tagged.Name.Name).Append(' ').Append(typedef.Name.Name).AppendLine(";");
                }

                break;
            case Typedef typedef:
                builder.Append("typedef ").Append(typedef.Type.Name).Append(new string('*', typedef.Type.PointerDepth)).Append(' ').Append(typedef.Name.Name);
                foreach (Expr dimension in typedef.ArrayShape)
                {
                    builder.Append('[').Append(ExpressionPrinter.Print(dimension)).Append(']');
                }

                builder.AppendLine(";");
                break;
            case Struct composite:
                this.RenderComposite(builder, composite, string.Empty);
                break;
            }
        }

        return builder.ToString();
    }

    private void RenderComposite(StringBuilder builder, Struct composite, string indent)
    {
        builder.Append(indent).Append(composite.IsUnion ? "union " : "struct ");
        if (composite.Name.Name.Length > 0 && indent.Length == 0)
        {
            builder.Append(composite.Name.Name).Append(' ');
        }

        if (composite.CompositeAlignmentOverrideExpression is not null)
        {
            builder.Append("@align(").Append(ExpressionPrinter.Print(composite.CompositeAlignmentOverrideExpression)).Append(") ");
        }

        builder.AppendLine("{");
        string inner = indent + "    ";
        foreach (Field field in composite.Fields)
        {
            if (field is SwitchCaseValidation)
            {
                continue;
            }

            if (field is Struct nested)
            {
                this.RenderComposite(builder, nested, inner);
                continue;
            }

            builder.Append(inner);
            if (field.Condition is not null)
            {
                builder.Append("if (").Append(ExpressionPrinter.Print(field.Condition)).Append(") { ");
            }

            builder.Append(field.Type.Name).Append(' ').Append(new string('*', field.PointerDepth)).Append(field.Name.Name.Length == 0 && field.BitSize == 0 && !field.HasBitfieldDeclarator ? "_" : field.Name.Name);
            foreach (Expr dimension in field.ArrayCount)
            {
                builder.Append('[').Append(ReferenceEquals(dimension, Field.UnknownArraysize) ? string.Empty : ExpressionPrinter.Print(dimension)).Append(']');
            }

            if (field.BitSize > 0 || field.HasBitfieldDeclarator)
            {
                builder.Append(" : ").Append(field.BitSize.ToString(CultureInfo.InvariantCulture));
            }

            if (field.AlignmentOverrideExpression is not null)
            {
                builder.Append(" @align(").Append(ExpressionPrinter.Print(field.AlignmentOverrideExpression)).Append(')');
            }
            else if (field.OffsetAssertionExpression is not null)
            {
                builder.Append(" @").Append(ExpressionPrinter.Print(field.OffsetAssertionExpression));
            }

            builder.Append(';');
            if (field.Condition is not null)
            {
                builder.Append(" }");
            }

            builder.AppendLine();
        }

        builder.Append(indent).Append('}');
        if (composite.Name.Name.Length > 0 && indent.Length > 0)
        {
            // An inline named member: `struct { ... } name;`.
            builder.Append(' ').Append(composite.Name.Name);
        }

        builder.AppendLine(";");
    }

    private LayoutInfo BuildLayoutInfo()
    {
        var declarations = new List<LayoutDeclarationInfo>();
        foreach (KeyValuePair<string, CStructElement> declaration in this.CompiledModel.OrderedDeclarations)
        {
            switch (declaration.Value)
            {
            case Struct composite:
                declarations.Add(this.DescribeComposite(declaration.Key, composite));
                break;
            case CstructEnum enm:
                {
                    CompiledEnumType compiled = this.compiledModelQueries.GetCompiledEnum(enm);
                    declarations.Add(
                        new LayoutDeclarationInfo(
                            declaration.Key,
                            enm.IsFlag ? LayoutDeclarationKind.Flag : LayoutDeclarationKind.Enum,
                            compiled.Integer.SizeInBytes,
                            compiled.Integer.SizeInBytes,
                            Array.Empty<LayoutFieldInfo>(),
                            enm.Values.Select(member => new LayoutEnumMemberInfo(member.Name.Name, ((Literal)member.Value).ExactValue)).ToArray(),
                            compiled.Integer.StorageType,
                            0,
                            Array.Empty<int>()));
                    break;
                }

            case Typedef { Struct: { } tagged, } typedef when tagged.Name.Name == typedef.Name.Name:
                declarations.Add(this.DescribeComposite(declaration.Key, tagged));
                break;
            case Typedef typedef:
                {
                    CompiledTypeReference type = this.CompiledModel.Symbols[declaration.Key];
                    int? size = type.PointerDepth > 0 ? this.PointerSize : type.Symbol.FixedSize;
                    int[] shape = typedef.ArrayShape.Select(dimension => ((Literal)dimension).ExactValue).Select(value => (int)value).ToArray();
                    if (size.HasValue)
                    {
                        foreach (int count in shape)
                        {
                            size = checked(size.Value * count);
                        }
                    }

                    declarations.Add(
                        new LayoutDeclarationInfo(
                            declaration.Key,
                            LayoutDeclarationKind.Typedef,
                            size,
                            type.PointerDepth > 0 ? this.PointerSize : type.Symbol.Alignment,
                            Array.Empty<LayoutFieldInfo>(),
                            Array.Empty<LayoutEnumMemberInfo>(),
                            typedef.Struct?.Name.Name ?? type.TerminalName,
                            type.PointerDepth,
                            shape));
                    break;
                }
            }
        }

        return new LayoutInfo(declarations, this.Constants, this.Includes);
    }

    private LayoutDeclarationInfo DescribeComposite(string name, Struct composite)
    {
        CompiledCompositeType compiled = this.compiledSizeQueries.GetCompiledComposite(composite);
        return new LayoutDeclarationInfo(
            name,
            composite.IsUnion ? LayoutDeclarationKind.Union : LayoutDeclarationKind.Struct,
            compiled.Symbol.FixedSize,
            compiled.Symbol.Alignment,
            this.DescribeFields(compiled),
            Array.Empty<LayoutEnumMemberInfo>(),
            null,
            0,
            Array.Empty<int>());
    }

    private IReadOnlyList<LayoutFieldInfo> DescribeFields(CompiledCompositeType composite)
    {
        var fields = new List<LayoutFieldInfo>(composite.Fields.Length);
        foreach (CompiledField field in composite.Fields)
        {
            Field declaration = field.Declaration;
            IReadOnlyList<LayoutFieldInfo> promoted = Array.Empty<LayoutFieldInfo>();
            if (composite.PromotedFields.Contains(field) && field.Type.Symbol.Definition is CompiledCompositeType promotedComposite)
            {
                promoted = this.DescribeFields(promotedComposite);
            }

            LayoutArrayKind kind = field.Array.Kind switch
            {
                CompiledArrayKind.Scalar => LayoutArrayKind.Scalar,
                CompiledArrayKind.Fixed => LayoutArrayKind.Fixed,
                CompiledArrayKind.Runtime => LayoutArrayKind.Runtime,
                CompiledArrayKind.Flexible => LayoutArrayKind.String,
                CompiledArrayKind.ToEnd => LayoutArrayKind.ToEnd,
                _ => LayoutArrayKind.Terminated,
            };
            fields.Add(
                new LayoutFieldInfo(
                    declaration.Name.Name,
                    declaration is Struct inline ? inline.Name.Name : field.Type.TerminalName,
                    field.PointerDepth,
                    kind,
                    field.Array.Dimensions.Select(dimension => dimension.FixedCount).ToArray(),
                    field.FixedOffset,
                    field.EffectiveField.BitSize > 0 ? field.BitUnitSize ?? field.BitStorageSize : field.IsZeroWidthBitfield ? 0 : field.FixedStorageSize,
                    field.EffectiveField.BitSize > 0 || field.IsZeroWidthBitfield ? field.EffectiveField.BitSize : null,
                    field.EffectiveField.BitSize > 0 ? field.BitOffset : null,
                    composite.PromotedFields.Contains(field),
                    declaration.Condition is not null,
                    promoted));
        }

        return fields;
    }

    /// <summary>Renders an expression tree back to Portable spelling.</summary>
    private static class ExpressionPrinter
    {
        public static string Print(Expr expression)
        {
            return expression switch
            {
                Literal literal => literal.ExactValue.ToString(CultureInfo.InvariantCulture),
                NoneExpr => "0",
                Identifier identifier => identifier.Name,
                UnaryOp unary => Symbol(unary.Type) + Wrap(unary.Expr),
                BinaryOp binary => Wrap(binary.Left) + " " + Symbol(binary.Type) + " " + Wrap(binary.Right),
                ConditionalExpr conditional => Wrap(conditional.Condition) + " ? " + Wrap(conditional.WhenTrue) + " : " + Wrap(conditional.WhenFalse),
                Call call => Print(call.Expr) + "(" + string.Join(", ", call.Arguments.Select(Print)) + ")",
                _ => expression.ToString() ?? string.Empty,
            };
        }

        private static string Wrap(Expr expression)
        {
            return expression is Literal or Identifier or NoneExpr or Call ? Print(expression) : "(" + Print(expression) + ")";
        }

        private static string Symbol(UnaryOperatorType type)
        {
            return type switch
            {
                UnaryOperatorType.Neg => "-",
                UnaryOperatorType.Complement => "~",
                _ => "!",
            };
        }

        private static string Symbol(BinaryOperatorType type)
        {
            return type switch
            {
                BinaryOperatorType.LogicalAnd => "&&",
                BinaryOperatorType.LogicalOr => "||",
                BinaryOperatorType.Equal => "==",
                BinaryOperatorType.NotEqual => "!=",
                BinaryOperatorType.Less => "<",
                BinaryOperatorType.LessOrEqual => "<=",
                BinaryOperatorType.Greater => ">",
                BinaryOperatorType.GreaterOrEqual => ">=",
                BinaryOperatorType.Add => "+",
                BinaryOperatorType.Minus => "-",
                BinaryOperatorType.Mul => "*",
                BinaryOperatorType.Div => "/",
                BinaryOperatorType.And => "&",
                BinaryOperatorType.Or => "|",
                BinaryOperatorType.ShiftLeft => "<<",
                BinaryOperatorType.ShiftRight => ">>",
                BinaryOperatorType.Mod => "%",
                _ => "^",
            };
        }
    }
}
