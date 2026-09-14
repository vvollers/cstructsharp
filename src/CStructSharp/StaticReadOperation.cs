namespace CStructSharp;

using CStructSharp.Structure;

/// <summary>One step of a <see cref="StaticReadPlan"/>.</summary>
internal sealed class StaticReadOperation
{
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

    public StaticReadKind Kind { get; }

    public int Slot { get; }

    public int Offset { get; }

    public CompiledField Field { get; }

    public Struct? NestedDeclaration { get; }

    public CompiledCompositeType? NestedComposite { get; }

    public StaticReadPlan? NestedPlan { get; }

    public int Count { get; }
}
