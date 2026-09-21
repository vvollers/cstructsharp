namespace CStructSharp.Reading;

/// <summary>The struct a record sequence is made of: its declaration name and its fixed size, when it has one under the operation's variables.</summary>
internal readonly record struct RecordRoot(string Name, int? Size);
