namespace CStructSharp.Compilation;

using System.Threading;
using CStructSharp.Reading;

/// <summary>The runtime half of a compiled composite: the cached span read plan and layout fingerprint.</summary>
internal sealed partial class CompiledCompositeType
{
    // Stands for "built, and this composite has no static plan", so the cache is a single reference.
    private static readonly object NoStaticPlan = new();

    // Boxed so that publishing it is one reference write: a 16-byte ulong? could be read half-written by another thread.
    private System.Runtime.CompilerServices.StrongBox<ulong>? fingerprint;

    // Null until built, then the plan or NoStaticPlan. One reference, published with a release write, means another
    // thread sees either nothing (and builds the same plan itself) or a complete result, never "built" without a plan.
    private object? staticPlanState;

    /// <summary>The span read plan when every member is statically placed; null otherwise. Built on first use.</summary>
    public StaticReadPlan? StaticPlan
    {
        get
        {
            object? state = Volatile.Read(ref this.staticPlanState);
            if (state is null)
            {
                // Built after the whole model is bound; a benign race builds the same plan twice.
                state = (object?)StaticReadPlan.TryBuild(this) ?? NoStaticPlan;
                Volatile.Write(ref this.staticPlanState, state);
            }

            return state as StaticReadPlan;
        }
    }

    /// <summary>
    ///     The layout fingerprint of this composite (see <see cref="Compilation.LayoutFingerprint"/>), which a mapped
    ///     class generated against the same layout carries. Computed on first use; a benign race computes it twice.
    /// </summary>
    public ulong Fingerprint => (this.fingerprint ??= new System.Runtime.CompilerServices.StrongBox<ulong>(Compilation.LayoutFingerprint.Compute(this))).Value;
}
