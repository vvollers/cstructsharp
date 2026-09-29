namespace CStructSharp.Tests;

/// <summary>What <see cref="ExpressionDifferential"/> compared, summed over a corpus.</summary>
internal sealed class ExpressionDifferentialCounts
{
    /// <summary>Gets or sets the number of distinct layouts.</summary>
    public int Layouts { get; set; }

    /// <summary>Gets or sets the number of distinct expressions.</summary>
    public int Expressions { get; set; }

    /// <summary>Gets or sets how many expressions compiled to native slot programs.</summary>
    public int Native { get; set; }

    /// <summary>Gets or sets how many expressions may take the allocation-free leaf path.</summary>
    public int LeafSafe { get; set; }

    /// <summary>Gets or sets the number of compared evaluations (or creation failures).</summary>
    public int Evaluations { get; set; }

    /// <summary>Gets or sets how many compared evaluations failed (identically).</summary>
    public int Failures { get; set; }

    /// <summary>Adds another corpus part's counts.</summary>
    /// <param name="other">The counts to add.</param>
    public void Add(ExpressionDifferentialCounts other)
    {
        this.Layouts += other.Layouts;
        this.Expressions += other.Expressions;
        this.Native += other.Native;
        this.LeafSafe += other.LeafSafe;
        this.Evaluations += other.Evaluations;
        this.Failures += other.Failures;
    }

    /// <summary>Renders the counts.</summary>
    /// <returns>A one-line summary.</returns>
    public override string ToString()
        => $"{this.Layouts} layouts, {this.Expressions} expressions ({this.Native} native, {this.LeafSafe} leaf-safe), {this.Evaluations} evaluations compared ({this.Failures} failing identically)";
}
