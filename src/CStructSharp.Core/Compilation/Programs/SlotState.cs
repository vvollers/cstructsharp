namespace CStructSharp.Compilation.Programs;

/// <summary>
///     What one layout-variable slot holds (<see cref="SlotValue"/>). The states are the value kinds the operation's
///     name dictionary can hold, so a slot and a dictionary entry of the same name always evaluate the same way.
/// </summary>
internal enum SlotState : byte
{
    /// <summary>The name has no value: an expression that selects it fails with "Undefined expression identifier".</summary>
    Undefined = 0,

    /// <summary>An integer inside the signed 128-bit expression domain, held in <see cref="SlotValue.Value"/>.</summary>
    Literal,

    /// <summary>
    ///     An exact integer outside the domain (a definition or enum member such as <c>1 &lt;&lt; 127</c>), held as a
    ///     boxed <see cref="System.Numerics.BigInteger"/> payload; an expression that selects it fails naming the value.
    /// </summary>
    OutOfDomain,

    /// <summary>
    ///     A captured value no expression can use (a wide integer, a field that is not an integer); the payload is the
    ///     <see cref="Expressions.UnusableVariable"/> whose failure an expression that selects it throws.
    /// </summary>
    Unusable,

    /// <summary>
    ///     An expression evaluated each time it is selected (a definition that names a field, a caller expression);
    ///     the payload is its <see cref="ProgramExpression"/>.
    /// </summary>
    LiveExpression,

    /// <summary>
    ///     A bare reference to a name that has no slot in the table; the payload is that name. Only internal expression
    ///     inputs can produce it, and an evaluation that selects it runs on the dictionary evaluator.
    /// </summary>
    Identifier,
}
