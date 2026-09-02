namespace CStructSharp.Structure;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;

/// <summary>Represents an enum declaration and the primitive type used to store its numeric value.</summary>
internal class Enum : CStructElement
{
    private readonly ImmutableArray<EnumValue> evaluatedValues;

    /// <summary>Creates a byte-sized enum and fills in values omitted from the declaration.</summary>
    public Enum(Identifier name, ImmutableArray<EnumValue> values)
        : this(name, values, Identifier.BYTE)
    {
    }

    /// <summary>Creates an enum with an explicit storage type and fills in values omitted from the declaration.</summary>
    public Enum(Identifier name, ImmutableArray<EnumValue> values, Identifier type)
        : this(
            name,
            values,
            type,
            EvaluateValues(
                values,
                global::CStructSharp.ExpressionEvaluator.Default,
                null,
                64,
                null,
                null))
    {
    }

    /// <summary>Creates a parser or compiled enum with its immutable evaluated-value state supplied explicitly.</summary>
    private Enum(
        Identifier name,
        ImmutableArray<EnumValue> declaredValues,
        Identifier type,
        ImmutableArray<EnumValue> evaluatedValues)
    {
        this.Name = name;
        this.DeclaredValues = declaredValues;
        this.Type = type;
        this.evaluatedValues = evaluatedValues;
    }

    public override Identifier Name { get; }

    public Identifier Type { get; }

    public ImmutableArray<EnumValue> Values
    {
        get => this.evaluatedValues.IsDefault
                   ? EvaluateValues(
                       this.DeclaredValues,
                       global::CStructSharp.ExpressionEvaluator.Default,
                       null,
                       64,
                       null,
                       null)
                   : this.evaluatedValues;
    }

    internal ImmutableArray<EnumValue> DeclaredValues { get; }

    /// <summary>Creates the parser form without evaluating expressions before compilation options are available.</summary>
    internal static Enum CreateUnevaluated(
        Identifier name,
        ImmutableArray<EnumValue> values,
        Identifier type)
    {
        return new Enum(name, values, type, default);
    }

    /// <summary>Returns an enum whose values were checked with the owning compiled layout's evaluator.</summary>
    internal Enum Evaluate(
        global::CStructSharp.ExpressionEvaluator evaluator,
        IReadOnlyDictionary<string, Expr> staticVariables,
        int bitWidth,
        BigInteger minimum,
        BigInteger maximum)
    {
        return new Enum(
            this.Name,
            this.DeclaredValues,
            this.Type,
            EvaluateValues(
                this.DeclaredValues,
                evaluator,
                staticVariables,
                bitWidth,
                minimum,
                maximum));
    }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    public override bool Equals(CStructElement? other)
    {
        return other is Enum e &&
               this.Name.Equals(e.Name) &&
               this.Values.SequenceEqual(e.Values) &&
               this.Type.Equals(e.Type);
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    public override int GetHashCode()
    {
        HashCode hash = default;
        hash.Add(this.Name);
        hash.Add(this.Type);
        foreach (EnumValue value in this.Values)
        {
            hash.Add(value);
        }

        return hash.ToHashCode();
    }

    /// <summary>Returns a short readable description for debugging and logs.</summary>
    public override string ToString()
    {
        return $"Enum {this.Name} [{this.Type}] ({string.Join(", ", this.Values)})";
    }

    /// <summary>Expands implicit enum values in declaration order so later expressions can refer to earlier names.</summary>
    private static ImmutableArray<EnumValue> EvaluateValues(
        ImmutableArray<EnumValue> values,
        global::CStructSharp.ExpressionEvaluator evaluator,
        IReadOnlyDictionary<string, Expr>? staticVariables,
        int bitWidth,
        BigInteger? minimum,
        BigInteger? maximum)
    {
        // C enum declarations begin at zero unless an explicit expression establishes a different starting value.
        BigInteger nextValue = BigInteger.Zero;
        ImmutableArray<EnumValue>.Builder result = ImmutableArray.CreateBuilder<EnumValue>(values.Length);

        // Keep static definitions and earlier enum names available because later members may refer to either.
        var variables = new Dictionary<string, Expr>(StringComparer.Ordinal);
        if (staticVariables is not null)
        {
            foreach (KeyValuePair<string, Expr> variable in staticVariables)
            {
                variables.Add(variable.Key, variable.Value);
            }
        }

        for (int index = 0; index < values.Length; index++)
        {
            EnumValue value = values[index];
            BigInteger evaluated;

            // C enums count upward from the previous value unless an expression supplies a new starting value.
            if (ReferenceEquals(value.Value, NoneExpr.Instance))
            {
                evaluated = nextValue;
            }
            else
            {
                // Evaluate an explicit value against the names already established in declaration order.
                evaluated = evaluator.EvaluateExact(value.Value, variables, bitWidth);
            }

            if ((minimum is not null && evaluated < minimum.Value) ||
                (maximum is not null && evaluated > maximum.Value))
            {
                throw new global::CStructSharp.CStructLayoutException(
                    $"Enum member '{value.Name.Name}' value {evaluated} is outside its declared {bitWidth}-bit domain.");
            }

            var literal = new Literal(evaluated);
            result.Add(new EnumValue(value.Name, literal));
            variables[value.Name.Name] = literal;
            if (index < values.Length - 1 &&
                ReferenceEquals(values[index + 1].Value, NoneExpr.Instance))
            {
                nextValue = evaluated + BigInteger.One;
            }
        }

        return result.ToImmutable();
    }
}
