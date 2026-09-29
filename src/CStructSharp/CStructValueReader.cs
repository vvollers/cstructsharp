namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using CStructSharp.Addressing;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Syntax;
using CStructSharp.Values;

/// <summary>Reads natural scalar or composite values and projects them to caller-selected CLR types.</summary>
public sealed partial class CStruct
{
    /// <summary>Extracts a named value, with a one-value fallback for inline typedef roots.</summary>
    /// <param name="container">The one-member value a root or a selected member was read into.</param>
    /// <param name="preferredName">The name the value is stored under.</param>
    /// <returns>The value under <paramref name="preferredName"/>, or the only value the container holds.</returns>
    /// <exception cref="CStructPathException">The container holds no value under the name and not exactly one value.</exception>
    internal static object? ExtractOnlyValue(StructValue container, string preferredName)
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

    /// <summary>Reads one selected value through the compiled reader and maps it to <typeparamref name="T"/>.</summary>
    /// <typeparam name="T">The CLR type the value is converted to.</typeparam>
    /// <param name="stream">The source, positioned at the operation origin.</param>
    /// <param name="elementNameOrPath">The declaration name or member path, such as <c>header.size</c>.</param>
    /// <param name="variables">The caller's integer layout variables, or <see langword="null"/> for none.</param>
    /// <param name="options">The read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The selected value converted to <typeparamref name="T"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is <see langword="null"/>.</exception>
    internal T ReadTypedValueCore<T>(
        Stream stream,
        string elementNameOrPath,
        IReadOnlyDictionary<string, int>? variables = null,
        ReadOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        IReadOnlyList<PathSegment> segments = this.ParsePath(elementNameOrPath);
        try
        {
            // The natural value is converted to T: a checked scalar conversion, an array, a value object, or a
            // registered mapped class (ICStructMapped<T>) built from the parsed composite.
            object? naturalValue = this.ReadValueCore(
                stream,
                segments,
                LayoutVariableInput.FromIntegers(variables),
                options);
            return (T)TypedValueConverter.Convert(naturalValue, typeof(T), ExceptionContext.FormatPath(segments))!;
        }
        catch (CStructException exception)
        {
            ExceptionContext.Attach(exception, segments, stream);
            throw;
        }
    }

    /// <summary>Reads one semantically resolved target through the compiled reader.</summary>
    /// <param name="stream">The source, at the operation origin.</param>
    /// <param name="elementNameOrPath">The path of the value.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The read options, or the defaults.</param>
    /// <returns>The value.</returns>
    internal object? ReadValueCore(
        Stream stream,
        string elementNameOrPath,
        LayoutVariableInput variables,
        ReadOptions? options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        return this.ReadValueCore(stream, this.ParsePath(elementNameOrPath), variables, options);
    }

    /// <summary>Reads the value a parsed path selects through the compiled reader.</summary>
    /// <param name="stream">The source, at the operation origin.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The read options, or the defaults.</param>
    /// <returns>The value.</returns>
    private object? ReadValueCore(
        Stream stream,
        IReadOnlyList<PathSegment> segments,
        LayoutVariableInput variables,
        ReadOptions? options)
    {
        ReadOperationSettings effectiveOptions = ReadOperationSettings.SnapshotReadOptions(options);
        if (this.SelectValueRead(effectiveOptions, segments, variables) is { } program)
        {
            // The caller's variables are resolved into the layout's slots first (a definition that cannot be resolved
            // fails here, without a path), then the source is validated and the path read, as the interpreter orders them.
            VariableSlots slots = VariableSlots.Create(this.compilation.SlotTable, variables);
            try
            {
                return ReadEngine.ReadValue(this, stream, segments, program, slots, effectiveOptions);
            }
            finally
            {
                slots.Dispose();
            }
        }

        return this.ReadValueWithInterpreter(stream, segments, variables, effectiveOptions);
    }

    /// <summary>
    ///     Makes the one engine decision of a selected read: a bare root asks for the root's program as a root read, a nested
    ///     path for its root's program as a path read.
    /// </summary>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <returns>The root's program when the engine runs the read; otherwise <see langword="null"/>.</returns>
    /// <exception cref="InvalidOperationException">The engine is required and declined the read.</exception>
    private ReadProgram? SelectValueRead(in ReadOperationSettings options, IReadOnlyList<PathSegment> segments, in LayoutVariableInput variables)
    {
        if (segments.Count == 1)
        {
            return EngineSelector.SelectRootRead(options.EngineSelection, this.compilation, segments[0].Name);
        }

        return EngineSelector.SelectPathRead(options.EngineSelection, this.compilation, segments[0].Name, EngineOperation.PathRead);
    }

    /// <summary>
    ///     Reads the natural value a path selects from a pinned memory region with the compiled engine: a bare root's value
    ///     under its name (or the only value the root holds, as the interpreter extracts it), or the value a longer path
    ///     reaches. The caller's variables are resolved into the layout's slots first, then the settings are validated.
    /// </summary>
    /// <param name="region">The input's byte 0; the caller keeps it pinned until the method returns.</param>
    /// <param name="length">The input length in bytes.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="program">The eligible program of the path's root.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="position">The position the read ended at, in bytes from the region's start.</param>
    /// <returns>The value.</returns>
    /// <exception cref="CStructException">The path cannot be resolved, the input cannot be read, or a root produces no single value; the path and offset are attached.</exception>
    private unsafe object? ReadValueWithEngine(byte* region, int length, IReadOnlyList<PathSegment> segments, ReadProgram program, in LayoutVariableInput variables, in ReadOperationSettings options, out long position)
    {
        VariableSlots slots = VariableSlots.Create(this.compilation.SlotTable, variables);
        try
        {
            return ReadEngine.ReadValue(this, region, length, segments, program, slots, options, out position);
        }
        finally
        {
            slots.Dispose();
        }
    }

    /// <summary>Reads the value a parsed path selects with the interpreter, after the engine declined the operation.</summary>
    /// <param name="stream">The source, at the operation origin.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="effectiveOptions">The operation's snapshotted settings.</param>
    /// <returns>The value.</returns>
    private object? ReadValueWithInterpreter(
        Stream stream,
        IReadOnlyList<PathSegment> segments,
        LayoutVariableInput variables,
        ReadOperationSettings effectiveOptions)
    {
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
            state.CompleteWithContext(exception, segments, stream);
            throw;
        }
        finally
        {
            state.Complete();
        }
    }

    /// <summary>Decodes a resolved selection at its exact address without applying its parent placement again.</summary>
    /// <param name="state">The stream, limits and variables for this read.</param>
    /// <param name="target">The resolved field, array item, pointer level or root.</param>
    /// <param name="rootName">The exported declaration name used for a root selection.</param>
    /// <returns>The natural decoded value, or null for a null pointer target.</returns>
    /// <remarks>Pointer levels followed while selecting the target remain part of the read's depth budget.</remarks>
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
                           null)
                       : this.ReadPointerTargetValue(
                           pointerTarget,
                           state,
                           null);
        }

        bool isComposite = (!target.IsArray || target.SelectsArrayElement) &&
                           target.RemainingPointerDepth == 0 &&
                           target.TargetComposite is not null &&
                           target.EffectiveCompiledField?.PointerDepth == 0;
        if (isComposite)
        {
            return this.ParseCompiledStructAt(
                state,
                target.Address,
                target.TargetComposite!,
                null,
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

        // Resolving the path has already followed these levels before decoding this field or array element.
        state.PointerDereferenceDepth = target.PointerAccessorsConsumed;
        if (target.BitStorageSize > 0)
        {
            state.CurrentBitOffset = target.BitOffset;
            state.BitfieldUnitOpen = true;
            state.CurrentBitfieldSize = target.BitStorageSize;
            state.NextPosition = checked(target.Address + target.BitStorageSize);
            state.BitfieldUnitSeeded = true;
        }

        var container = new StructValue(this.compiledModelQueries.GetRootShape(selectedField.Name));

        // Resolution already placed the whole field. Aligning again can move an unaligned union view, or
        // incorrectly add alignment padding between the elements of a selected enum array.
        this.ReadField(selectedField, container, state, null, -1, null, positionIsResolvedTarget: true);
        return ExtractOnlyValue(container, selectedField.Name);
    }

    /// <summary>Reads any supported root declaration and unwraps its single natural value.</summary>
    private object? ReadRootValue(CStructOperationContext state, string rootName)
    {
        if (!this.compiledModelQueries.TryGetCompiledDeclaration(rootName, out CStructElement? declaration))
        {
            throw this.compiledModelQueries.UnknownRoot(rootName);
        }

        var container = new StructValue(this.compiledModelQueries.GetRootShape(rootName));
        this.HandleCStructElement(
            declaration,
            container,
            state,
            null);
        return ExtractOnlyValue(container, rootName);
    }
}
