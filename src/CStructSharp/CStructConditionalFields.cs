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
    /// <summary>Whether a root reaches a conditional member, which an update must re-read around.</summary>
    /// <param name="rootName">The declared root name.</param>
    /// <returns>Whether any type reachable from the root has an <c>if</c> or <c>switch</c> member.</returns>
    private bool HasConditionalLayout(string rootName)
        => this.compilation.CompiledModel.Symbols[rootName].Symbol.Definition is CompiledCompositeType composite && composite.ReachesConditionalMembers;

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
