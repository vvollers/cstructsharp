namespace CStructSharp.Compilation;

using System;
using System.Collections.Generic;
using CStructSharp;
using CStructSharp.Addressing;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;

/// <summary>Contains the small lookup helpers that connect parsed layout declarations to public paths.</summary>
internal sealed partial class LayoutCompilation
{
    /// <summary>Returns a struct selected during path traversal or raises the caller's focused path error.</summary>
    private static CompiledCompositeType RequirePathStruct(CompiledCompositeType? composite, string error)
    {
        return composite ?? throw new CStructPathException(error);
    }

    /// <summary>
    ///     Finds the exact writable layout shape selected by a direct, non-pointer path. An N-dimensional array
    ///     peels one dimension per supplied index - the same "repeat the single-dimension operation once per
    ///     dimension" mechanism the runtime's address resolver uses; fewer indices than dimensions selects the
    ///     corresponding lower-dimensional sub-array.
    /// </summary>
    /// <param name="root">The declaration the path starts at, such as a struct or a typedef of one.</param>
    /// <param name="segments">The parsed path segments after the root; at least one is required.</param>
    /// <param name="variables">The operation's layout variables, for runtime array counts in index checks.</param>
    /// <returns>The selected field, narrowed to the element or sub-array the supplied indexes pick.</returns>
    /// <exception cref="CStructPathException">
    ///     A segment names no field, indexes a non-array or out of range, traverses a scalar, an unindexed array, or a
    ///     pointer, or the path selects nothing.
    /// </exception>
    internal CompiledField ResolveElementPath(
        CStructElement root,
        IReadOnlyList<PathSegment> segments,
        IReadOnlyDictionary<string, Expr> variables)
        => this.ResolveElementPath(root, segments, variables, out _, out _);

    /// <summary>
    ///     Finds the writable layout shape a path selects, as <see cref="ResolveElementPath(CStructElement, IReadOnlyList{PathSegment}, IReadOnlyDictionary{string, Expr})"/>
    ///     does, and names it by the declared member and the number of dimensions its indexes peel. Without
    ///     <paramref name="variables"/> the indexes are not checked against the array counts, so only the path's shape is
    ///     resolved: the compiled engine names the member a write selects this way before the write starts.
    /// </summary>
    /// <param name="root">The declaration the path starts at, such as a struct or a typedef of one.</param>
    /// <param name="segments">The parsed path segments after the root; at least one is required.</param>
    /// <param name="variables">
    ///     The operation's layout variables, for runtime array counts in index checks; <see langword="null"/> skips the
    ///     range checks.
    /// </param>
    /// <param name="declared">The member the last segment names, before any index peels a dimension.</param>
    /// <param name="peeled">The number of dimensions the last segment's indexes peel from <paramref name="declared"/>.</param>
    /// <returns>The selected field, narrowed to the element or sub-array the supplied indexes pick.</returns>
    /// <exception cref="CStructPathException">
    ///     A segment names no field, indexes a non-array or out of range, traverses a scalar, an unindexed array, or a
    ///     pointer, or the path selects nothing.
    /// </exception>
    internal CompiledField ResolveElementPath(
        CStructElement root,
        IReadOnlyList<PathSegment> segments,
        IReadOnlyDictionary<string, Expr>? variables,
        out CompiledField declared,
        out int peeled)
    {
        CStructElement resolvedRoot = this.compiledModelQueries.ResolveCompiledNamedElement(root) ?? root;
        CompiledCompositeType? current = resolvedRoot is Struct rootStruct ? this.compiledSizeQueries.GetCompiledComposite(rootStruct) : null;
        for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
        {
            PathSegment segment = segments[segmentIndex];
            CompiledCompositeType strct = RequirePathStruct(current, "Cannot resolve path segment: " + segment.Name);
            CompiledField compiledField = strct.FindField(segment.Name);
            bool declaredIsArray = compiledField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime or
                                   CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated;

            if (segment.Indexes.Count > 0 && !declaredIsArray)
            {
                throw new CStructPathException("Field is not an indexable fixed array: " + segment.Name);
            }

            int totalDimensions = compiledField.Array.Dimensions.Length;
            if (segment.Indexes.Count > totalDimensions)
            {
                throw new CStructPathException(
                    $"Too many array indices for {segment.Name}: expected at most {totalDimensions}, got " +
                    $"{segment.Indexes.Count}.");
            }

            CompiledField writableField = compiledField;
            foreach (int suppliedIndex in segment.Indexes)
            {
                if (variables is not null)
                {
                    Int128 count = this.compiledSizeQueries.GetCompiledArrayCount(writableField, variables, false);
                    if (suppliedIndex >= count)
                    {
                        throw new CStructPathException(
                            $"Array index {suppliedIndex} is out of range for {segment.Name} with length {count}.");
                    }
                }

                writableField = writableField.SelectArrayElement();
            }

            bool remainingIsArray = writableField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime or
                                    CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated;
            if (remainingIsArray && segmentIndex + 1 < segments.Count)
            {
                throw new CStructPathException("An array index is required before traversing: " + segment.Name);
            }

            if (segmentIndex == segments.Count - 1)
            {
                declared = compiledField;
                peeled = segment.Indexes.Count;
                return writableField;
            }

            if (writableField.PointerDepth > 0)
            {
                throw new CStructPathException(
                    "Write cannot dereference pointer targets; use Update with an existing stream.");
            }

            current = RequirePathStruct(
                writableField.Composite,
                "Cannot traverse through scalar field: " + segment.Name);
        }

        throw new CStructPathException("Path does not select a writable layout element.");
    }
}
