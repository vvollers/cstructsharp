namespace CStructSharp.Compilation.Programs;

/// <summary>
///     The move a <see cref="Placement"/> asks for before a member: a forward seek over padding whose size is known, or an
///     alignment the executor computes because the padding depends on the data. The read and write builders emit it as
///     their own <c>Seek</c> or <c>Align</c> step.
/// </summary>
/// <param name="Aligns">
///     <see langword="true"/> for an alignment computed at run time; <see langword="false"/> for a seek over known padding.
/// </param>
/// <param name="Field">The member's index, the emitted step's field.</param>
/// <param name="Amount">The padding in bytes for a seek; the alignment in bytes for an alignment.</param>
internal readonly record struct PlacementStep(bool Aligns, int Field, int Amount);
