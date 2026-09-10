namespace CStructSharp.Fuzzing;

/// <summary>Stores one UTF-8 or hexadecimal retained seed.</summary>
public sealed class FuzzSeed
{
    public string Id { get; init; } = string.Empty;

    public string Encoding { get; init; } = string.Empty;

    public string Data { get; init; } = string.Empty;

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
