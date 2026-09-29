namespace CStructSharp.Tests;

/// <summary>One scripted cursor operation with its two operands, rendered compactly in a differential trace.</summary>
/// <param name="Operation">The operation.</param>
/// <param name="A">The first operand; its meaning depends on <paramref name="Operation"/>.</param>
/// <param name="B">The second operand, or 0 when unused.</param>
internal readonly record struct CursorStep(CursorOperation Operation, int A, int B = 0)
{
    /// <summary>Renders the step as <c>Operation(A,B)</c>.</summary>
    /// <returns>The step text.</returns>
    public override string ToString() => $"{this.Operation}({this.A},{this.B})";
}
