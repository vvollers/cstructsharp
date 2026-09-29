namespace CStructSharp.Compilation.Programs;

using System;
using System.Numerics;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>
///     The value of one layout-variable slot: a <see cref="SlotState"/>, the in-domain integer for
///     <see cref="SlotState.Literal"/>, and the payload the other states need. The default value is
///     <see cref="SlotState.Undefined"/>, so a cleared slot array holds no names.
/// </summary>
internal readonly struct SlotValue
{
    /// <summary>Stores the parts; the factory methods keep each state's payload of the right type.</summary>
    /// <param name="state">The state.</param>
    /// <param name="value">The integer of a literal, otherwise zero.</param>
    /// <param name="payload">The payload the state needs, otherwise <see langword="null"/>.</param>
    private SlotValue(SlotState state, Int128 value, object? payload)
    {
        this.State = state;
        this.Value = value;
        this.Payload = payload;
    }

    /// <summary>Gets an undefined slot.</summary>
    public static SlotValue Undefined => default;

    /// <summary>Gets what the slot holds.</summary>
    public SlotState State { get; }

    /// <summary>Gets the integer of a <see cref="SlotState.Literal"/>; zero for every other state.</summary>
    public Int128 Value { get; }

    /// <summary>
    ///     Gets the exact <see cref="BigInteger"/> (out of domain), the <see cref="UnusableVariable"/>, the
    ///     <see cref="ProgramExpression"/> (live expression) or the name (identifier); <see langword="null"/> otherwise.
    /// </summary>
    public object? Payload { get; }

    /// <summary>Creates a literal inside the 128-bit domain.</summary>
    /// <param name="value">The integer.</param>
    /// <returns>The slot value.</returns>
    public static SlotValue FromLiteral(Int128 value) => new(SlotState.Literal, value, null);

    /// <summary>Creates an exact integer outside the 128-bit domain.</summary>
    /// <param name="exact">The integer, which must lie outside the signed 128-bit range.</param>
    /// <returns>The slot value.</returns>
    public static SlotValue FromOutOfDomain(BigInteger exact) => new(SlotState.OutOfDomain, Int128.Zero, exact);

    /// <summary>Creates a value that fails when an expression selects it.</summary>
    /// <param name="variable">The unusable variable, which supplies the failure.</param>
    /// <returns>The slot value.</returns>
    public static SlotValue FromUnusable(UnusableVariable variable) => new(SlotState.Unusable, Int128.Zero, variable);

    /// <summary>Creates an expression evaluated whenever it is selected.</summary>
    /// <param name="program">The expression's slot-indexed program.</param>
    /// <returns>The slot value.</returns>
    public static SlotValue FromLiveExpression(ProgramExpression program) => new(SlotState.LiveExpression, Int128.Zero, program);

    /// <summary>Creates a reference to a name that has no slot.</summary>
    /// <param name="name">The name.</param>
    /// <returns>The slot value.</returns>
    public static SlotValue FromIdentifier(string name) => new(SlotState.Identifier, Int128.Zero, name);

    /// <summary>
    ///     Returns the dictionary entry this value stands for, for the dictionary evaluator and for comparisons: a new
    ///     literal, the unusable variable, the live expression's source tree, or an identifier node.
    /// </summary>
    /// <returns>The expression, or <see langword="null"/> for an undefined slot (no entry).</returns>
    public Expr? ToExpression() => this.State switch
    {
        SlotState.Literal => new Literal(this.Value),
        SlotState.OutOfDomain => new Literal((BigInteger)this.Payload!),
        SlotState.Unusable => (UnusableVariable)this.Payload!,
        SlotState.LiveExpression => ((ProgramExpression)this.Payload!).Source,
        SlotState.Identifier => new Identifier((string)this.Payload!),
        _ => null,
    };
}
