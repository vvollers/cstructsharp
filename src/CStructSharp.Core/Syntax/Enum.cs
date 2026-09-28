namespace CStructSharp.Syntax;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Numerics;

/// <summary>Represents an enum declaration and the primitive type used to store its numeric value.</summary>
internal class Enum : CStructElement
{
    private readonly ImmutableArray<EnumValue> evaluatedValues;

    /// <summary>Creates a <c>uint32</c>-backed enum (the compiler default for non-negative members) and fills in values omitted from the declaration.</summary>
    /// <param name="name">The enum's declared name.</param>
    /// <param name="values">
    ///     The members in declaration order; a member without an expression gets the next value.
    /// </param>
    public Enum(Identifier name, ImmutableArray<EnumValue> values)
        : this(name, values, Identifier.UINT32)
    {
    }

    /// <summary>Creates an enum with an explicit storage type and fills in values omitted from the declaration.</summary>
    /// <param name="name">The enum's declared name.</param>
    /// <param name="values">
    ///     The members in declaration order; a member without an expression gets the next value.
    /// </param>
    /// <param name="type">The primitive type that stores the enum's numeric value.</param>
    public Enum(Identifier name, ImmutableArray<EnumValue> values, Identifier type)
        : this(
            name,
            values,
            type,
            EvaluateValues(
                values,
                global::CStructSharp.Expressions.ExpressionEvaluator.Default,
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
        ImmutableArray<EnumValue> evaluatedValues,
        bool isFlag = false)
    {
        this.Name = name;
        this.DeclaredValues = declaredValues;
        this.Type = type;
        this.evaluatedValues = evaluatedValues;
        this.IsFlag = isFlag;
    }

    /// <summary>Gets the enum's declared name.</summary>
    public override Identifier Name { get; }

    /// <summary>Gets the primitive type that stores the enum's numeric value.</summary>
    public Identifier Type { get; }

    /// <summary>
    ///     Whether this is a <c>flag</c> declaration (a bitmask enum): an omitted member value is the next unused
    ///     bit rather than the previous value plus one, and a read decomposes the stored value into member names.
    /// </summary>
    public bool IsFlag { get; }

    /// <summary>
    ///     Gets the members in declaration order with every value evaluated to a literal, including omitted values.
    ///     A parser-form enum is evaluated with the default evaluator (shift counts below 64, no range check) on each access.
    /// </summary>
    public ImmutableArray<EnumValue> Values
    {
        get => this.evaluatedValues.IsDefault
                   ? EvaluateValues(
                       this.DeclaredValues,
                       global::CStructSharp.Expressions.ExpressionEvaluator.Default,
                       null,
                       64,
                       null,
                       null,
                       this.IsFlag)
                   : this.evaluatedValues;
    }

    /// <summary>Gets the members in declaration order with their value expressions as written.</summary>
    internal ImmutableArray<EnumValue> DeclaredValues { get; }

    /// <summary>Creates the parser form without evaluating expressions before compilation options are available.</summary>
    /// <param name="name">The enum's declared name.</param>
    /// <param name="values">The members in declaration order, with their expressions as written.</param>
    /// <param name="type">The primitive type that stores the enum's numeric value.</param>
    /// <param name="isFlag">Whether the declaration is a <c>flag</c> (bitmask) enum.</param>
    /// <returns>
    ///     An enum whose member values are evaluated later, by <see cref="Evaluate"/> or on access to
    ///     <see cref="Values"/>.
    /// </returns>
    internal static Enum CreateUnevaluated(
        Identifier name,
        ImmutableArray<EnumValue> values,
        Identifier type,
        bool isFlag = false)
    {
        return new Enum(name, values, type, default, isFlag);
    }

    /// <summary>Returns an enum whose values were checked with the owning compiled layout's evaluator.</summary>
    /// <param name="evaluator">The compiled layout's evaluator, which applies its depth and work limits.</param>
    /// <param name="staticVariables">The layout's constant names (such as defines) member expressions may use.</param>
    /// <param name="bitWidth">The storage type's size in bits, which bounds shift counts in member expressions.</param>
    /// <param name="minimum">The smallest value the storage type can hold.</param>
    /// <param name="maximum">The largest value the storage type can hold.</param>
    /// <returns>A new enum with the same declaration and every member value evaluated exactly.</returns>
    /// <exception cref="global::CStructSharp.Diagnostics.CStructLayoutException">
    ///     A member value lies outside <paramref name="minimum"/> to <paramref name="maximum"/>, or an expression
    ///     exceeds the evaluator's limits.
    /// </exception>
    internal Enum Evaluate(
        global::CStructSharp.Expressions.ExpressionEvaluator evaluator,
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
                maximum,
                this.IsFlag),
            this.IsFlag);
    }

    /// <summary>Checks whether another value represents the same layout data.</summary>
    /// <param name="other">The item to compare with, or <see langword="null"/>.</param>
    /// <returns>
    ///     <see langword="true"/> when <paramref name="other"/> is an enum with the same name, flag kind, storage type,
    ///     and evaluated member values in the same order.
    /// </returns>
    public override bool Equals(CStructElement? other)
    {
        return other is Enum e &&
               this.Name.Equals(e.Name) &&
               this.IsFlag == e.IsFlag &&
               this.Values.SequenceEqual(e.Values) &&
               this.Type.Equals(e.Type);
    }

    /// <summary>Returns a hash code that matches this value's equality rules.</summary>
    /// <returns>A hash code combining the name, storage type, and evaluated member values.</returns>
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
    /// <returns>Text naming the kind (Enum or Flag), the name, the storage type, and the evaluated members.</returns>
    public override string ToString()
    {
        return $"{(this.IsFlag ? "Flag" : "Enum")} {this.Name} [{this.Type}] ({string.Join(", ", this.Values)})";
    }

    /// <summary>Expands implicit enum values in declaration order so later expressions can refer to earlier names.</summary>
    private static ImmutableArray<EnumValue> EvaluateValues(
        ImmutableArray<EnumValue> values,
        global::CStructSharp.Expressions.ExpressionEvaluator evaluator,
        IReadOnlyDictionary<string, Expr>? staticVariables,
        int bitWidth,
        BigInteger? minimum,
        BigInteger? maximum,
        bool isFlag = false)
    {
        // C enum declarations begin at zero unless an explicit expression establishes a different starting value;
        // a flag's first omitted member is the lowest bit.
        BigInteger nextValue = isFlag ? BigInteger.One : BigInteger.Zero;
        BigInteger highestBitSeen = BigInteger.Zero;
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
                throw new global::CStructSharp.Diagnostics.CStructLayoutException(
                    $"Enum member '{value.Name.Name}' value {evaluated} is outside its declared {bitWidth}-bit domain.");
            }

            var literal = new Literal(evaluated);
            result.Add(new EnumValue(value.Name, literal));
            variables[value.Name.Name] = literal;
            if (isFlag)
            {
                // The next omitted flag is the first bit above every bit seen so far (dissect.cstruct's rule).
                if (evaluated > highestBitSeen)
                {
                    highestBitSeen = evaluated;
                }

                nextValue = highestBitSeen.IsZero ? BigInteger.One : BigInteger.One << (int)highestBitSeen.GetBitLength();
            }
            else if (index < values.Length - 1 &&
                     ReferenceEquals(values[index + 1].Value, NoneExpr.Instance))
            {
                nextValue = evaluated + BigInteger.One;
            }
        }

        return result.ToImmutable();
    }
}
