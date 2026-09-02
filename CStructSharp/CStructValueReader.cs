namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.IO;
using System.Linq;
using CStructSharp.Structure;

/// <summary>Reads natural scalar or composite values and projects them to caller-selected CLR types.</summary>
public sealed partial class CStruct
{
    private const DynamicallyAccessedMemberTypes TypedReadMembers =
        DynamicallyAccessedMemberTypes.PublicParameterlessConstructor |
        DynamicallyAccessedMemberTypes.PublicProperties |
        DynamicallyAccessedMemberTypes.PublicFields;

    /// <summary>Extracts a named value, with a one-value fallback for inline typedef roots.</summary>
    private static object? ExtractOnlyValue(ExpandoObject container, string preferredName)
    {
        var values = (IDictionary<string, object?>)container;
        if (values.TryGetValue(preferredName, out object? selected))
        {
            return selected;
        }

        if (values.Count == 1)
        {
            return values.Values.Single();
        }

        throw new CStructPathException("The selected layout element does not produce a readable value.");
    }

    /// <summary>
    ///     Reads the first declared struct or union in its natural representation. Structs use
    ///     <see cref="ExpandoObject"/> and unions use <see cref="UnionValue"/>.
    /// </summary>
    /// <param name="stream">The readable stream whose current position is the operation origin.</param>
    /// <returns>The first composite in its natural dynamic representation.</returns>
    /// <exception cref="CStructReadException">The stream cannot provide or decode the required bytes.</exception>
    public object? ReadValue(Stream stream)
    {
        return this.ReadValue(
            stream,
            this.GetFirstCompiledStructName(),
            null,
            null);
    }

    /// <summary>
    ///     Reads one root, field, array item, pointer accessor, or nested object without materializing unrelated
    ///     siblings. Optional integer variables are copied before traversal.
    /// </summary>
    /// <param name="stream">The readable stream whose current position is the operation origin.</param>
    /// <param name="elementNameOrPath">The case-sensitive exported declaration or nested field path to read.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer-coordinate settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The selected value in its natural dynamic, scalar, collection, pointer, enum, or union representation.</returns>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructReadException">The stream cannot provide or decode the required bytes.</exception>
    public object? ReadValue(
        Stream stream,
        string elementNameOrPath,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        return this.ReadValueCore(
            stream,
            elementNameOrPath,
            LayoutVariableInput.FromIntegers(variables),
            options);
    }

    /// <summary>
    ///     Reads the first declared struct or union and maps it to <typeparamref name="T"/>. Public writable properties
    ///     and fields are matched to struct members without regard to case; numeric and CLR-enum conversions are checked.
    /// </summary>
    /// <typeparam name="T">The destination type; supported POCOs require a public parameterless constructor and public bindable members.</typeparam>
    /// <param name="stream">The readable stream whose current position is the operation origin.</param>
    /// <returns>The first composite converted or bound to <typeparamref name="T"/>.</returns>
    /// <exception cref="CStructReadException">The bytes cannot be decoded or the result cannot be bound to <typeparamref name="T"/>.</exception>
    public T ReadValue<[DynamicallyAccessedMembers(TypedReadMembers)] T>(Stream stream)
    {
        return this.ReadValue<T>(
            stream,
            this.GetFirstCompiledStructName(),
            null,
            null);
    }

    /// <summary>
    ///     Reads one selected value and maps it to <typeparamref name="T"/>. Arrays and common generic list
    ///     abstractions are mapped element by element. Unsupported or lossy conversions fail with
    ///     <see cref="CStructReadException"/>.
    /// </summary>
    /// <typeparam name="T">The destination type; supported POCOs require a public parameterless constructor and public bindable members.</typeparam>
    /// <param name="stream">The readable stream whose current position is the operation origin.</param>
    /// <param name="elementNameOrPath">The case-sensitive exported declaration or nested field path to read.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer-coordinate settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The selected value converted or bound to <typeparamref name="T"/>.</returns>
    /// <exception cref="CStructPathException">The path is invalid or cannot be resolved.</exception>
    /// <exception cref="CStructReadException">The bytes cannot be decoded or the result cannot be bound to <typeparamref name="T"/>.</exception>
    public T ReadValue<[DynamicallyAccessedMembers(TypedReadMembers)] T>(
        Stream stream,
        string elementNameOrPath,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        IReadOnlyList<PathSegment> segments = CStructPathResolver.Parse(elementNameOrPath);
        try
        {
            object? naturalValue = this.ReadValueCore(
                stream,
                elementNameOrPath,
                LayoutVariableInput.FromIntegers(variables),
                options);
            return (T)TypedValueConverter.Convert(naturalValue, typeof(T), FormatPath(segments))!;
        }
        catch (CStructException exception)
        {
            AttachExceptionContext(exception, segments, stream);
            throw;
        }
    }

    /// <summary>
    ///     Attempts to read and map one selected value. Expected layout, path, read, or conversion failures return
    ///     <see langword="false"/> and restore the stream position from before the attempt. Invalid arguments and
    ///     unexpected runtime failures are not hidden.
    /// </summary>
    /// <typeparam name="T">The destination type; supported POCOs require a public parameterless constructor and public bindable members.</typeparam>
    /// <param name="stream">The readable, seekable stream whose position is restored after an expected failure.</param>
    /// <param name="elementNameOrPath">The case-sensitive exported declaration or nested field path to read.</param>
    /// <param name="value">Receives the typed result on success, or the default value of <typeparamref name="T"/> on failure.</param>
    /// <param name="variables">Optional per-operation integer layout variables; entries are snapshotted and never mutated.</param>
    /// <param name="options">Optional read limits and pointer-coordinate settings; <see langword="null"/> uses the documented defaults.</param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> for a categorized CStructSharp layout, path, read, or conversion failure.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable and seekable.</exception>
    public bool TryReadValue<[DynamicallyAccessedMembers(TypedReadMembers)] T>(
        Stream stream,
        string elementNameOrPath,
        [MaybeNullWhen(false)] out T value,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek)
        {
            throw new ArgumentException("Reading values requires a readable, seekable stream.", nameof(stream));
        }

        long initialPosition = stream.Position;
        try
        {
            value = this.ReadValue<T>(stream, elementNameOrPath, variables, options);
            return true;
        }
        catch (CStructException)
        {
            stream.Position = initialPosition;
            value = default;
            return false;
        }
    }

    /// <summary>
    ///     Attempts to read and map the first declared struct or union. Expected domain failures return
    ///     <see langword="false"/> and restore the stream position from before the attempt.
    /// </summary>
    /// <typeparam name="T">The destination type; supported POCOs require a public parameterless constructor and public bindable members.</typeparam>
    /// <param name="stream">The readable, seekable stream whose position is restored after an expected failure.</param>
    /// <param name="value">Receives the typed result on success, or the default value of <typeparamref name="T"/> on failure.</param>
    /// <returns><see langword="true"/> on success; <see langword="false"/> for a categorized CStructSharp layout, path, read, or conversion failure.</returns>
    /// <exception cref="ArgumentException"><paramref name="stream"/> is not readable and seekable.</exception>
    public bool TryReadValue<[DynamicallyAccessedMembers(TypedReadMembers)] T>(
        Stream stream,
        [MaybeNullWhen(false)] out T value)
    {
        return this.TryReadValue(
            stream,
            this.GetFirstCompiledStructName(),
            out value);
    }

    /// <summary>Reads one semantically resolved target through the compiled reader.</summary>
    internal object? ReadValueCore(
        Stream stream,
        string elementNameOrPath,
        LayoutVariableInput variables,
        ReadOptions? options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        IReadOnlyList<PathSegment> segments = CStructPathResolver.Parse(elementNameOrPath);
        ReadOperationSettings effectiveOptions = SnapshotReadOptions(options);
        Dictionary<string, Expr> effectiveVariables = variables.Resolve(this.layoutVariableResolver);
        var state = new CStructOperationContext(
            stream,
            effectiveVariables,
            this.Aligned,
            effectiveOptions);

        try
        {
            ResolvedTarget target = this.ResolveTargetFromLayout(state, segments);
            return this.ReadResolvedValue(state, target, segments[0].Name);
        }
        catch (CStructException exception)
        {
            AttachExceptionContext(exception, segments, stream);
            throw;
        }
    }

    /// <summary>Chooses the exact compiled decoder appropriate for one semantic target.</summary>
    private object? ReadResolvedValue(
        CStructOperationContext state,
        ResolvedTarget target,
        string rootName)
    {
        if (target.Kind == ResolvedTargetKind.Root)
        {
            return this.ReadRootValue(state, rootName);
        }

        if (target.Kind == ResolvedTargetKind.PointerAddress)
        {
            state.Stream.Position = target.Address;
            return this.ReadPointerAddress(state);
        }

        if (target.Kind == ResolvedTargetKind.PointerValue)
        {
            if (target.PointerTargetAddress == 0)
            {
                return null;
            }

            CompiledField pointerTarget = target.WritableCompiledField ??
                                          throw new InvalidOperationException(
                                              "Resolved pointer target has no compiled field.");
            state.Stream.Position = target.Address;
            state.StructureDepth = target.ContainingStructureDepth;
            state.PointerDereferenceDepth = target.PointerAccessorsConsumed;
            return target.RemainingPointerDepth > 0
                       ? this.ReadPointerValue(
                           target.RemainingPointerDepth,
                           pointerTarget,
                           state,
                           target.DebugPrefix.ToArray())
                       : this.ReadPointerTargetValue(
                           pointerTarget,
                           state,
                           target.DebugPrefix.ToArray());
        }

        bool isComposite = (!target.IsArray || target.SelectsArrayElement) &&
                           target.RemainingPointerDepth == 0 &&
                           target.TargetElement is Struct &&
                           target.EffectiveField?.PointerDepth == 0;
        if (isComposite)
        {
            return this.ParseCompiledStructAt(
                state,
                target.Address,
                (Struct)target.TargetElement!,
                target.DebugPrefix.ToArray(),
                target.ContainingStructureDepth,
                target.PointerAccessorsConsumed,
                false).Result;
        }

        CompiledField selectedField =
            (target.Kind == ResolvedTargetKind.ArrayElement
                 ? target.WritableCompiledField
                 : target.EffectiveCompiledField) ??
            throw new InvalidOperationException("Resolved field has no compiled decoder.");
        state.Stream.Position = target.Address;
        state.StructureDepth = target.ContainingStructureDepth;
        if (target.BitOffset > 0)
        {
            state.CurrentBitOffset = target.BitOffset;
            state.CurrentBitfieldType = selectedField.EffectiveField.Type.Name;
            state.CurrentBitfieldSize = target.BitStorageSize;
            state.CurrentFieldAlignment = selectedField.Alignment;
            state.NextPosition = checked(target.Address + target.BitStorageSize);
        }

        dynamic container = new ExpandoObject();
        this.HandleCStructElement(
            selectedField.EffectiveField,
            container,
            state,
            target.DebugPrefix.ToArray(),
            -1,
            false,
            selectedField);
        return ExtractOnlyValue(container, selectedField.EffectiveField.Name.Name);
    }

    /// <summary>Reads any supported root declaration and unwraps its single natural value.</summary>
    private object? ReadRootValue(CStructOperationContext state, string rootName)
    {
        if (!this.TryGetCompiledDeclaration(rootName, out CStructElement? declaration))
        {
            throw new CStructPathException("Unknown root element: " + rootName);
        }

        dynamic container = new ExpandoObject();
        this.HandleCStructElement(
            declaration,
            container,
            state,
            Array.Empty<CStructElement>());
        return ExtractOnlyValue(container, rootName);
    }
}
