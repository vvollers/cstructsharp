namespace CStructSharp.Structure;

using System;
using System.Collections.Generic;
using System.Linq;

/// <summary>Represents a name in a layout expression or declaration and records any pointer stars written with it.</summary>
internal class Identifier : Expr
{
    // public static Identifier DEFAULT = new("default");
    public static readonly Identifier BYTE = new("byte");

    /// <summary>Creates an identifier, removing pointer stars from its name while remembering their count.</summary>
    public Identifier(string name)
    {
        this.PointerDepth = name.Count(c => c == '*');
        this.Name = name.Replace("*", string.Empty).Trim();
        this.IsPointer = this.PointerDepth > 0;
    }

    public bool IsPointer { get; }

    public string Name { get; }

    public int PointerDepth { get; }

    public override int Value
    {
        get => this.Calc();
    }

    /// <summary>Looks up this name in the supplied expression values and calculates the referenced expression.</summary>
    public override int Calc(Dictionary<string, Expr> variables)
    {
        return global::CStructSharp.ExpressionEvaluator.Default.Evaluate(this, variables);
    }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    public override bool Equals(Expr? other)
    {
        return other is Identifier i &&
               this.Name == i.Name &&
               this.PointerDepth == i.PointerDepth;
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    public override int GetHashCode()
    {
        return HashCode.Combine(this.Name.GetHashCode(StringComparison.Ordinal), this.PointerDepth);
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    public override string ToString()
    {
        return $"[{this.Name}]";
    }
}
