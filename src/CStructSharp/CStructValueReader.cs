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
        ReadProgram? program = this.SelectValueRead(segments);

        // The caller's variables are resolved into the layout's slots first (a definition that cannot be resolved fails
        // here, without a path), then the source is validated and the path read.
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

    /// <summary>
    ///     Looks up the program of a selected read once: a bare root is read by the root's program, a nested path by the
    ///     programs the path resolver finds as it walks.
    /// </summary>
    /// <param name="segments">The parsed path.</param>
    /// <returns>
    ///     The root's program for a bare root; <see langword="null"/> for a nested path, and for a root the layout does not
    ///     declare, which the read reports as it resolves the path.
    /// </returns>
    private ReadProgram? SelectValueRead(IReadOnlyList<PathSegment> segments)
    {
        if (segments.Count == 1)
        {
            return EnginePrograms.RootRead(this.compilation, segments[0].Name);
        }

        EnginePrograms.PathOperation(EngineOperation.PathRead);
        return null;
    }

    /// <summary>
    ///     Reads the natural value a path selects from a pinned memory region with the compiled engine: a bare root's value
    ///     under its name (or the only value the root holds), or the value a longer path
    ///     reaches. The caller's variables are resolved into the layout's slots first, then the settings are validated.
    /// </summary>
    /// <param name="region">The input's byte 0; the caller keeps it pinned until the method returns.</param>
    /// <param name="length">The input length in bytes.</param>
    /// <param name="segments">The parsed path.</param>
    /// <param name="program">The root's program for a bare root; <see langword="null"/> for a nested path or an undeclared root.</param>
    /// <param name="variables">The caller's layout variables.</param>
    /// <param name="options">The operation's snapshotted settings.</param>
    /// <param name="position">The position the read ended at, in bytes from the region's start.</param>
    /// <returns>The value.</returns>
    /// <exception cref="CStructException">The path cannot be resolved, the input cannot be read, or a root produces no single value; the path and offset are attached.</exception>
    private unsafe object? ReadValueWithEngine(byte* region, int length, IReadOnlyList<PathSegment> segments, ReadProgram? program, in LayoutVariableInput variables, in ReadOperationSettings options, out long position)
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
}
