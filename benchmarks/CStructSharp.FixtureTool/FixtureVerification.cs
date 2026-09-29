namespace CStructSharp.FixtureTool;

using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using CStructSharp.Diagnostics;

/// <summary>
///     Computes a fixture's outcome with the managed library and checks it against the expectations recorded in the
///     fixture file. The <c>verify</c> command and the managed fixture-expectation test share this class, so both apply
///     exactly the same rules.
/// </summary>
public static class FixtureVerification
{
    /// <summary>
    ///     Parses one fixture the way the recorded expectations were produced: a compile-only fixture compiles each of its
    ///     definitions; a fixture without bytes compiles its layout; any other fixture reads its root from a
    ///     read-only <see cref="MemoryStream"/> over its bytes with its variables and read options.
    /// </summary>
    /// <param name="directory">The fixture directory that <c>file</c> payload paths are relative to.</param>
    /// <param name="fixture">The fixture to evaluate.</param>
    /// <returns>
    ///     The canonical JSON of the value and the number of bytes consumed, the name of the <see cref="CStructException"/>
    ///     type the read threw, or neither when nothing is read.
    /// </returns>
    /// <exception cref="CStructException">A layout does not compile.</exception>
    /// <exception cref="InvalidDataException">The fixture's byte description is incomplete.</exception>
    public static FixtureOutcome Evaluate(string directory, FixtureDocument fixture)
    {
        if (fixture.Definitions is { Count: > 0 })
        {
            foreach (string definition in fixture.Definitions)
            {
                _ = new CStruct(definition, fixture.Options.PointerSize, fixture.Options.Aligned, fixture.Options.LittleEndian);
            }

            return new FixtureOutcome(null, null, null, null);
        }

        CStruct layout = FixtureLoader.CreateLayout(fixture);
        if (fixture.Bytes is null)
        {
            return new FixtureOutcome(null, null, null, null);
        }

        byte[] bytes = FixtureLoader.MaterializeBytes(directory, fixture);
        ReadOptions options = FixtureLoader.CreateReadOptions(fixture);
        try
        {
            using var stream = new MemoryStream(bytes, writable: false);
            object? result = layout.ReadValue(stream, fixture.Root, fixture.Variables, options);
            long consumed = stream.Position;
            string json = CanonicalJson.Serialize(result);
            return new FixtureOutcome(json, null, consumed, bytes.Length);
        }
        catch (CStructException exception)
        {
            return new FixtureOutcome(null, exception.GetType().Name, null, bytes.Length);
        }
    }

    /// <summary>
    ///     Compares an outcome with the fixture's recorded expectations: the same expected exception type (or none), and
    ///     for a value, the same SHA-256 and character length of its canonical JSON and, when the value is stored
    ///     inline, the same JSON value.
    /// </summary>
    /// <param name="fixture">The fixture with its recorded expectations.</param>
    /// <param name="outcome">The outcome <see cref="Evaluate"/> computed.</param>
    /// <returns>A description of the first difference, or <see langword="null"/> when the outcome matches.</returns>
    public static string? Compare(FixtureDocument fixture, FixtureOutcome outcome)
    {
        if (!string.Equals(fixture.ExpectedError, outcome.ErrorType, StringComparison.Ordinal))
        {
            return $"expected error '{fixture.ExpectedError}', got '{outcome.ErrorType}'";
        }

        if (outcome.Json is null)
        {
            // Nothing was read, or the read failed as expected; a recorded value would be stale.
            return fixture.ExpectedSha256 is null && fixture.Expected is null ? null : "a value is recorded but none was read";
        }

        string sha = Sha256(outcome.Json);
        if (!string.Equals(fixture.ExpectedSha256, sha, StringComparison.OrdinalIgnoreCase))
        {
            return $"expected SHA-256 {fixture.ExpectedSha256}, got {sha}";
        }

        if (fixture.ExpectedJsonLength != outcome.Json.Length)
        {
            return $"expected {fixture.ExpectedJsonLength} JSON characters, got {outcome.Json.Length}";
        }

        // The inline value is what the JavaScript harnesses compare with; it must describe the same result as the hash.
        if (fixture.Expected is not null &&
            !JsonNode.DeepEquals(fixture.Expected, JsonNode.Parse(outcome.Json, documentOptions: new JsonDocumentOptions { MaxDepth = 4096 })))
        {
            return "the inline expected value differs from the computed value";
        }

        return null;
    }

    /// <summary>Computes the SHA-256 of text encoded as UTF-8.</summary>
    /// <param name="text">The text, such as a canonical JSON result.</param>
    /// <returns>The hash as 64 lowercase hexadecimal digits.</returns>
    public static string Sha256(string text)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text))).ToLowerInvariant();
    }
}
