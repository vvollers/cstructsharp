namespace CStructSharp.Compilation.Programs;

using System.Collections.Generic;
using CStructSharp.Syntax;

/// <summary>
///     What a program compiler knows, per member, about the position after it: a member of known size advances the
///     static placement (<see cref="ReadPlacement"/>), a member whose size the data decides restarts it from a new anchor.
///     Shared by <see cref="ReadProgramCompiler"/> and <see cref="WriteProgramCompiler"/>, because the reader and the
///     writer place the members of a struct by the same rule: each member starts where the previous one ended, aligned
///     from the struct's first byte.
/// </summary>
/// <remarks>Used by one compiler on one thread; it caches which composites hold a caller's codec.</remarks>
internal sealed class MemberExtents
{
    private readonly LayoutCompilation compilation;

    // Whether each composite examined so far holds a caller's codec anywhere inside it (see ContainsCustomCodec); created
    // only for a layout that registers custom codecs.
    private Dictionary<CompiledCompositeType, bool>? customComposites;

    /// <summary>Creates the rules for one compilation request.</summary>
    /// <param name="compilation">The layout.</param>
    public MemberExtents(LayoutCompilation compilation)
    {
        this.compilation = compilation;
    }

    /// <summary>
    ///     A size a dynamic member's extent is always a multiple of, which carries alignment knowledge past it (see
    ///     <see cref="ReadPlacement.Restart"/>): the element size of an array whose count the data decides, or the
    ///     alignment of a struct in an aligned layout (its tail padding makes its size a multiple of it); otherwise 1.
    ///     Elements that hold a caller's codec have no size to rely on, only a struct's alignment.
    /// </summary>
    /// <param name="field">The member.</param>
    /// <param name="aligned">Whether the layout is aligned.</param>
    /// <param name="custom">Whether the member holds a caller's codec.</param>
    /// <returns>The unit in bytes.</returns>
    public static long ExtentUnit(CompiledField field, bool aligned, bool custom)
    {
        bool array = field.Array.Kind is CompiledArrayKind.Fixed or CompiledArrayKind.Runtime or CompiledArrayKind.ToEnd or CompiledArrayKind.Terminated;
        if (array && !custom && field.FixedElementSize is int size && size > 0)
        {
            return size;
        }

        return (array || field.Array.Kind == CompiledArrayKind.Scalar) && aligned && field.Composite is { } composite
                   ? composite.Symbol.Alignment
                   : 1;
    }

    /// <summary>
    ///     Records where the position is after a member: a known size advances, a size the data decides restarts from a
    ///     new anchor. A member holding a caller's codec restarts even when it declares a fixed size, because the
    ///     reader and writer continue from where the codec's bytes actually end.
    /// </summary>
    /// <param name="placement">The placement state after the member was placed; the position's guarantee there is the member's start guarantee.</param>
    /// <param name="field">The member.</param>
    public void AdvancePast(ref ReadPlacement placement, CompiledField field)
    {
        bool custom = this.ContainsCustomCodec(field);
        if (field.FixedStorageSize is int size && !custom)
        {
            placement.Advance(size);
        }
        else
        {
            placement.Restart(ExtentUnit(field, this.compilation.Aligned, custom));
        }
    }

    /// <summary>
    ///     Whether a member is read or written, anywhere inside it, by a caller's codec: the member's own codec, or a member
    ///     of the struct it holds (nested structs, arrays of them and promoted members included; a pointer's own storage has
    ///     a fixed size). Such a codec reports how many bytes a value took, which can differ from the size it declares.
    /// </summary>
    /// <param name="field">The member.</param>
    /// <returns>Whether the member's extent depends on a caller's codec.</returns>
    public bool ContainsCustomCodec(CompiledField field)
    {
        if (field.PointerDepth > 0 || this.compilation.Catalog.CustomCodecs.IsEmpty)
        {
            return false;
        }

        if (field.Codec.IsCustom)
        {
            return true;
        }

        CompiledCompositeType? composite = field.Composite ??
                                           (field.Declaration is Struct inline ? this.compilation.SizeQueries.GetCompiledComposite(inline) : null);
        if (composite is null)
        {
            return false;
        }

        this.customComposites ??= new Dictionary<CompiledCompositeType, bool>(ReferenceEqualityComparer.Instance);
        if (this.customComposites.TryGetValue(composite, out bool known))
        {
            return known;
        }

        // Marked first, so a composite reached again while its own members are examined answers without recursing.
        this.customComposites[composite] = false;
        bool custom = false;
        foreach (CompiledField member in composite.Fields)
        {
            if (this.ContainsCustomCodec(member))
            {
                custom = true;
                break;
            }
        }

        this.customComposites[composite] = custom;
        return custom;
    }
}
