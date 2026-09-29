namespace CStructSharp.Compilation;

using System;
using System.Collections.Generic;
using System.Threading;
using CStructSharp;
using CStructSharp.Addressing;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;

/// <summary>Contains the small lookup helpers that connect parsed layout declarations to public paths.</summary>
internal sealed partial class LayoutCompilation
{
    /// <summary>The last update decision, reused while updates repeat the same parsed path.</summary>
    private UpdateDecision? lastUpdateDecision;

    /// <summary>Returns a struct selected during path traversal or raises the caller's focused path error.</summary>
    private static CompiledCompositeType RequirePathStruct(CompiledCompositeType? composite, string error)
    {
        return composite ?? throw new CStructPathException(error);
    }

    /// <summary>Whether a field (or view) is an array a path indexes: fixed, runtime-counted, to-end or terminated.</summary>
    /// <param name="field">The field.</param>
    /// <returns>Whether it takes indexes.</returns>
    private static bool IsIndexable(CompiledField field)
        => field.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime or CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated;

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

    /// <summary>
    ///     Returns why the compiled engine cannot run an update of a path, or <see langword="null"/> when it can: the root
    ///     must be readable (its programs walk the path and capture the layout an update compares), and the storage the
    ///     path selects writable on its own - the root through its write program, a member or element through its member
    ///     program, a pointer's <c>.value</c> storage through its pointed-to program; a pointer's <c>.address</c> needs none.
    ///     The path's shape is followed without data, as the interpreter's resolver follows it (members, indexes, pointer
    ///     accessors, nested structs and unions); a path it cannot follow is declined, so the interpreter reports it.
    /// </summary>
    /// <param name="root">The path's root declaration.</param>
    /// <param name="segments">The parsed path, the root's name first.</param>
    /// <returns>The reason, or <see langword="null"/>.</returns>
    /// <remarks>
    ///     The last decision is kept with the parsed path it was made for: the path parser returns the same segments for the
    ///     same path, so repeated updates of one path decide with one reference comparison.
    /// </remarks>
    public string? DeclineUpdate(CStructElement root, IReadOnlyList<PathSegment> segments)
    {
        if (Volatile.Read(ref this.lastUpdateDecision) is { } last && ReferenceEquals(last.Segments, segments))
        {
            return last.Reason;
        }

        string? reason = this.DeclineUpdateCore(root, segments);
        Volatile.Write(ref this.lastUpdateDecision, new UpdateDecision(segments, reason));
        return reason;
    }

    /// <summary>Makes the decision <see cref="DeclineUpdate"/> keeps.</summary>
    /// <param name="root">The path's root declaration.</param>
    /// <param name="segments">The parsed path, the root's name first.</param>
    /// <returns>The reason, or <see langword="null"/>.</returns>
    private string? DeclineUpdateCore(CStructElement root, IReadOnlyList<PathSegment> segments)
    {
        string rootName = segments[0].Name;
        if (this.GetRootReadProgram(rootName).Reason is { } unreadable)
        {
            return unreadable;
        }

        if (this.GetRootDebugReadProgram(rootName).Reason is { } untraceable)
        {
            return untraceable;
        }

        if (segments.Count == 1)
        {
            return this.GetRootWriteProgram(rootName).Reason;
        }

        try
        {
            return this.UpdateTargetOutcome(root, segments)?.Reason;
        }
        catch (CStructException exception)
        {
            return WriteProgramCompiler.UnresolvedPath + exception.Message;
        }
    }

    /// <summary>
    ///     Follows an update path's shape without data and returns the program that writes what it selects, or
    ///     <see langword="null"/> for a pointer's <c>.address</c>, which the update writes as an address.
    /// </summary>
    /// <param name="root">The path's root declaration.</param>
    /// <param name="segments">The parsed path, more than one segment.</param>
    /// <returns>The outcome of the selected storage's program, or <see langword="null"/>.</returns>
    /// <exception cref="CStructPathException">The path's shape cannot be followed.</exception>
    private WriteProgramOutcome? UpdateTargetOutcome(CStructElement root, IReadOnlyList<PathSegment> segments)
    {
        CStructElement resolvedRoot = this.compiledModelQueries.ResolveCompiledNamedElement(root) ?? root;
        CompiledCompositeType current = resolvedRoot is Struct rootStruct
                                            ? this.compiledSizeQueries.GetCompiledComposite(rootStruct)
                                            : throw new CStructPathException("Root path cannot contain child segments: " + segments[0].Name);
        int last = segments.Count - 1;
        for (int index = 1; index <= last; index++)
        {
            PathSegment segment = segments[index];
            CompiledField declared = current.FindField(segment.Name);
            if (segment.Indexes.Count > (IsIndexable(declared) ? declared.Array.Dimensions.Length : 0))
            {
                throw new CStructPathException("Field cannot take these indexes: " + segment.Name);
            }

            CompiledField field = declared;
            for (int peel = 0; peel < segment.Indexes.Count; peel++)
            {
                field = field.SelectArrayElement();
            }

            if (index == last)
            {
                return this.SlotTable.WritePrograms.GetMember(this, declared, segment.Indexes.Count);
            }

            if (IsIndexable(field))
            {
                throw new CStructPathException("An array index is required before traversing: " + segment.Name);
            }

            if (field.PointerDepth == 0)
            {
                current = field.Composite ?? throw new CStructPathException("Cannot traverse through scalar field: " + segment.Name);
                continue;
            }

            // Pointer accessors: .address ends the path at the stored bits, each .value follows one level.
            int remaining = field.PointerDepth;
            while (true)
            {
                PathSegment accessor = segments[++index];
                if (accessor.Indexes.Count > 0)
                {
                    throw new CStructPathException("Pointer accessors cannot have array indexes.");
                }

                if (string.Equals(accessor.Name, "address", StringComparison.Ordinal))
                {
                    return index == last ? null : throw new CStructPathException("Pointer .address must be the terminal path segment.");
                }

                if (!string.Equals(accessor.Name, "value", StringComparison.Ordinal))
                {
                    throw new CStructPathException("Expected pointer accessor '.value' or '.address' after: " + segment.Name);
                }

                if (field.HasCountedTarget)
                {
                    // The count may name a member after the pointer, which a path never reads.
                    throw new CStructPathException("A path cannot select the target of a @count pointer: " + segment.Name);
                }

                remaining--;
                if (index == last)
                {
                    return this.SlotTable.WritePrograms.GetPointee(this, declared, segment.Indexes.Count, remaining);
                }

                if (remaining == 0)
                {
                    current = field.TargetComposite ?? throw new CStructPathException("Cannot traverse beyond a scalar pointer target.");
                    break;
                }
            }
        }

        throw new CStructPathException("Path does not select a writable layout element.");
    }

    /// <summary>One kept update decision: the parsed path it was made for and its reason (<see langword="null"/>: eligible).</summary>
    /// <param name="Segments">The parsed path.</param>
    /// <param name="Reason">The decline reason, or <see langword="null"/>.</param>
    private sealed record UpdateDecision(IReadOnlyList<PathSegment> Segments, string? Reason);
}
