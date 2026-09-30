namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using CStructSharp.Addressing;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Engine;
using CStructSharp.Expressions;
using CStructSharp.Streams;
using CStructSharp.Syntax;
using CStructSharp.Values;
using CStructSharp.Writing;

/// <summary>
///     Contains the writing half of <see cref="CStruct"/>: the entry points of <c>Serialize</c>, <c>Write</c> and
///     <c>Update</c>, which settle a write's options, variables, path and program before the compiled engine writes it
///     (<see cref="WriteEngine"/>), and the value rules the engine and the static write plans share.
/// </summary>
public sealed partial class CStruct
{
    /// <summary>The composite a struct root or an inline-struct typedef root writes; false for scalar roots.</summary>
    private bool TryGetRootComposite(CStructElement rootElement, [NotNullWhen(true)] out CompiledCompositeType? composite)
    {
        composite = rootElement switch
        {
            Struct s => this.compiledSizeQueries.GetCompiledComposite(s),
            Typedef { Struct: not null } t => this.compiledSizeQueries.GetCompiledComposite(t.Struct),
            _ => null,
        };
        return composite is not null;
    }

    /// <summary>Whether the data supplies at least one leaf of an anonymous promoted member (transitively).</summary>
    /// <param name="promoted">The anonymous member.</param>
    /// <param name="data">The data that would carry its members.</param>
    /// <returns>Whether any named member under it is supplied.</returns>
    internal static bool SuppliesAnyPromotedMember(CompiledField promoted, object data)
    {
        if (promoted.Type.Symbol.Definition is not CompiledCompositeType composite)
        {
            return false;
        }

        foreach (CompiledField member in composite.Fields)
        {
            if (composite.PromotedFields.Contains(member))
            {
                if (SuppliesAnyPromotedMember(member, data))
                {
                    return true;
                }

                continue;
            }

            string name = member.Name;
            if (name.Length > 0 && WriteDataBinding.TryGetMemberValue(data, name, out _))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    ///     <see cref="UnknownMemberPolicy.Reject"/>: every member the supplied value carries must be one the composite
    ///     declares, matched by exact key (the lookup the writer performs). A parsed <see cref="UnionValue"/> is
    ///     trusted; a mapped class was materialized into the composite's own shape and so cannot carry an unknown key.
    ///     The compiled engine calls it at every non-promoted struct it enters, and the direct fixed-root write for its root.
    /// </summary>
    /// <param name="composite">The struct whose declared members are allowed.</param>
    /// <param name="data">The struct's bound value.</param>
    /// <exception cref="CStructWriteException">The value carries a member the struct does not declare.</exception>
    internal static void RejectUnknownMembers(CompiledCompositeType composite, object data)
    {
        StructShape shape = composite.Shape;
        if (data is UnionValue)
        {
            return;
        }

        foreach (string key in WriteDataBinding.EnumerateMemberNames(data))
        {
            if (!shape.TryGetIndex(key, out _))
            {
                throw UnknownMember(composite, key);
            }
        }

        RejectUnknownNestedMembers(composite, data);
    }

    /// <summary>Applies the same check to every by-value nested struct the composite declares, arrays included.</summary>
    private static void RejectUnknownNestedMembers(CompiledCompositeType composite, object data)
    {
        foreach (CompiledField field in composite.Fields)
        {
            if (field.Composite is not { } nested)
            {
                continue;
            }

            if (composite.PromotedFields.Contains(field))
            {
                // A promoted member's children live on the same data object; only its own nested composites need checking.
                RejectUnknownNestedMembers(nested, data);
                continue;
            }

            if (field.IsUnnamed || !WriteDataBinding.TryGetMemberValue(data, field.Name, out object? value) || value is null)
            {
                continue;
            }

            try
            {
                if (field.Array.Kind == CompiledArrayKind.Scalar)
                {
                    RejectUnknownMembers(nested, WriteDataBinding.Materialize(value, nested));
                }
                else if (value is System.Collections.IEnumerable elements and not string)
                {
                    foreach (object? element in elements)
                    {
                        if (element is not null)
                        {
                            RejectUnknownMembers(nested, WriteDataBinding.Materialize(element, nested));
                        }
                    }
                }
            }
            catch (CStructException exception) when (exception.NoteMember(field.Name, field.DisplayTypeSpelling))
            {
                throw;
            }
        }
    }

    /// <summary>The failure of <see cref="UnknownMemberPolicy.Reject"/> for one member the composite does not declare.</summary>
    /// <param name="composite">The struct whose declared members are allowed.</param>
    /// <param name="member">The supplied member name.</param>
    /// <returns>The failure, naming the member and the declared ones.</returns>
    private static CStructWriteException UnknownMember(CompiledCompositeType composite, string member)
    {
        string declared = composite.Shape.Names.Length == 0 ? "no members" : string.Join(", ", composite.Shape.Names);
        return new CStructWriteException(
            $"'{member}' is not a member of '{composite.Name}' (WriteOptions.UnknownMembers is Reject). The layout declares: {declared}.");
    }

    /// <summary>The all-zero value an unnamed padding field is written with: a zero scalar, or one zero per fixed element.</summary>
    /// <param name="field">The unnamed field.</param>
    /// <returns>A new value: <c>0</c>, an empty string for characters, or an array of zeroes.</returns>
    internal static object CreatePaddingValue(CompiledField field)
    {
        if (field.Array.Kind == CompiledArrayKind.Scalar)
        {
            return 0;
        }

        if (field.IsCharacterArray)
        {
            return string.Empty;
        }

        var zeroes = new object[field.Array.TotalFixedElementCount ?? 0];
        Array.Fill(zeroes, 0);
        return zeroes;
    }

    /// <summary>
    ///     Materializes a caller-supplied N-deep nested collection into a flat, row-major list of leaf values,
    ///     validating that every level matches its declared dimension size exactly. Each level is materialized
    ///     with the same <see cref="WriteValueMaterialization.ConvertToObjectList"/> a single-dimension array
    ///     already uses once - this just repeats that call once per remaining dimension.
    /// </summary>
    /// <param name="value">The caller's nested collection.</param>
    /// <param name="dimensionSizes">The declared size of each remaining dimension, outermost first.</param>
    /// <param name="fieldName">The array field, named in a failure.</param>
    /// <returns>The leaves in row-major order.</returns>
    /// <exception cref="CStructWriteException">A level is not a collection or has a different number of elements.</exception>
    internal static List<object> FlattenNestedArrayValues(object value, IReadOnlyList<int> dimensionSizes, string fieldName)
    {
        IList<object> level = WriteValueMaterialization.ConvertToObjectList(value, dimensionSizes[0], fieldName);
        if (level.Count != dimensionSizes[0])
        {
            throw new CStructWriteException(WriteFailures.ArrayLengthMismatch(fieldName, dimensionSizes[0], level.Count));
        }

        if (dimensionSizes.Count == 1)
        {
            return level as List<object> ?? level.ToList();
        }

        int[] remainingDimensions = [.. dimensionSizes.Skip(1),];
        var flattened = new List<object>();
        foreach (object item in level)
        {
            flattened.AddRange(FlattenNestedArrayValues(item, remainingDimensions, fieldName));
        }

        return flattened;
    }

    /// <summary>Writes a pointer address after applying the configured absolute or relative addressing rule.</summary>
    /// <param name="stream">The destination, positioned at the pointer's storage.</param>
    /// <param name="address">The target's physical stream address, or 0 for a null pointer.</param>
    /// <param name="options">The write's addressing mode and origin.</param>
    private void WritePointerAddress(Stream stream, long address, WriteOptions options)
    {
        // Callers provide a physical signed stream address. The shared address domain applies relative conversion,
        // null semantics, and pointer-width validation before the stream receives any bytes.
        ulong value = CStructPointerArithmetic.EncodeTargetAddress(
            address,
            options.AddressingMode,
            options.Origin,
            this.PointerSize);

        // The shared primitive helper handles the layout byte order for every supported pointer width.
        BinaryPrimitiveIO.WriteUnsigned(stream, value, this.PointerSize, this.IsLittleEndian);
    }

    /// <summary>Encodes a caller value with a field's compiled primitive codec at the stream position.</summary>
    /// <remarks>
    ///     An in-place update of a variable-length (LEB128 or unsized custom) value must keep its existing encoded length.
    ///     Only expected caller-value conversion failures become <see cref="CStructWriteException"/>. The compiled engine
    ///     writes such a value in sparse update staging through this method too.
    /// </remarks>
    /// <param name="field">The field whose codec encodes the value.</param>
    /// <param name="stream">The destination, positioned at the value.</param>
    /// <param name="value">The caller value.</param>
    internal void WritePrimitiveValue(
        CompiledField field,
        Stream stream,
        object value)
    {
        Action<Stream, object> writer = this.codecs.WriterOf(field) ??
                                        throw new InvalidOperationException(
                                            "Compiled field has no writer: " + field.CodecName);
        try
        {
            if (stream is WriteBudgetStream { IsSparseUpdate: true } && (field.Codec.IsLeb128 || (field.Codec.IsCustom && !field.FixedElementSize.HasValue)))
            {
                long start = stream.Position;
                _ = this.codecs.ReaderOf(field)!(stream);
                long available = stream.Position - start;
                stream.Position = start;
                using var encoded = new MemoryStream();
                writer(encoded, value);
                if (encoded.Length != available)
                {
                    throw new CStructWriteException("LEB128 updates must preserve the existing encoded byte length.");
                }

                stream.Write(encoded.GetBuffer(), 0, (int)encoded.Length);
                return;
            }

            writer(stream, value);
        }
        catch (Exception exception) when (exception is ArgumentException or ArithmeticException or
                                          FormatException or InvalidCastException)
        {
            throw new CStructWriteException(DescribeUnwritableValue(value, field), exception);
        }
    }

    /// <summary>The shared unwritable-value text (<see cref="WriteFailures.UnwritableValue"/>) for one compiled field.</summary>
    /// <param name="value">The value that could not be encoded.</param>
    /// <param name="field">The field it was written as, whose type and accepted range the text names.</param>
    /// <returns>The failure message.</returns>
    internal static string DescribeUnwritableValue(object? value, CompiledField field)
        => WriteFailures.UnwritableValue(value, field.TypeSpelling, WriteFailures.AcceptedRange(field.Codec.Kind));

    /// <summary>
    ///     Creates a new byte array while snapshotting expression variables from a read-only caller view.
    /// </summary>
    /// <param name="elementNameOrPath">The case-sensitive root name or nested field path to encode.</param>
    /// <param name="data">The value to encode.</param>
    /// <param name="variables">The caller's layout variables; they are copied and never mutated.</param>
    /// <param name="options">Write limits and pointer settings; <see langword="null"/> uses the defaults.</param>
    /// <returns>A new caller-owned array holding exactly the encoded bytes.</returns>
    internal byte[] SerializeCore(
        string elementNameOrPath,
        object data,
        LayoutVariableInput variables,
        WriteOptions? options = null)
    {
        // Serialize is the convenience entry point: the compiled engine writes the root or path into a growable buffer and
        // hands its complete contents to the caller.
        WritePreparation request = this.PrepareWrite(null, elementNameOrPath, variables, options);
        try
        {
            return WriteEngine.SerializeToArray(this, request, data);
        }
        finally
        {
            request.Slots.Dispose();
        }
    }

    /// <summary>
    ///     Updates a selected value while snapshotting expression variables from a read-only caller view. All
    ///     library-detectable failures occur against bounded sparse staging before destination commit, and the
    ///     replacement cannot extend the existing stream.
    /// </summary>
    /// <param name="stream">
    ///     The caller-owned readable, writable, seekable stream whose current position is the operation origin.
    /// </param>
    /// <param name="elementNameOrPath">The case-sensitive path of the existing value to replace.</param>
    /// <param name="value">The replacement value.</param>
    /// <param name="variables">The caller's layout variables; they are copied and never mutated.</param>
    /// <param name="options">
    ///     Traversal limits, pointer rules, and union handling; <see langword="null"/> uses the documented defaults.
    /// </param>
    /// <exception cref="ArgumentException">The stream is not readable, writable, and seekable.</exception>
    /// <exception cref="CStructPathException">The path is empty or cannot be resolved.</exception>
    internal void UpdateStreamCore(
        Stream stream,
        string elementNameOrPath,
        object value,
        LayoutVariableInput variables,
        UpdateOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanSeek || !stream.CanWrite)
        {
            throw new ArgumentException("Updating requires a readable, writable, seekable stream.", nameof(stream));
        }

        // Updating needs a real path because it starts from bytes that already exist instead of creating a new root value.
        if (string.IsNullOrWhiteSpace(elementNameOrPath))
        {
            throw new CStructPathException("Path is empty.");
        }

        UpdateOptions effectiveOptions = WriteOptionSnapshots.SnapshotUpdateOptions(options);
        WriteOptionSnapshots.ValidateWriteOptions(effectiveOptions);

        // Copy caller variables and calculate #defines so array lengths are evaluated exactly as they are for normal
        // writes; a definition that cannot be resolved fails here, before the path is parsed.
        VariableSlots slots = VariableSlots.Create(this.compilation.SlotTable, variables);
        try
        {
            IReadOnlyList<PathSegment> segments = this.ParsePath(elementNameOrPath);
            if (segments.Count == 0)
            {
                throw new CStructPathException("Path is empty.");
            }

            string rootName = segments[0].Name;
            if (!this.compiledModelQueries.TryGetCompiledDeclaration(rootName, out _))
            {
                CStructPathException exception = this.compiledModelQueries.UnknownRoot(rootName);
                ExceptionContext.Attach(exception, segments, stream);
                throw exception;
            }

            EnginePrograms.Update();
            this.UpdateWithEngine(stream, segments, value, slots, effectiveOptions);
        }
        finally
        {
            slots.Dispose();
        }
    }

    /// <summary>
    ///     Writes a complete or selected value while snapshotting expression variables from a read-only caller view.
    /// </summary>
    /// <param name="stream">
    ///     The caller-owned writable, seekable destination; writing starts at its current position, and fields written
    ///     before a later failure remain in it.
    /// </param>
    /// <param name="elementNameOrPath">The case-sensitive root name or nested field path to write.</param>
    /// <param name="data">The value to encode.</param>
    /// <param name="variables">The caller's layout variables; they are copied and never mutated.</param>
    /// <param name="options">Write limits and pointer settings; <see langword="null"/> uses the defaults.</param>
    /// <exception cref="ArgumentException">The stream is not writable and seekable.</exception>
    /// <exception cref="CStructPathException">The path is empty or cannot be resolved.</exception>
    internal void WriteStreamCore(
        Stream stream,
        string elementNameOrPath,
        object data,
        LayoutVariableInput variables,
        WriteOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanWrite || !stream.CanSeek)
        {
            throw new ArgumentException("Writing requires a writable, seekable stream.", nameof(stream));
        }

        WritePreparation request = this.PrepareWrite(stream, elementNameOrPath, variables, options);
        try
        {
            WriteEngine.WriteToStream(this, request, stream, data);
        }
        finally
        {
            request.Slots.Dispose();
        }
    }

    /// <summary>
    ///     Settles everything a write decides before it writes, in this order: the path is present, the options are
    ///     snapshotted and valid, the variables resolve into the layout's slots (a definition that cannot be resolved fails
    ///     here, before the path is parsed), the root exists, and the program is looked up once. A nested path keeps the
    ///     segments after its root, and the variables as a dictionary for its index checks.
    /// </summary>
    /// <param name="stream">
    ///     The caller's destination, whose position an unknown-root failure reports; <see langword="null"/> for
    ///     <c>Serialize</c>, whose new destination is at position 0.
    /// </param>
    /// <param name="elementNameOrPath">The case-sensitive root name or nested field path to write.</param>
    /// <param name="variables">The caller's layout variables; they are copied and never mutated.</param>
    /// <param name="options">Write limits and pointer settings; <see langword="null"/> uses the defaults.</param>
    /// <returns>The settled write, with the variables as slots, which the caller disposes.</returns>
    /// <exception cref="CStructPathException">The path is empty or names no root.</exception>
    private WritePreparation PrepareWrite(Stream? stream, string elementNameOrPath, LayoutVariableInput variables, WriteOptions? options)
    {
        // Validate the requested root before touching the stream so bad paths fail without partial output.
        if (string.IsNullOrWhiteSpace(elementNameOrPath))
        {
            throw new CStructPathException("Path is empty.");
        }

        WriteOptions effectiveOptions = WriteOptionSnapshots.SnapshotWriteOptions(options);
        WriteOptionSnapshots.ValidateWriteOptions(effectiveOptions);

        // Definitions and supplied variables form the small expression environment used for array counts.
        VariableSlots slots = VariableSlots.Create(this.compilation.SlotTable, variables);
        try
        {
            IReadOnlyList<PathSegment> segments = this.ParsePath(elementNameOrPath);
            if (segments.Count == 0)
            {
                throw new CStructPathException("Path is empty.");
            }

            string rootName = segments[0].Name;
            if (!this.compiledModelQueries.TryGetCompiledDeclaration(rootName, out CStructElement? rootElement))
            {
                CStructPathException exception = this.compiledModelQueries.UnknownRoot(rootName);
                if (stream is null)
                {
                    ExceptionContext.Attach(exception, segments, 0);
                }
                else
                {
                    ExceptionContext.Attach(exception, segments, stream);
                }

                throw exception;
            }

            PathSegment[]? childSegments = segments.Count > 1 ? segments.Skip(1).ToArray() : null;
            WriteProgramOutcome outcome = EnginePrograms.Write(this.compilation, segments, childSegments, rootElement);

            // A nested path's indexes are checked against counts in the resolved variables when the write runs.
            Dictionary<string, Expr>? pathVariables = childSegments is null ? null : variables.Resolve(this.layoutVariableResolver);
            return new WritePreparation(effectiveOptions, segments, childSegments, rootElement, pathVariables, slots, outcome.Program, outcome.Reason);
        }
        catch
        {
            slots.Dispose();
            throw;
        }
    }

    /// <summary>
    ///     Selects what a write of a nested path writes, in this order, before anything is written: a
    ///     mapped-class root becomes a struct value, the value at the path is taken from a complete root value (the data is
    ///     the value itself when it lacks the path's first member), and the layout member the path selects is resolved, its
    ///     indexes checked against the counts the variables give.
    /// </summary>
    /// <param name="rootElement">The path's root declaration.</param>
    /// <param name="childSegments">The segments after the root; at least one.</param>
    /// <param name="rootData">The normalized root data, or <see langword="null"/>.</param>
    /// <param name="variables">The operation's resolved variables, before anything is written.</param>
    /// <param name="target">The member the path selects, narrowed by its indexes.</param>
    /// <returns>The value to write as <paramref name="target"/>.</returns>
    /// <exception cref="CStructPathException">The path selects no writable member, or an index is out of range.</exception>
    /// <exception cref="CStructWriteException">The root value lacks a member or element the path names.</exception>
    internal object SelectWrittenPathValue(CStructElement rootElement, IReadOnlyList<PathSegment> childSegments, object rootData, Dictionary<string, Expr> variables, out CompiledField target)
    {
        // A mapped-class root becomes a StructValue first so its members can be walked like any other root object.
        if (rootData is not null && !WriteDataBinding.IsMemberSource(rootData) && this.TryGetRootComposite(rootElement, out CompiledCompositeType? rootComposite))
        {
            rootData = WriteDataBinding.Materialize(rootData, rootComposite)!;
        }

        object subData = rootData!;
        if (WriteDataBinding.TryGetMemberValue(rootData!, childSegments[0].Name, out _))
        {
            // If the caller supplied a complete root object, walk down to the matching nested source value.
            subData = WriteDataBinding.ResolveDataPath(rootData!, childSegments);
        }

        // Separately resolve the layout shape so the writer knows whether the selected target is a field, struct, or typedef.
        target = this.compilation.ResolveElementPath(rootElement, childSegments, variables);
        return subData;
    }
}
