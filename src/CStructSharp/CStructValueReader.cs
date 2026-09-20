namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using CStructSharp.Addressing;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Reading;
using CStructSharp.Syntax;
using CStructSharp.Values;

/// <summary>Reads natural scalar or composite values and projects them to caller-selected CLR types.</summary>
public sealed partial class CStruct
{
    /// <summary>Extracts a named value, with a one-value fallback for inline typedef roots.</summary>
    private static object? ExtractOnlyValue(StructValue container, string preferredName)
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
                elementNameOrPath,
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
    internal object? ReadValueCore(
        Stream stream,
        string elementNameOrPath,
        LayoutVariableInput variables,
        ReadOptions? options)
    {
        ArgumentNullException.ThrowIfNull(stream);
        IReadOnlyList<PathSegment> segments = this.ParsePath(elementNameOrPath);
        ReadOperationSettings effectiveOptions = ReadOperationSettings.SnapshotReadOptions(options);
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
            state.Complete();
            ExceptionContext.Attach(exception, segments, stream);
            throw;
        }
        finally
        {
            state.Complete();
        }
    }

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
        if (target.BitStorageSize > 0)
        {
            state.CurrentBitOffset = target.BitOffset;
            state.CurrentBitfieldType = selectedField.BitUnitType;
            state.CurrentBitfieldSize = target.BitStorageSize;
            state.CurrentFieldAlignment = selectedField.Alignment;
            state.NextPosition = checked(target.Address + target.BitStorageSize);
            state.BitfieldUnitSeeded = true;
        }

        var container = new StructValue(this.compiledModelQueries.GetRootShape(selectedField.Name));
        this.HandleCStructElement(
            selectedField.EffectiveField,
            container,
            state,
            null,
            -1,
            false,
            selectedField);
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
