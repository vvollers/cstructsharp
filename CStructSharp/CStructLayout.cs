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

    /// <summary>Finds the exact writable layout shape selected by a direct, non-pointer path.</summary>
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
            Field effectiveField = compiledField.EffectiveField;
            CStructElement? namedElement = compiledField.NamedElement;
            bool isArray = compiledField.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime;
            CompiledField writableField = compiledField;

            // TODO(LANG-05): multidimensional path addressing (more than one index per segment) is not yet
            // available - real per-dimension peeling lands in a later seam. Until then, 0 or 1 index behaves
            // exactly as before.
            if (segment.Indexes.Count > 1)
            {
                throw new CStructPathException(
                    "Multiple indices in one path segment require a multidimensional field, not yet available: " +
                    segment.Name);
            }

            if (segment.Indexes.Count == 1)
            {
                if (!isArray)
                {
                    throw new CStructPathException("Field is not an indexable fixed array: " + segment.Name);
                }

                int count = this.compiledSizeQueries.GetCompiledArrayCount(compiledField, variables, false);
                if (segment.Indexes[0] >= count)
                {
                    throw new CStructPathException(
                        $"Array index {segment.Indexes[0]} is out of range for {segment.Name} with length {count}.");
                }

                writableField = compiledField.SelectArrayElement();
            }
            else if (isArray && segmentIndex + 1 < segments.Count)
            {
                throw new CStructPathException("An array index is required before traversing: " + segment.Name);
            }

            if (segmentIndex == segments.Count - 1)
            {
                return writableField;
            }

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
