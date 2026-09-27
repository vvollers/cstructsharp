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

    /// <summary>
    ///     Emits the group lines that take the rendering from the arms in <paramref name="open"/> (outermost first) to
    ///     the arms of the next member, <paramref name="target"/>: closes groups the member is not in, moves to the next
    ///     arm of a group it continues, and opens groups it enters. <paramref name="open"/> is updated in place.
    /// </summary>
    /// <param name="builder">The layout text being built.</param>
    /// <param name="open">The arms currently open, outermost first.</param>
    /// <param name="target">The arms of the next member, or none to close everything.</param>
    /// <param name="indent">The indentation of the composite's own members.</param>
    private static void MoveToBranches(StringBuilder builder, List<ConditionalBranch> open, IReadOnlyList<ConditionalBranch> target, string indent)
    {
        int common = 0;
        while (common < open.Count && common < target.Count && open[common] == target[common])
        {
            common++;
        }

        // The next member continues the group at this level in a later arm: keep the group, change the arm.
        bool nextArm = common < open.Count && common < target.Count && ReferenceEquals(open[common].Group, target[common].Group);
        int keep = nextArm ? common + 1 : common;
        while (open.Count > keep)
        {
            int level = open.Count - 1;
            CloseGroup(builder, open[level], LevelIndent(indent, level));
            open.RemoveAt(level);
        }

        if (nextArm)
        {
            MoveToArm(builder, open[common], target[common].Arm, LevelIndent(indent, common));
            open[common] = target[common];
            common++;
        }

        for (int level = common; level < target.Count; level++)
        {
            OpenGroup(builder, target[level], LevelIndent(indent, level));
            open.Add(target[level]);
        }
    }

    /// <summary>The indentation of a group line at nesting <paramref name="level"/> inside a composite.</summary>
    private static string LevelIndent(string indent, int level) => indent + new string(' ', 4 * level);

    /// <summary>Whether a group is a <c>switch</c> (it has case labels) rather than an <c>if</c>.</summary>
    private static bool IsSwitch(ConditionalGroup group) => group.CaseLabels is not null || group.CaseArms is not null;

    /// <summary>The number of <c>case</c> arms of a switch group, excluding <c>default</c>.</summary>
    private static int CaseCount(ConditionalGroup group) => group.CaseLabels?.Count ?? group.CaseArms!.Count;

    /// <summary>Emits the <c>case</c> line of switch arm <paramref name="arm"/>, or the <c>default</c> line for arm -1.</summary>
    private static void AppendArm(StringBuilder builder, ConditionalGroup group, int arm, string indent)
    {
        if (arm < 0)
        {
            builder.Append(indent).AppendLine("default: {");
            return;
        }

        // A normalized group keeps each label's value (value -> arm); a parsed group keeps the label expressions.
        string label = group.CaseLabels is { } labels
                           ? ExpressionPrinter.Print(labels[arm])
                           : group.CaseArms!.First(pair => pair.Value == arm).Key.ToString(CultureInfo.InvariantCulture);
        builder.Append(indent).Append("case ").Append(label).AppendLine(": {");
    }

    /// <summary>
    ///     Emits empty <c>case</c> arms <paramref name="from"/> (inclusive) to <paramref name="to"/> (exclusive). An empty
    ///     arm must still be rendered: without it, its value would select <c>default</c>.
    /// </summary>
    private static void AppendEmptyArms(StringBuilder builder, ConditionalGroup group, int from, int to, string indent)
    {
        for (int arm = from; arm < to; arm++)
        {
            AppendArm(builder, group, arm, indent);
            builder.Append(indent).AppendLine("}");
        }
    }

    /// <summary>Opens the group of <paramref name="branch"/> and its arm; an if-group entered at its else arm gets an empty then arm.</summary>
    private static void OpenGroup(StringBuilder builder, ConditionalBranch branch, string indent)
    {
        ConditionalGroup group = branch.Group;
        string selector = ExpressionPrinter.Print(group.Selector);
        if (!IsSwitch(group))
        {
            builder.Append(indent).Append("if (").Append(selector).AppendLine(") {");
            if (branch.Arm == 0)
            {
                builder.Append(indent).AppendLine("} else {");
            }

            return;
        }

        builder.Append(indent).Append("switch (").Append(selector).AppendLine(") {");
        AppendEmptyArms(builder, group, 0, branch.Arm < 0 ? CaseCount(group) : branch.Arm, indent);
        AppendArm(builder, group, branch.Arm, indent);
    }

    /// <summary>Closes the current arm of a group and opens the later arm <paramref name="arm"/> of the same group.</summary>
    private static void MoveToArm(StringBuilder builder, ConditionalBranch current, int arm, string indent)
    {
        ConditionalGroup group = current.Group;
        if (!IsSwitch(group))
        {
            builder.Append(indent).AppendLine("} else {");
            return;
        }

        builder.Append(indent).AppendLine("}");
        AppendEmptyArms(builder, group, current.Arm + 1, arm < 0 ? CaseCount(group) : arm, indent);
        AppendArm(builder, group, arm, indent);
    }

    /// <summary>Closes the open arm of a group and the group itself, rendering any later empty switch arms.</summary>
    private static void CloseGroup(StringBuilder builder, ConditionalBranch branch, string indent)
    {
        builder.Append(indent).AppendLine("}");
        if (!IsSwitch(branch.Group))
        {
            return;
        }

        if (branch.Arm >= 0)
        {
            AppendEmptyArms(builder, branch.Group, branch.Arm + 1, CaseCount(branch.Group), indent);
        }

        builder.Append(indent).AppendLine("}");
    }

    /// <summary>Renders one struct or union back to Portable text, including each declarator's suffixes.</summary>
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

        // Conditional members are rendered as the if/switch groups they were declared in, not one condition per
        // member: a group is decided once, at its first member, so separate per-member conditions could select
        // differently after an earlier member of the arm changes a variable the selector reads.
        var open = new List<ConditionalBranch>();
        foreach (Field field in composite.Fields)
        {
            MoveToBranches(builder, open, field.BranchConditions, inner);
            string fieldIndent = inner + new string(' ', 4 * open.Count);
            if (field is Struct nested)
            {
                this.RenderComposite(builder, nested, fieldIndent);
                continue;
            }

            builder.Append(fieldIndent);
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

            if (field.PointerCountExpression is not null)
            {
                builder.Append(" @count(").Append(ExpressionPrinter.Print(field.PointerCountExpression)).Append(')');
            }

            builder.AppendLine(";");
        }

        MoveToBranches(builder, open, Array.Empty<ConditionalBranch>(), inner);
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

    /// <summary>Describes the fields of a compiled composite for the public layout introspection.</summary>
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
                    field.BitSize > 0 ? field.BitUnitSize ?? field.BitStorageSize : field.IsZeroWidthBitfield ? 0 : field.FixedStorageSize,
                    field.BitSize > 0 || field.IsZeroWidthBitfield ? field.BitSize : null,
                    field.BitSize > 0 ? field.BitOffset : null,
                    composite.PromotedFields.Contains(field),
                    declaration.IsConditional,
                    promoted)
                {
                    HasCountedTarget = field.HasCountedTarget,
                });
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
