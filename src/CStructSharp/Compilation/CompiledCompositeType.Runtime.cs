namespace CStructSharp.Compilation;

using CStructSharp.Reading;

/// <summary>The runtime half of a compiled composite: the cached span read plan.</summary>
internal sealed partial class CompiledCompositeType
{
    private StaticReadPlan? staticPlan;
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
}
