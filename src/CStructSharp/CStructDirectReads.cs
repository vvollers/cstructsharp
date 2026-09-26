namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Reading;
using CStructSharp.Syntax;
using CStructSharp.Values;

/// <summary>
///     The direct read of a whole fixed-layout root from memory: <c>Parse(bytes, "header")</c> and
///     <c>ReadValue&lt;T&gt;(bytes, "header")</c> for a struct whose every member has a fixed offset. The general reader
///     would wrap the span in a stream, build the per-operation state, resolve the path, and then run the same
///     <see cref="StaticReadPlan"/> over the same bytes; this path runs the plan straight over the span.
/// </summary>
/// <remarks>
///     The direct path is taken only when it cannot behave differently from the general reader: no caller variables,
///     no debug records, valid limits that the plan is known to satisfy, and enough input. Every other call, and every
///     input that would fail, goes through the general reader, so failures keep their messages and context.
/// </remarks>
public sealed partial class CStruct
{
    // Test hook: disables the direct paths on the current thread, so the general reader and writer (with their own
    // static plans still enabled) can be compared against them.
    [ThreadStatic]
    private static bool directAccessDisabledForTesting;

    // The most recent root-name lookup of TryGetFixedRootPlan. The entry is immutable and replaced whole, so a reader
    // never sees a torn entry; the box keeps the layout's own fields readonly after construction.
    private readonly StrongBox<FixedRootEntry?> lastFixedRoot = new();

    /// <summary>Gets or sets the per-thread test switch that routes whole-root reads and writes through the general reader and writer.</summary>
    internal static bool DirectAccessDisabledForTesting
    {
        get => directAccessDisabledForTesting;
        set => directAccessDisabledForTesting = value;
    }

    /// <summary>
    ///     Checks the conditions in the class remarks for a direct read of a whole fixed root: a bare root name with a
    ///     static plan, no variables, valid limits that the plan satisfies, and enough input.
    /// </summary>
    /// <param name="source">The input; the root starts at its first byte.</param>
    /// <param name="path">The requested path; only a bare root name qualifies.</param>
    /// <param name="variables">The caller's variables; any dictionary disqualifies the call.</param>
    /// <param name="options">The caller's read options.</param>
    /// <param name="composite">The root composite when the method returns <see langword="true"/>.</param>
    /// <param name="plan">Its static plan when the method returns <see langword="true"/>.</param>
    /// <param name="settings">The snapshot of <paramref name="options"/>.</param>
    /// <returns><see langword="true"/> when the root can be read directly.</returns>
    private bool CanReadFixedRoot(
        ReadOnlySpan<byte> source,
        string path,
        IReadOnlyDictionary<string, int>? variables,
        ReadOptions? options,
        [NotNullWhen(true)] out CompiledCompositeType? composite,
        [NotNullWhen(true)] out StaticReadPlan? plan,
        out ReadOperationSettings settings)
    {
        settings = default;
        if (variables is not null || StaticReadPlan.DisabledForTesting || directAccessDisabledForTesting || !this.TryGetFixedRootPlan(path, out composite, out plan))
        {
            composite = null;
            plan = null;
            return false;
        }

        // The general reader validates the limits and observes a cancelled token before reading; leave both to it.
        settings = ReadOperationSettings.SnapshotReadOptions(options);
        if (settings.CancellationToken.IsCancellationRequested ||
            settings.MaxPointerDepth < 0 || settings.MaxPointerTargetBytes < 0 || settings.MaxArrayElements < 0 ||
            settings.MaxStringBytes < 0 || settings.MaxTotalBytesRead < 0 || settings.MaxNestingDepth <= 0)
        {
            return false;
        }

        // The same preconditions under which the general reader runs this plan for a root at offset zero: the
        // bytes are present and within the read budget, and the nesting and array limits hold for the whole plan.
        return plan.Size <= source.Length && plan.Size <= settings.MaxTotalBytesRead &&
               plan.NestingDepth <= settings.MaxNestingDepth && plan.MaximumArrayCount <= settings.MaxArrayElements;
    }

    /// <summary>Reads a whole fixed root directly when the conditions in the class remarks hold.</summary>
    /// <param name="source">The input; the root starts at its first byte.</param>
    /// <param name="path">The requested path; only a bare root name qualifies.</param>
    /// <param name="variables">The caller's variables; any dictionary disqualifies the call.</param>
    /// <param name="options">The caller's read options.</param>
    /// <param name="result">The parsed root when the method returns <see langword="true"/>.</param>
    /// <param name="consumed">The root's size, which is the stream position the general reader would end at.</param>
    /// <returns><see langword="true"/> when the root was read directly.</returns>
    private bool TryReadFixedRoot(
        ReadOnlySpan<byte> source,
        string path,
        IReadOnlyDictionary<string, int>? variables,
        ReadOptions? options,
        [NotNullWhen(true)] out StructValue? result,
        out int consumed)
    {
        if (!this.CanReadFixedRoot(source, path, variables, options, out CompiledCompositeType? composite, out StaticReadPlan? plan, out ReadOperationSettings settings))
        {
            result = null;
            consumed = 0;
            return false;
        }

        result = this.ReadPlannedRoot(source, composite, plan, settings);
        consumed = plan.Size;
        return true;
    }

    /// <summary>Runs a root's static plan over its bytes, once <see cref="CanReadFixedRoot"/> allowed it.</summary>
    private StructValue ReadPlannedRoot(ReadOnlySpan<byte> source, CompiledCompositeType composite, StaticReadPlan plan, ReadOperationSettings settings)
    {
        var value = new StructValue(composite.Shape);
        this.ExecuteStaticPlan(plan, source[..plan.Size], value, null, settings.MaxArrayElements, settings.TrimFixedText, settings.CancellationToken);
        return value;
    }

    /// <summary>
    ///     Reads a whole fixed root directly and maps it to <typeparamref name="T"/>, with the conversion and failure
    ///     context of <see cref="ReadTypedValueCore{T}"/>. A layout-bound mapped class generated for this very struct
    ///     (the same layout fingerprint) is read by its direct reader, without a <see cref="StructValue"/> in between.
    /// </summary>
    /// <typeparam name="T">The requested type.</typeparam>
    /// <param name="source">The input; the root starts at its first byte.</param>
    /// <param name="path">The requested path; only a bare root name qualifies.</param>
    /// <param name="variables">The caller's variables; any dictionary disqualifies the call.</param>
    /// <param name="options">The caller's read options.</param>
    /// <param name="value">The converted value when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when the root was read directly.</returns>
    /// <exception cref="CStructReadException">The root was read but does not convert to <typeparamref name="T"/>.</exception>
    private bool TryReadFixedRoot<T>(
        ReadOnlySpan<byte> source,
        string path,
        IReadOnlyDictionary<string, int>? variables,
        ReadOptions? options,
        out T value)
    {
        if (!this.CanReadFixedRoot(source, path, variables, options, out CompiledCompositeType? composite, out StaticReadPlan? plan, out ReadOperationSettings settings))
        {
            value = default!;
            return false;
        }

        if (MappedTypes.FindFixedReader<T>(composite.Fingerprint) is { } direct && direct(source[..plan.Size], settings.TrimFixedText, out T? mapped))
        {
            value = mapped;
            return true;
        }

        StructValue root = this.ReadPlannedRoot(source, composite, plan, settings);
        if (root is T typed)
        {
            value = typed;
            return true;
        }

        try
        {
            value = (T)TypedValueConverter.Convert(root, typeof(T), path)!;
            return true;
        }
        catch (CStructException exception)
        {
            // The general reader attaches the formatted path and the stream position after the root, its size.
            exception.AttachContext(path, plan.Size);
            throw;
        }
    }

    /// <summary>Finds the static plan of the root a bare name selects, when that root is a fixed struct.</summary>
    /// <param name="path">The requested path.</param>
    /// <param name="composite">The root composite when the method returns <see langword="true"/>.</param>
    /// <param name="plan">Its static read plan when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> for a struct root (or a typedef naming one inline) with a static plan.</returns>
    private bool TryGetFixedRootPlan(
        string path,
        [NotNullWhen(true)] out CompiledCompositeType? composite,
        [NotNullWhen(true)] out StaticReadPlan? plan)
    {
        // Callers pass the same root name (usually a string literal) call after call; the last answer is kept as one
        // immutable entry, so a repeated lookup is a string comparison instead of two dictionary lookups.
        if (this.lastFixedRoot.Value is { } cached && string.Equals(cached.Path, path, StringComparison.Ordinal))
        {
            composite = cached.Composite;
            plan = cached.Plan;
            return plan is not null;
        }

        bool found = this.FindFixedRootPlan(path, out composite, out plan);
        if (path is not null)
        {
            this.lastFixedRoot.Value = new FixedRootEntry(path, composite, plan);
        }

        return found;
    }

    /// <summary>Finds the static plan of the root a bare name selects, without the one-entry cache.</summary>
    private bool FindFixedRootPlan(
        string path,
        [NotNullWhen(true)] out CompiledCompositeType? composite,
        [NotNullWhen(true)] out StaticReadPlan? plan)
    {
        composite = null;
        plan = null;

        // Only a declared name is a root selection; a dotted or indexed path, or anything the path parser would
        // normalize or reject, takes the general reader.
        if (string.IsNullOrEmpty(path) || path.AsSpan().IndexOfAny(".[] \t\r\n") >= 0 ||
            !this.compiledModelQueries.TryGetCompiledDeclaration(path, out CStructElement? declaration))
        {
            return false;
        }

        // The general reader stores the root under the struct's own name or the typedef alias; the direct result is
        // that stored value, so the name must be the one the caller asked for.
        Struct? body = declaration switch
        {
            Struct { IsUnion: false } direct when direct.Name.Name == path => direct,
            Typedef { Struct: { IsUnion: false } aliased } alias when alias.Name.Name == path => aliased,
            _ => null,
        };
        if (body is null)
        {
            return false;
        }

        composite = this.compiledSizeQueries.GetCompiledComposite(body);
        plan = composite.StaticPlan;
        return plan is not null;
    }

    /// <summary>The answer of <see cref="FindFixedRootPlan"/> for one root name; <see cref="Plan"/> is null for a root without one.</summary>
    private sealed record FixedRootEntry(string Path, CompiledCompositeType? Composite, StaticReadPlan? Plan);
}
