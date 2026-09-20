namespace CStructSharp.Diagnostics;

/// <summary>
///     The write-side diagnostic texts, in one place so the runtime writer, the budget stream, and the generated
///     code's <c>WriteCursor</c> report the same words for the same failure.
/// </summary>
internal static class WriteFailures
{
    public const string TotalBytesLimit = "Write operation exceeded the configured total byte limit.";

    public const string StringBytesLimit = "String field exceeded the configured encoded-byte write limit.";

    public const string NestingLimit = "Maximum nested struct write depth exceeded.";

    public const string DestinationCapacity = "The serialized value exceeds the supplied destination capacity.";

    /// <summary>An array with more elements than <c>MaxArrayElements</c> allows for a write.</summary>
    public static string ArrayLengthLimit(string fieldName) => "Array length exceeds the configured write limit: " + fieldName;
}
