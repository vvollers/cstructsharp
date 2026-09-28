namespace CStructSharp.Fuzzing;

/// <summary>Stores one UTF-8 or hexadecimal retained seed.</summary>
public sealed class FuzzSeed
{
    /// <summary>Gets the seed name reported when the seed triggers an unexpected failure.</summary>
    public string Id { get; init; } = string.Empty;

    /// <summary>Gets how <see cref="Data"/> is encoded: <c>utf8</c> or <c>hex</c>.</summary>
    public string Encoding { get; init; } = string.Empty;

    /// <summary>Gets the seed input as UTF-8 text or hexadecimal digits.</summary>
    public string Data { get; init; } = string.Empty;

    /// <summary>Decodes <see cref="Data"/> into the raw input bytes according to <see cref="Encoding"/>.</summary>
    /// <returns>A new array holding the seed input.</returns>
    /// <exception cref="InvalidDataException">The encoding is neither <c>utf8</c> nor <c>hex</c>.</exception>
    public byte[] Decode()
    {
        return this.Encoding switch
        {
            "utf8" => System.Text.Encoding.UTF8.GetBytes(this.Data),
            "hex" => Convert.FromHexString(this.Data),
            _ => throw new InvalidDataException($"Fuzz seed '{this.Id}' has an unknown encoding."),
        };
    }
}
