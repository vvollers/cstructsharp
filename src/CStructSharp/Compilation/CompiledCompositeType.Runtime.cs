namespace CStructSharp.Compilation;

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using CStructSharp.Reading;
using CStructSharp.Values;

/// <summary>The runtime half of a compiled composite: the cached span read plan and the typed plans built over it.</summary>
internal sealed partial class CompiledCompositeType
{
    private StaticReadPlan? staticPlan;
    private ConcurrentDictionary<Type, TypedReadPlan?>? typedReadPlans;
    private bool staticPlanBuilt;

    /// <summary>The span read plan when every member is statically placed; null otherwise. Built on first use.</summary>
    public StaticReadPlan? StaticPlan
    {
        get
        {
            if (!this.staticPlanBuilt)
            {
                // Built after the whole model is bound; a benign race builds the same plan twice.
                this.staticPlan = StaticReadPlan.TryBuild(this);
                this.staticPlanBuilt = true;
            }

            return this.staticPlan;
        }
    }

    /// <summary>
    ///     Typed read plans bound to this composite's static plan, one per target type, created on the first
    ///     typed read so a layout never read into a POCO does not pay for the table.
    /// </summary>
    public TypedReadPlan? GetOrAddTypedReadPlan([DynamicallyAccessedMembers(TypedValueConverter.MappedMembers)] Type targetType)
    {
        ConcurrentDictionary<Type, TypedReadPlan?> plans = this.typedReadPlans ??
            Interlocked.CompareExchange(ref this.typedReadPlans, new ConcurrentDictionary<Type, TypedReadPlan?>(), null) ??
            this.typedReadPlans;
        if (plans.TryGetValue(targetType, out TypedReadPlan? plan))
        {
            return plan;
        }

        // Built with the annotated type in hand (a delegate would lose the annotation); a benign race builds twice.
        plan = TypedReadPlan.TryBuild(this.StaticPlan!, targetType);
        return plans.TryAdd(targetType, plan) ? plan : plans[targetType];
    }
}
