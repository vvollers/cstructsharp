namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using System.Threading;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Reading;
using CStructSharp.Syntax;
using CStructSharp.Values;

/// <summary>
///     The direct read of a whole fixed-layout root from memory: <c>Parse(bytes, "header")</c> and
///     <c>ReadValue&lt;T&gt;(bytes, "header")</c> for a struct whose every member has a fixed offset. The compiled engine
///     would build the per-operation state and slots, resolve the path, and then run the same <see cref="StaticReadPlan"/>
///     over the same bytes; this path runs the plan straight over the span.
/// </summary>
/// <remarks>
///     The direct path is taken only when it cannot behave differently from the compiled engine: no caller variables,
///     no debug records, valid limits that the plan is known to satisfy, and enough input. Every other call, and every
///     input that would fail, goes through the engine, so failures keep their messages and context.
/// </remarks>
public sealed partial class CStruct
{
    // The most recent root-name lookup of TryGetFixedRootPlan. The entry is immutable and replaced whole, so a reader
    // never sees a torn entry; the box keeps the layout's own fields readonly after construction.
    private readonly StrongBox<FixedRootEntry?> lastFixedRoot = new();

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
        if (variables is not null || options?.ExecutionPath is not (null or ExecutionPath.Fastest) || !this.TryGetFixedRootPlan(path, out composite, out plan))
        {
            composite = null;
            plan = null;
            return false;
        }

        // The engine validates the limits and observes a cancelled token before reading; leave both to it.
        // Otherwise these are the preconditions under which it runs this plan for a root at offset zero.
        settings = ReadOperationSettings.SnapshotReadOptions(options);
        return settings.HasValidLimits && plan.Size <= source.Length && settings.CoversPlan(plan);
    }

    /// <summary>Reads a whole fixed root directly when the conditions in the class remarks hold.</summary>
    /// <param name="source">The input; the root starts at its first byte.</param>
    /// <param name="path">The requested path; only a bare root name qualifies.</param>
    /// <param name="variables">The caller's variables; any dictionary disqualifies the call.</param>
    /// <param name="options">The caller's read options.</param>
    /// <param name="result">The parsed root when the method returns <see langword="true"/>.</param>
    /// <param name="consumed">The root's size, which is the stream position the compiled engine would end at.</param>
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
    /// <param name="source">The borrowed bytes beginning at the root.</param>
    /// <param name="composite">The composite whose shape the plan fills completely.</param>
    /// <param name="plan">The validated fixed read plan.</param>
    /// <param name="settings">The operation's validated limits, text policy and cancellation token.</param>
    /// <returns>An owned mutable result, exposed only after every slot is filled.</returns>
    /// <exception cref="OperationCanceledException">The token is cancelled before the plan finishes.</exception>
    private StructValue ReadPlannedRoot(ReadOnlySpan<byte> source, CompiledCompositeType composite, StaticReadPlan plan, ReadOperationSettings settings)
    {
        // A fixed plan fills every named shape slot; no conditional member can remain absent.
        object?[] slots = composite.Shape.Count == 0 ? Array.Empty<object?>() : new object?[composite.Shape.Count];
        ExecuteStaticPlan(plan, source[..plan.Size], slots, settings.MaxArrayElements, settings.TrimFixedText, settings.CancellationToken);
        return new StructValue(composite.Shape, slots);
    }

    /// <summary>
    ///     Runs a static read plan over exactly a composite's bytes into its value, as a direct read of a whole fixed root
    ///     does: nothing is read afterwards, so no layout variable is published, and the caller checked the nesting limit.
    ///     The token is observed on entering each composite, as the compiled engine observes it.
    /// </summary>
    /// <param name="plan">The plan of the composite being read.</param>
    /// <param name="bytes">Exactly the composite's bytes.</param>
    /// <param name="destination">The complete shape's owned slots; every slot is filled before the value is exposed.</param>
    /// <param name="maxArrayElements">The array element limit of the read.</param>
    /// <param name="trimFixedText">Whether fixed-capacity text drops its trailing NUL padding.</param>
    /// <param name="cancellationToken">The token observed on entering each composite.</param>
    /// <exception cref="OperationCanceledException">The token is cancelled.</exception>
    /// <exception cref="CStructReadLimitException">An array holds more elements than the limit.</exception>
    private static void ExecuteStaticPlan(
        StaticReadPlan plan,
        ReadOnlySpan<byte> bytes,
        object?[] destination,
        int maxArrayElements,
        bool trimFixedText,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        StaticReadOperation[] operations = plan.Operations;
        for (int index = 0; index < operations.Length; index++)
        {
            StaticReadOperation operation = operations[index];
            CompiledField field = operation.Field;
            switch (operation.Kind)
            {
            case StaticReadKind.Numeric:
                destination[operation.Slot] = field.Codec.ReadNumeric(bytes.Slice(operation.Offset, field.Codec.Size));
                break;

            case StaticReadKind.Enum:
                {
                    object storage = field.Codec.ReadNumeric(bytes.Slice(operation.Offset, field.Codec.Size));
                    destination[operation.Slot] = ValueDecoding.CreateEnumValue(field.Enum!, storage);
                    break;
                }

            case StaticReadKind.CharArray:
                {
                    if (operation.Count > maxArrayElements)
                    {
                        throw new CStructReadLimitException(ReadFailures.ArrayLengthLimit(operation.Count, maxArrayElements));
                    }

                    string latin1 = ValueDecoding.ReadLatin1Characters(bytes.Slice(operation.Offset, operation.Count));
                    destination[operation.Slot] = trimFixedText ? latin1.TrimEnd('\0') : latin1;
                    break;
                }

            case StaticReadKind.NumericArray:
                {
                    if (operation.Count > maxArrayElements)
                    {
                        throw new CStructReadLimitException(ReadFailures.ArrayLengthLimit(operation.Count, maxArrayElements));
                    }

                    object values = operation.Count == 0
                                        ? PrimitiveArrayReader.Empty(field.Codec)
                                        : PrimitiveArrayReader.Decode(bytes.Slice(operation.Offset, operation.Count * field.Codec.Size), field.Codec, operation.Count);
                    destination[operation.Slot] = values;
                    break;
                }

            case StaticReadKind.Nested:
                {
                    StructShape nestedShape = operation.NestedComposite!.Shape;
                    object?[] nestedSlots = nestedShape.Count == 0 ? Array.Empty<object?>() : new object?[nestedShape.Count];
                    var nested = new StructValue(nestedShape, nestedSlots);
                    destination[operation.Slot] = nested;
                    ExecuteStaticPlan(operation.NestedPlan!, bytes.Slice(operation.Offset, operation.NestedPlan!.Size), nestedSlots, maxArrayElements, trimFixedText, cancellationToken);
                    break;
                }

            case StaticReadKind.NestedArray:
                {
                    if (operation.Count > maxArrayElements)
                    {
                        throw new CStructReadLimitException(ReadFailures.ArrayLengthLimit(operation.Count, maxArrayElements));
                    }

                    var elements = new List<object?>(operation.Count);
                    destination[operation.Slot] = elements;
                    StaticReadPlan nestedPlan = operation.NestedPlan!;
                    StructShape nestedShape = operation.NestedComposite!.Shape;
                    int elementOffset = operation.Offset;
                    for (int element = 0; element < operation.Count; element++, elementOffset += nestedPlan.Size)
                    {
                        object?[] nestedSlots = nestedShape.Count == 0 ? Array.Empty<object?>() : new object?[nestedShape.Count];
                        var nested = new StructValue(nestedShape, nestedSlots);
                        elements.Add(nested);
                        ExecuteStaticPlan(nestedPlan, bytes.Slice(elementOffset, nestedPlan.Size), nestedSlots, maxArrayElements, trimFixedText, cancellationToken);
                    }

                    break;
                }
            }
        }
    }

    /// <summary>
    ///     Reads a whole fixed root directly and maps it to <typeparamref name="T"/>, with the conversion and failure
    ///     context of <see cref="ReadTypedValueCore{T}(Stream, string, IReadOnlyDictionary{string, int}?, ReadOptions?)"/>. A layout-bound mapped class generated for this very struct
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
            // The engine attaches the formatted path and the stream position after the root, its size.
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
        // normalize or reject, takes the compiled engine.
        if (string.IsNullOrEmpty(path) || path.AsSpan().IndexOfAny(".[] \t\r\n") >= 0 ||
            !this.compiledModelQueries.TryGetCompiledDeclaration(path, out CStructElement? declaration))
        {
            return false;
        }

        // The engine stores the root under the struct's own name or the typedef alias; the direct result is
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
