namespace CStructSharp;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using CStructSharp.Compilation;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Syntax;
using CStructSharp.Values;

/// <summary>Conditional-field support for the runtime reader: detecting conditional layouts and tracing arm selection.</summary>
public partial class CStruct
{
    /// <summary>Checks only types reachable from the selected root, including aliases and pointer targets.</summary>
    private bool HasConditionalLayout(string rootName)
    {
        var pending = new Stack<CompiledTypeSymbol>();
        var visited = new HashSet<CompiledTypeSymbol>();
        pending.Push(this.compilation.CompiledModel.Symbols[rootName].Symbol);
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
        var state = new CStructOperationContext(stream, new LayoutVariables(variables), this.Aligned, options)
        {
            Debug = true,
            NextPosition = origin,
            ConditionalLayoutTrace = new List<(string Path, long Start, long End)>(),
        };
        try
        {
            this.HandleCStructElement(root, new StructValue(), state, null);
        }
        finally
        {
            state.Complete();
        }

        return state.DebugMapping.Select(item => (item.Path, item.Start, item.End))
            .Concat(state.ConditionalLayoutTrace).ToArray();
    }
}
