namespace CStructSharp;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using CStructSharp.Structure;

/// <summary>
///     Roots named by a type spelling instead of a declaration: <c>uint32</c>, <c>DWORD</c>, <c>uint64[4]</c>,
///     <c>uint16[N]</c>, <c>char[]</c>, <c>uint8[EOF]</c>, <c>entry[2]</c>. Each is compiled once per layout on
///     first use into the same root-field projection a typedef gets, so the readers and writers never see the
///     difference. A declared name always wins over a spelling.
/// </summary>
public partial class CStruct
{
    private const int PathCacheCapacity = 256;

    private readonly ConcurrentDictionary<string, IReadOnlyList<PathSegment>> pathCache = new(StringComparer.Ordinal);

    /// <summary>
    ///     Parses an element name or path, resolving a type-spelling root on the way. Results are cached per layout
    ///     (bounded like the shared path cache), so a repeated operation pays one lookup, exactly as before.
    /// </summary>
    internal IReadOnlyList<PathSegment> ParsePath(string elementNameOrPath)
    {
        if (elementNameOrPath is not null && this.pathCache.TryGetValue(elementNameOrPath, out IReadOnlyList<PathSegment>? cached))
        {
            return cached;
        }

        IReadOnlyList<PathSegment> segments;
        try
        {
            segments = CStructPathResolver.Parse(elementNameOrPath!);
        }
        catch (CStructPathException) when (elementNameOrPath is not null && this.TryCreateSyntheticRoot(elementNameOrPath, out IReadOnlyList<PathSegment>? synthetic))
        {
            // `uint16[N]` or `uint8[EOF]` is not a path (its index is not a literal) but is a root spelling.
            segments = synthetic;
        }

        // A root is never an array of itself, so `entry[2]` names two `entry` records; a bare declared name stays
        // the declaration.
        if (segments.Count == 1 &&
            (segments[0].Indexes.Count > 0 || !this.compiledLayout.Declarations.ContainsKey(segments[0].Name)) &&
            this.TryCreateSyntheticRoot(elementNameOrPath!, out IReadOnlyList<PathSegment>? spelled))
        {
            segments = spelled;
        }

        if (this.pathCache.Count < PathCacheCapacity)
        {
            this.pathCache.TryAdd(elementNameOrPath!, segments);
        }

        return segments;
    }

    /// <summary>Compiles a root from a type spelling with optional dimensions; false when the text is not one.</summary>
    private bool TryCreateSyntheticRoot(string spelling, out IReadOnlyList<PathSegment> segments)
    {
        segments = Array.Empty<PathSegment>();
        if (this.compiledModelQueries.TryGetSyntheticRoot(spelling, out _))
        {
            segments = [new PathSegment(spelling, Array.Empty<int>()),];
            return true;
        }

        string trimmed = spelling.Trim();
        if (trimmed.Length == 0 || trimmed.Contains('.'))
        {
            return false;
        }

        int bracket = trimmed.IndexOf('[');
        string head = (bracket < 0 ? trimmed : trimmed[..bracket]).Trim();
        string dimensions = bracket < 0 ? string.Empty : trimmed[bracket..];
        if (head.Length == 0 || (bracket < 0 && this.compiledLayout.Declarations.ContainsKey(head)))
        {
            return false;
        }

        IReadOnlyList<Field> fields;
        try
        {
            fields = CStructDefinitionParser.ParseFieldGroup(head + " value" + dimensions + ";");
        }
        catch (CStructLayoutException)
        {
            return false;
        }

        if (fields.Count != 1 || fields[0].BitSize != 0)
        {
            return false;
        }

        Field parsed = fields[0];
        if (!this.compiledLayout.Symbols.TryGetValue(parsed.Type.Name, out CompiledTypeReference type))
        {
            return false;
        }

        CompiledField compiled = this.CompileSyntheticRootField(spelling, parsed, type);
        var declaration = new Typedef(new Identifier(spelling), new Identifier(parsed.Type.Name + new string('*', parsed.PointerDepth)))
        {
            ArrayShape = parsed.ArrayCount,
        };
        this.compiledModelQueries.RegisterSyntheticRoot(spelling, declaration, compiled);
        segments = [new PathSegment(spelling, Array.Empty<int>()),];
        return true;
    }

    /// <summary>The same projection a typedef root gets, built from the resolved type and the spelled dimensions.</summary>
    private CompiledField CompileSyntheticRootField(string spelling, Field parsed, CompiledTypeReference type)
    {
        int pointerDepth = checked(parsed.PointerDepth + type.PointerDepth);
        if (pointerDepth == 0 && type.TerminalName == "void")
        {
            throw new CStructPathException("void has no storage of its own; declare a pointer to it: " + spelling);
        }

        IReadOnlyList<Expr> arrayCount = parsed.ArrayCount;
        foreach (Expr dimension in arrayCount)
        {
            if (!ReferenceEquals(dimension, Field.UnknownArraysize))
            {
                if (ExpressionEvaluator.ContainsCall(dimension))
                {
                    throw new CStructPathException("A root spelling's dimension cannot use sizeof or offsetof: " + spelling);
                }

                this.expressionEvaluator.Compile(dimension);
            }
        }

        var field = new Field(new Identifier(type.TerminalName), new Identifier(spelling), arrayCount, 0, pointerDepth);
        bool isUnsizedCharacterArray = arrayCount.Count == 1 && ReferenceEquals(arrayCount[0], Field.UnknownArraysize) && CharacterFieldTypes.IsCharArrayField(field);
        Func<Stream, object>? terminatedReader = null;
        Action<Stream, object>? terminatedWriter = null;
        if (isUnsizedCharacterArray || (pointerDepth > 0 && CharacterFieldTypes.IsStringPointerType(field.Type)))
        {
            string handler = CharacterFieldTypes.GetStringPointerHandlerKey(field.Type);
            terminatedReader = this.fieldHandlers[handler];
            terminatedWriter = this.writeHandlers[handler];
        }

        int alignment = pointerDepth > 0 ? this.PointerSize : type.Symbol.Alignment;
        int? elementSize = pointerDepth > 0 ? this.PointerSize : type.Symbol.FixedSize;
        CompiledArrayShape arrayShape;
        try
        {
            arrayShape = this.CompileArrayShape(field);
        }
        catch (CStructLayoutException exception)
        {
            throw new CStructPathException("Invalid root spelling '" + spelling + "': " + exception.Message, exception);
        }

        if (arrayShape.Kind is CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated && !elementSize.HasValue)
        {
            throw new CStructPathException("A data-sized root needs a fixed-size element type: " + spelling);
        }

        int? storageSize = elementSize.HasValue && arrayShape.TotalFixedElementCount.HasValue
                               ? checked(elementSize.Value * arrayShape.TotalFixedElementCount.Value)
                               : null;
        return new CompiledField(
            field,
            field,
            type,
            this.GetCompiledReader(type.Symbol),
            this.GetCompiledWriter(type.Symbol),
            terminatedReader,
            terminatedWriter,
            alignment,
            elementSize,
            arrayShape,
            storageSize,
            isUnsizedCharacterArray,
            null,
            null,
            null,
            0,
            this.IsLittleEndian);
    }
}
