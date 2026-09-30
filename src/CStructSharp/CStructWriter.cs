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
///     (<see cref="WriteEngine"/>), and the primitive and pointer writes the engine delegates to the layout. The value rules
///     every write shares are in <see cref="WriteValueRules"/>.
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
        Action<Stream, object> writer = this.codecs.WriterOfCodec(field.CodecId) ??
                                        throw new InvalidOperationException(
                                            "Compiled field has no writer: " + field.CodecName);
        try
        {
            if (stream is WriteBudgetStream { IsSparseUpdate: true } && (field.Codec.IsLeb128 || (field.Codec.IsCustom && !field.FixedElementSize.HasValue)))
            {
                long start = stream.Position;
                this.SkipVariableLengthValue(field, stream);
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
            throw new CStructWriteException(WriteValueRules.DescribeUnwritableValue(value, field), exception);
        }
    }

    /// <summary>
    ///     Reads past the existing variable-length value at the stream position, so the caller learns its encoded length:
    ///     a LEB128 integer through the shared LEB128 decoder, an unsized custom value through its codec's adapter.
    /// </summary>
    /// <param name="field">The LEB128 or unsized custom field.</param>
    /// <param name="stream">The source, positioned at the value; left after it.</param>
    /// <exception cref="CStructReadException">The existing value is truncated or malformed.</exception>
    private void SkipVariableLengthValue(CompiledField field, Stream stream)
    {
        if (field.Codec.IsCustom)
        {
            _ = CustomCodecAdapter.Read(this.codecs.CustomCodecOf(field.CodecId), stream);
            return;
        }

        PrimitiveCodecKind kind = field.Codec.Kind;
        _ = Leb128Codec.Read(
            stream,
            kind is PrimitiveCodecKind.ULeb128_32 or PrimitiveCodecKind.SLeb128_32 ? 32 : 64,
            kind is PrimitiveCodecKind.SLeb128_32 or PrimitiveCodecKind.SLeb128_64);
    }

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
    ///     indexes checked against the counts the variables give. The write's program already resolved the path's shape;
    ///     a path whose shape resolved is walked again only when it has indexes to check, and a path whose shape did not
    ///     resolve is walked again here, where its resolution reports the failure.
    /// </summary>
    /// <param name="rootElement">The path's root declaration.</param>
    /// <param name="childSegments">The segments after the root; at least one.</param>
    /// <param name="rootData">The normalized root data, or <see langword="null"/>.</param>
    /// <param name="variables">The operation's resolved variables, before anything is written.</param>
    /// <param name="shapeResolved">Whether the write's program resolved the path's shape (it has a program).</param>
    /// <returns>The value to write as the member the path selects.</returns>
    /// <exception cref="CStructPathException">The path selects no writable member, or an index is out of range.</exception>
    /// <exception cref="CStructWriteException">The root value lacks a member or element the path names.</exception>
    internal object SelectWrittenPathValue(CStructElement rootElement, IReadOnlyList<PathSegment> childSegments, object rootData, Dictionary<string, Expr> variables, bool shapeResolved)
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

        // Resolving the path with the variables adds only the index range checks to the shape the program resolved.
        if (!shapeResolved || HasIndexes(childSegments))
        {
            _ = this.compilation.ResolveElementPath(rootElement, childSegments, variables);
        }

        return subData;
    }

    /// <summary>Whether any segment of a path indexes an array.</summary>
    /// <param name="segments">The path segments.</param>
    /// <returns><see langword="true"/> when a segment has at least one index.</returns>
    private static bool HasIndexes(IReadOnlyList<PathSegment> segments)
    {
        for (int index = 0; index < segments.Count; index++)
        {
            if (segments[index].Indexes.Count > 0)
            {
                return true;
            }
        }

        return false;
    }
}
