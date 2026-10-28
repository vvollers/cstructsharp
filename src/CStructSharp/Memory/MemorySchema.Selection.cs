namespace CStructSharp.Memory;

/// <summary>Schema-only path selection and explicitly owned prepared typed readers.</summary>
public sealed partial class MemorySchema
{
    /// <summary>Prepares a path without accessing memory, including paths that require pointer reads when applied.</summary>
    /// <param name="typeId">Root type identity.</param>
    /// <param name="path">Relative member/index/pointer path; empty selects the root.</param>
    /// <returns>The immutable plan, including its constant offset when one exists.</returns>
    /// <exception cref="Diagnostics.CStructPathException">The type or path cannot be selected.</exception>
    public MemoryPath Select(string typeId, string path = "")
        => this.TrySelect(typeId, path, out MemoryPath? selection, out MemoryReadFailure? failure) ? selection! : throw failure!.ToException();

    /// <summary>Prepares a schema path without throwing for absent or ambiguous members or invalid traversal.</summary>
    /// <param name="typeId">Root type identity.</param>
    /// <param name="path">Relative path.</param>
    /// <param name="selection">Prepared plan on success; null otherwise.</param>
    /// <param name="failure">Structured explanation on failure; null otherwise.</param>
    /// <returns>Whether the schema defines this selection.</returns>
    public bool TrySelect(string typeId, string path, out MemoryPath? selection, out MemoryReadFailure? failure)
        => MemoryPath.TryCreate(this, typeId, path, out selection, out failure);

    /// <summary>Prepares a typed scalar or stored-pointer reader; binding never reads captured bytes or invokes custom decoders.</summary>
    /// <typeparam name="T">The exact result type for built-in 8/16/24/32/64-bit integers, bool, float32/float64, enum results, bit slices or StoredPointer. Other scalar codecs, including custom codecs, bind as object.</typeparam>
    /// <param name="typeId">Root type identity.</param>
    /// <param name="path">Relative scalar or pointer path.</param>
    /// <returns>A reusable reader retaining this schema, not operation state.</returns>
    /// <exception cref="Diagnostics.CStructPathException">The path is invalid or its result type differs from T.</exception>
    public MemoryFieldReader<T> Bind<T>(string typeId, string path = "")
        => this.TryBind(typeId, path, out MemoryFieldReader<T>? reader, out MemoryReadFailure? failure) ? reader! : throw failure!.ToException();

    /// <summary>Prepares a typed reader, returning absence, ambiguity and incompatible managed types as structured outcomes.</summary>
    /// <typeparam name="T">Exact scalar result type, StoredPointer for pointers, or object for arbitrary codecs.</typeparam>
    /// <param name="typeId">Root type identity.</param>
    /// <param name="path">Relative selected path.</param>
    /// <param name="reader">Prepared reader on success; null otherwise.</param>
    /// <param name="failure">Structured binding failure on false.</param>
    /// <returns>Whether a reader was created.</returns>
    public bool TryBind<T>(string typeId, string path, out MemoryFieldReader<T>? reader, out MemoryReadFailure? failure)
    {
        reader = null;
        if (!this.TrySelect(typeId, path, out MemoryPath? selection, out failure))
        {
            return false;
        }

        if (selection!.Type.Kind is not (MemoryTypeKind.Scalar or MemoryTypeKind.Pointer))
        {
            failure = new(MemoryReadFailureKind.WrongType, "A typed field reader requires scalar or pointer storage.", typeId + "." + path);
            return false;
        }

        MemoryScalarCodec codec = this.GetCodec(selection.Type, selection.Field, selection.ParentTypeId);
        Type resultType = selection.Type.Kind == MemoryTypeKind.Pointer ? typeof(StoredPointer) : codec.ResultType(selection.Field?.Signed == true);
        if (typeof(T) != typeof(object) && typeof(T) != resultType)
        {
            failure = new(MemoryReadFailureKind.WrongType, $"Selected codec returns '{resultType}', not '{typeof(T)}'.", typeId + "." + path);
            return false;
        }

        reader = new MemoryFieldReader<T>(selection, codec);
        return true;
    }
}
