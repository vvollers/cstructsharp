namespace CStructSharp.Compilation.Programs;

using CStructSharp.Codecs;

/// <summary>
///     What a pointer field's final target is and how to read it, decided when the program is built, checking in this
///     order: a counted target, an enum, a struct or union, terminated text, then any other value
///     through its codec. A <see cref="ReadOpCode.ReadPointer"/> or <see cref="ReadOpCode.ReadPointerArray"/> step names
///     one (<see cref="ReadProgram.PointerTargets"/>), and a deferred pointer carries it until it is followed.
/// </summary>
/// <remarks>
///     <para>
///         A struct or union target is not compiled with the program that points to it - a linked list points to itself -
///         so <see cref="Composite"/> names the composite and the executor takes its program from the layout's cache on
///         first use (<see cref="Program"/>). A root is eligible only when every composite a pointer can reach compiled.
///     </para>
///     <para>Immutable apart from that one cached program, which every thread may set to the same value.</para>
/// </remarks>
internal sealed class ReadPointerTarget
{
    /// <summary>Creates a target description.</summary>
    /// <param name="field">The pointer field; its compiled type is the final target's type.</param>
    /// <param name="kind">How the final target is read.</param>
    /// <param name="codec">The codec of the value, the enum storage, the terminated text, or a counted element.</param>
    /// <param name="enumType">The enum type of an enum target or counted enum elements.</param>
    /// <param name="composite">The struct or union of a composite target or counted composite elements.</param>
    /// <param name="element">The counted element view (<see cref="CompiledField.CountedElement"/>), or <see langword="null"/>.</param>
    /// <param name="count">The slot program of a data-dependent <c>@count(N)</c>, or <see langword="null"/>.</param>
    public ReadPointerTarget(
        CompiledField field,
        ReadPointerTargetKind kind,
        ProgramCodec codec,
        CompiledEnumType? enumType,
        CompiledCompositeType? composite,
        CompiledField? element,
        ProgramExpression? count)
    {
        this.Field = field;
        this.Kind = kind;
        this.Codec = codec;
        this.Enum = enumType;
        this.Composite = composite;
        this.Element = element;
        this.Count = count;
        this.CountContext = "pointer element count for " + field.Name;
    }

    /// <summary>Gets the pointer field.</summary>
    public CompiledField Field { get; }

    /// <summary>Gets how the final target is read.</summary>
    public ReadPointerTargetKind Kind { get; }

    /// <summary>Gets the codec of the value, enum storage, terminated text or counted element.</summary>
    public ProgramCodec Codec { get; }

    /// <summary>Gets the enum type of an enum target or of counted enum elements.</summary>
    public CompiledEnumType? Enum { get; }

    /// <summary>Gets the struct or union of a composite target or of counted composite elements.</summary>
    public CompiledCompositeType? Composite { get; }

    /// <summary>Gets the counted element view, or <see langword="null"/> for a pointer to one value.</summary>
    public CompiledField? Element { get; }

    /// <summary>Gets the slot program of a <c>@count(N)</c> the data decides, or <see langword="null"/> (no count, or a fixed one).</summary>
    public ProgramExpression? Count { get; }

    /// <summary>Gets what a count failure names (<c>pointer element count for p</c>).</summary>
    public string CountContext { get; }

    /// <summary>Gets a value indicating whether the target is counted (<c>@count(N)</c>).</summary>
    public bool IsCounted => this.Field.HasCountedTarget;

    /// <summary>Gets a value indicating whether the pointer is a one-level <c>void *</c>, an opaque address that is never followed.</summary>
    public bool IsVoid => this.Field.PointerDepth == 1 && this.Field.Type.TerminalName == "void";

    /// <summary>Gets or sets the cached program of <see cref="Composite"/>, taken from the layout's cache on first use.</summary>
    public ReadProgram? Program { get; set; }
}
