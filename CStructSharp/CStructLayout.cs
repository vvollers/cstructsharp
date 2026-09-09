namespace CStructSharp;

using System;
using System.Collections.Generic;
using CStructSharp.Structure;

/// <summary>Contains the small lookup helpers that connect parsed layout declarations to public paths.</summary>
public partial class CStruct
{
    /// <summary>Returns a struct selected during path traversal or raises the caller's focused path error.</summary>
    private static Struct RequirePathStruct(CStructElement? element, string error)
    {
        return element as Struct ?? throw new CStructPathException(error);
    }

    /// <summary>
    ///     Finds the exact writable layout shape selected by a direct, non-pointer path. An N-dimensional array
    ///     (LANG-05) peels one dimension per supplied index, the same "repeat the existing single-dimension
    ///     operation once per dimension" mechanism <see cref="ResolveTargetInField"/> uses (ADR-016 decision 5);
    ///     fewer indices than dimensions selects the corresponding lower-dimensional sub-array (decision 4).
    /// </summary>
    private CompiledField ResolveElementPath(
        CStructElement root,
        IReadOnlyList<PathSegment> segments,
        IReadOnlyDictionary<string, Expr> variables)
    {
        CStructElement current = this.compiledModelQueries.ResolveCompiledNamedElement(root) ?? root;
        for (int segmentIndex = 0; segmentIndex < segments.Count; segmentIndex++)
        {
            PathSegment segment = segments[segmentIndex];
            Struct strct = RequirePathStruct(current, "Cannot resolve path segment: " + segment.Name);
            CompiledField compiledField = this.FindCompiledField(strct, segment.Name);
            bool declaredIsArray = compiledField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime;

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
                int count = this.compiledSizeQueries.GetCompiledArrayCount(writableField, variables, false);
                if (suppliedIndex >= count)
                {
                    throw new CStructPathException(
                        $"Array index {suppliedIndex} is out of range for {segment.Name} with length {count}.");
                }

                writableField = writableField.SelectArrayElement();
            }

            bool remainingIsArray = writableField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime;
            if (remainingIsArray && segmentIndex + 1 < segments.Count)
            {
                throw new CStructPathException("An array index is required before traversing: " + segment.Name);
            }

            if (segmentIndex == segments.Count - 1)
            {
                return writableField;
            }

            Field effectiveField = writableField.EffectiveField;
            CStructElement? namedElement = writableField.NamedElement;
            if (effectiveField.PointerDepth > 0)
            {
                throw new CStructPathException(
                    "WriteStream cannot dereference pointer targets; use UpdateStream with an existing stream.");
            }

            current = RequirePathStruct(
                namedElement,
                "Cannot traverse through scalar field: " + segment.Name);
        }

        throw new CStructPathException("Path does not select a writable layout element.");
    }
}
