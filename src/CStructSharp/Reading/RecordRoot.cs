namespace CStructSharp.Reading;

/// <summary>The struct a record sequence is made of: its declaration name and its fixed size, when it has one under the operation's variables.</summary>
/// <param name="Name">The struct's declaration name.</param>
/// <param name="Size">The struct's size in bytes when every record has the same size; <see langword="null"/> when each record's size depends on its contents.</param>
internal readonly record struct RecordRoot(string Name, int? Size);
