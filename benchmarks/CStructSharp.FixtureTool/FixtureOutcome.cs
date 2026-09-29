namespace CStructSharp.FixtureTool;

/// <summary>What the managed library produced for one fixture (<see cref="FixtureVerification.Evaluate"/>).</summary>
/// <param name="Json">The canonical JSON of the parsed value, or <see langword="null"/> when nothing was read or the read failed.</param>
/// <param name="ErrorType">The name of the exception type the read threw, or <see langword="null"/> when it did not throw.</param>
/// <param name="Consumed">The bytes the read consumed, or <see langword="null"/> without a value.</param>
/// <param name="Length">The input length in bytes, or <see langword="null"/> when the fixture has no input.</param>
public sealed record FixtureOutcome(string? Json, string? ErrorType, long? Consumed, long? Length)
{
    /// <summary>Describes the outcome in one line for the tool's progress output.</summary>
    /// <returns>The thrown exception type, <c>compiled</c>, or the JSON length and the consumed byte count.</returns>
    public string Describe()
    {
        if (this.ErrorType is not null)
        {
            return "throws " + this.ErrorType;
        }

        if (this.Json is null)
        {
            return "compiled";
        }

        return $"{this.Json.Length} JSON chars, consumed {this.Consumed}/{this.Length}";
    }
}
