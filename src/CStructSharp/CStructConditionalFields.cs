namespace CStructSharp;

using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using CStructSharp.Structure;

/// <summary>Shares runtime field activation across compiled operations.</summary>
public partial class CStruct
{
    /// <summary>Freezes case constants without recursive traversal or changing runtime selectors.</summary>
    private static Expr? NormalizeCaseConstants(Expr? expression, Dictionary<Expr, Expr>? constants)
    {
        if (expression is null || constants is null || constants.Count == 0)
        {
            return expression;
        }

        var rewritten = new Dictionary<Expr, Expr>(constants, ReferenceEqualityComparer.Instance);
        var pending = new Stack<(Expr Expression, bool Complete)>();
        pending.Push((expression, false));
        while (pending.Count > 0)
        {
            (Expr current, bool complete) = pending.Pop();
            if (rewritten.ContainsKey(current))
            {
                continue;
            }

            if (complete)
            {
                rewritten[current] = current switch
                {
                    BinaryOp binary => new BinaryOp(binary.Type, rewritten[binary.Left], rewritten[binary.Right]),
                    UnaryOp unary => new UnaryOp(unary.Type, rewritten[unary.Expr]),
                    _ => current,
                };
                continue;
            }

            pending.Push((current, true));
            if (current is BinaryOp operation)
            {
                pending.Push((operation.Right, false));
                pending.Push((operation.Left, false));
            }
            else if (current is UnaryOp unary)
            {
                pending.Push((unary.Expr, false));
            }
        }

        return rewritten[expression];
    }

    /// <summary>Checks only types reachable from the selected root, including aliases and pointer targets.</summary>
    private bool HasConditionalLayout(string rootName)
    {
        var pending = new Stack<CompiledTypeSymbol>();
        var visited = new HashSet<CompiledTypeSymbol>();
        pending.Push(this.compiledLayout.Symbols[rootName].Symbol);
        while (pending.Count > 0)
        {
            CompiledTypeSymbol symbol = pending.Pop();
            if (!visited.Add(symbol) || symbol.Definition is not CompiledCompositeType composite)
            {
                continue;
            }

            foreach (CompiledField field in composite.Fields)
            {
                if (field.Declaration.Condition is not null)
                {
                    return true;
                }

                pending.Push(field.Type.Symbol);
            }
        }

        return false;
    }

    private (string Path, long Start, long End)[] CaptureUpdateLayout(
        Stream stream, long origin, CStructElement root, Dictionary<string, Expr> variables, ReadOperationSettings options)
    {
        stream.Position = origin;
        var state = new CStructOperationContext(stream, new Dictionary<string, Expr>(variables), this.Aligned, options)
        {
            Debug = true,
            NextPosition = origin,
            ConditionalLayoutTrace = new List<(string Path, long Start, long End)>(),
        };
        this.HandleCStructElement(root, new ExpandoObject(), state, System.Array.Empty<CStructElement>());
        return state.DebugMapping.Select(item => (item.DebugStackString, item.CurPos, item.EndPos))
            .Concat(state.ConditionalLayoutTrace).ToArray();
    }
}
