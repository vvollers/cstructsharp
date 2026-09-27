namespace CStructSharp;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using CStructSharp.Compilation;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Syntax;
using CStructSharp.Values;

/// <summary>
///     How an update checks that a replacement keeps every field in place: whether the root has <c>if</c>/<c>switch</c>
///     members, and the byte ranges a read gives every value before and after the change.
/// </summary>
public sealed partial class CStruct
{
    /// <summary>Whether a root reaches a conditional member, which an update must re-read around.</summary>
    /// <param name="rootName">The declared root name.</param>
    /// <returns>Whether any type reachable from the root has an <c>if</c> or <c>switch</c> member.</returns>
    private bool HasConditionalLayout(string rootName)
        => this.compilation.CompiledModel.Symbols[rootName].Symbol.Definition is CompiledCompositeType composite && composite.ReachesConditionalMembers;

    /// <summary>Reads the root once and records where every value and every conditional member lies.</summary>
    /// <param name="stream">The data, the original or the staged copy.</param>
    /// <param name="origin">The root's position.</param>
    /// <param name="root">The root declaration.</param>
    /// <param name="variables">The operation's layout variables.</param>
    /// <param name="options">The read settings.</param>
    /// <returns>Each value's path and byte range, then each conditional member's path, position and selection (1 or 0).</returns>
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
