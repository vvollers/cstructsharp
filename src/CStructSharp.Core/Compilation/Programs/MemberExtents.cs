namespace CStructSharp.Compilation.Programs;

/// <summary>
///     What a program compiler knows, per member, about the position after it: a member of known size advances the
///     static placement (<see cref="Placement"/>), a member whose size the data decides restarts it from a new anchor.
///     Shared by <see cref="ReadProgramCompiler"/> and <see cref="WriteProgramCompiler"/>, because the reader and the
///     writer place the members of a struct by the same rule: each member starts where the previous one ended, aligned
///     from the struct's first byte.
/// </summary>
/// <remarks>
///     A caller's codec that declares a fixed size occupies exactly that size wherever it is read or written, so it
///     advances the placement like any fixed-size member; only a variable-size codec (and a struct holding one, which has
///     no fixed size) restarts it. Used by one compiler on one thread.
/// </remarks>
internal sealed class MemberExtents
{
    private readonly LayoutCompilation compilation;

    /// <summary>Creates the rules for one compilation request.</summary>
    /// <param name="compilation">The layout.</param>
    public MemberExtents(LayoutCompilation compilation)
    {
        this.compilation = compilation;
    }

    /// <summary>
    ///     A size a dynamic member's extent is always a multiple of, which carries alignment knowledge past it (see
    ///     <see cref="Placement.Restart"/>): the element size of an array whose count the data decides, or the
    ///     alignment of a struct in an aligned layout (its tail padding makes its size a multiple of it); otherwise 1.
    /// </summary>
    /// <param name="field">The member.</param>
    /// <param name="aligned">Whether the layout is aligned.</param>
    /// <returns>The unit in bytes.</returns>
    public static long ExtentUnit(CompiledField field, bool aligned)
    {
        bool array = field.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime or CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated;
        if (array && field.FixedElementSize is int size && size > 0)
        {
            return size;
        }

        return (array || field.Array.Kind == CompiledArrayKind.Scalar) && aligned && field.Composite is { } composite
                   ? composite.Symbol.Alignment
                   : 1;
    }

    /// <summary>Records where the position is after a member: a known size advances, a size the data decides restarts from a new anchor.</summary>
    /// <param name="placement">The placement state after the member was placed; the position's guarantee there is the member's start guarantee.</param>
    /// <param name="field">The member.</param>
    public void AdvancePast(ref Placement placement, CompiledField field)
    {
        if (field.FixedStorageSize is int size)
        {
            placement.Advance(size);
        }
        else
        {
            placement.Restart(ExtentUnit(field, this.compilation.Aligned));
        }
    }
}
