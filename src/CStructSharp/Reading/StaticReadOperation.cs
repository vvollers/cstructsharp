namespace CStructSharp.Reading;

using CStructSharp.Compilation;
using CStructSharp.Syntax;

/// <summary>One step of a <see cref="StaticReadPlan"/>.</summary>
internal sealed class StaticReadOperation
{
    /// <summary>Creates one fixed-offset read step.</summary>
    /// <param name="kind">How the step decodes its bytes.</param>
    /// <param name="slot">The index of the result slot the value fills in the struct's shape.</param>
    /// <param name="offset">The field's byte offset from the start of the plan's root struct.</param>
    /// <param name="field">The compiled field the step reads.</param>
    /// <param name="nestedDeclaration">The nested struct declaration, for nested steps only.</param>
    /// <param name="nestedComposite">The nested compiled struct, for nested steps only.</param>
    /// <param name="nestedPlan">The nested struct's own cached plan, for nested steps only.</param>
    /// <param name="count">The fixed element count, for array steps; 0 for a scalar.</param>
    public StaticReadOperation(StaticReadKind kind, int slot, int offset, CompiledField field, Struct? nestedDeclaration, CompiledCompositeType? nestedComposite, StaticReadPlan? nestedPlan, int count = 0)
    {
        this.Kind = kind;
        this.Slot = slot;
        this.Offset = offset;
        this.Field = field;
        this.NestedDeclaration = nestedDeclaration;
        this.NestedComposite = nestedComposite;
        this.NestedPlan = nestedPlan;
        this.Count = count;
    }

    /// <summary>Gets how the step decodes its bytes.</summary>
    public StaticReadKind Kind { get; }

    /// <summary>Gets the index of the result slot the value fills in the struct's shape.</summary>
    public int Slot { get; }

    /// <summary>Gets the field's byte offset from the start of the plan's root struct.</summary>
    public int Offset { get; }

    /// <summary>Gets the compiled field the step reads.</summary>
    public CompiledField Field { get; }

    /// <summary>Gets the nested struct declaration, or <see langword="null"/> for a primitive step.</summary>
    public Struct? NestedDeclaration { get; }

    /// <summary>Gets the nested compiled struct, or <see langword="null"/> for a primitive step.</summary>
    public CompiledCompositeType? NestedComposite { get; }

    /// <summary>Gets the nested struct's cached plan, or <see langword="null"/> for a primitive step.</summary>
    public StaticReadPlan? NestedPlan { get; }

    /// <summary>Gets the fixed element count of an array step, or 0 for a scalar step.</summary>
    public int Count { get; }
}
