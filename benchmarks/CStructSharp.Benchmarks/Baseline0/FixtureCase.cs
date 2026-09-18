namespace CStructSharp.Benchmarks.Baseline0;

using CStructSharp.FixtureTool;

/// <summary>A fully materialized fixture: compiled layout, input bytes, root, variables, and read options.</summary>
public sealed class FixtureCase
{
    private static readonly Lazy<string> Directory = new(() =>
        Environment.GetEnvironmentVariable("CSTRUCTSHARP_FIXTURES") is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : FixtureLoader.FindFixtureDirectory());

    private FixtureCase(FixtureDocument document, CStruct layout, byte[] bytes, ReadOptions readOptions)
    {
        this.Document = document;
        this.Layout = layout;
        this.Bytes = bytes;
        this.ReadOptions = readOptions;
    }

    public static string FixtureDirectory => Directory.Value;

    public FixtureDocument Document { get; }

    public CStruct Layout { get; }

    public byte[] Bytes { get; }

    public ReadOptions ReadOptions { get; }

    public string Root => this.Document.Root;

    public IReadOnlyDictionary<string, int> Variables => this.Document.Variables;

    public static FixtureCase Load(string id)
    {
        FixtureDocument document = FixtureLoader.Load(FixtureDirectory, id);
        CStruct layout = FixtureLoader.CreateLayout(document);
        byte[] bytes = document.Bytes is null ? [] : FixtureLoader.MaterializeBytes(FixtureDirectory, document);
        return new FixtureCase(document, layout, bytes, FixtureLoader.CreateReadOptions(document));
    }

    public static FixtureDocument LoadDocument(string id)
    {
        return FixtureLoader.Load(FixtureDirectory, id);
    }

    public dynamic ParseSpan()
    {
        return this.Layout.Parse(this.Bytes.AsSpan(), this.Root, this.Variables, this.ReadOptions);
    }

    public object Parse(Stream stream)
    {
        stream.Position = 0;
        return this.Layout.Parse(stream, this.Root, this.Variables, this.ReadOptions);
    }
}
