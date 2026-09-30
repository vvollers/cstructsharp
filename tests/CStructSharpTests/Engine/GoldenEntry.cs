namespace CStructSharp.Tests;

/// <summary>
///     One entry of a <see cref="GoldenSection"/>: a readable outcome (<see cref="Text"/>) under its case key, or a hashed
///     group under the group's name: the SHA-256 (<see cref="Hash"/>) of its <see cref="Count"/> outcomes and their
///     readable <see cref="Summary"/>.
/// </summary>
/// <param name="Key">The case key of a readable outcome, or the group of a hashed one (empty for the whole test).</param>
/// <param name="Text">The normalized outcome (<see cref="EngineGolden.Normalize"/>), or <see langword="null"/> for a hashed entry.</param>
/// <param name="Hash">The group's hash as 64 lowercase hexadecimal digits, or <see langword="null"/> for a readable entry.</param>
/// <param name="Count">The number of outcomes the hash covers; 1 for a readable entry.</param>
/// <param name="Summary">
///     A hashed group's outcomes counted by kind (<see cref="GoldenScope.KindOf"/>), such as
///     <c>ok 12, CStructReadException 3</c>; <see langword="null"/> for a readable entry.
/// </param>
internal sealed record GoldenEntry(string Key, string? Text, string? Hash, int Count, string? Summary)
{
    /// <summary>Creates a readable entry.</summary>
    /// <param name="key">The case key.</param>
    /// <param name="text">The normalized outcome.</param>
    /// <returns>The entry.</returns>
    public static GoldenEntry Readable(string key, string text) => new(key, text, null, 1, null);

    /// <summary>Creates a hashed entry.</summary>
    /// <param name="group">The group, or an empty string for the whole test.</param>
    /// <param name="hash">The hash as 64 lowercase hexadecimal digits.</param>
    /// <param name="count">The number of outcomes the hash covers.</param>
    /// <param name="summary">The outcomes counted by kind: one line.</param>
    /// <returns>The entry.</returns>
    public static GoldenEntry Hashed(string group, string hash, int count, string summary) => new(group, null, hash, count, summary);
}
