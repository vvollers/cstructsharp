namespace CStructSharp.Compilation.Programs;

using CStructSharp.Codecs;

/// <summary>One element codec of a read or write program step, shared through <see cref="ProgramTables"/>.</summary>
/// <param name="CodecId">The field's catalog codec id, through which the engine finds a caller's codec instance or a writer delegate, or -1 for none.</param>
/// <param name="Primitive">The codec identity: kind, size and byte order.</param>
internal readonly record struct ProgramCodec(int CodecId, PrimitiveCodec Primitive);
