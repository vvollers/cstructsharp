namespace CStructSharp.Compilation;

using CStructSharp.Reading;

/// <summary>The runtime half of a compiled composite: the cached span read plan and layout fingerprint.</summary>
internal sealed partial class CompiledCompositeType
{
    // Boxed so that publishing it is one reference write: a 16-byte ulong? could be read half-written by another thread.
    private System.Runtime.CompilerServices.StrongBox<ulong>? fingerprint;
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

    /// <summary>
    ///     The layout fingerprint of this composite (see <see cref="Compilation.LayoutFingerprint"/>), which a mapped
    ///     class generated against the same layout carries. Computed on first use; a benign race computes it twice.
    /// </summary>
    public ulong Fingerprint => (this.fingerprint ??= new System.Runtime.CompilerServices.StrongBox<ulong>(Compilation.LayoutFingerprint.Compute(this))).Value;
}
