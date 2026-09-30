namespace CStructSharp.Benchmarks.Scenarios;

using System.Reflection;
using CStructSharp.FixtureTool;

/// <summary>A fully materialized fixture: compiled layout, input bytes, root, variables, and read options.</summary>
public sealed class FixtureCase
{
    private static readonly Lazy<string> Directory = new(() =>
        Environment.GetEnvironmentVariable("CSTRUCTSHARP_FIXTURES") is { Length: > 0 } configured
            ? Path.GetFullPath(configured)
            : FixtureLoader.FindFixtureDirectory());

    /// <summary>Initializes a fixture from its loaded parts.</summary>
    /// <param name="document">The fixture document.</param>
    /// <param name="layout">The layout compiled from the document's definition and options.</param>
    /// <param name="bytes">The materialized input bytes; empty for a compile-only fixture.</param>
    /// <param name="readOptions">The read options the document asks for.</param>
    private FixtureCase(FixtureDocument document, CStruct layout, byte[] bytes, ReadOptions readOptions)
    {
        this.Document = document;
        this.Layout = layout;
        this.Bytes = bytes;
        this.ReadOptions = readOptions;
    }

    /// <summary>Gets the fixture directory: <c>CSTRUCTSHARP_FIXTURES</c> when set, otherwise the repository's <c>benchmarks/fixtures</c>.</summary>
    public static string FixtureDirectory => Directory.Value;

    /// <summary>Gets the fixture document.</summary>
    public FixtureDocument Document { get; }

    /// <summary>Gets the layout compiled from the document.</summary>
    public CStruct Layout { get; }

    /// <summary>Gets the input bytes.</summary>
    public byte[] Bytes { get; }

    /// <summary>Gets the read options the document asks for.</summary>
    public ReadOptions ReadOptions { get; }

    /// <summary>Gets the root struct name.</summary>
    public string Root => this.Document.Root;

    /// <summary>Gets the layout variables the document supplies.</summary>
    public IReadOnlyDictionary<string, int> Variables => this.Document.Variables;

    /// <summary>Loads a fixture by id, compiles its layout, and materializes its bytes.</summary>
    /// <param name="id">The fixture id from <c>manifest.json</c>.</param>
    /// <returns>The loaded fixture.</returns>
    public static FixtureCase Load(string id)
    {
        FixtureDocument document = FixtureLoader.Load(FixtureDirectory, id);
        CStruct layout = FixtureLoader.CreateLayout(document);
        byte[] bytes = document.Bytes is null ? [] : FixtureLoader.MaterializeBytes(FixtureDirectory, document);
        return new FixtureCase(document, layout, bytes, FixtureLoader.CreateReadOptions(document));
    }

    /// <summary>Loads a fixture that a generated layout copies, and checks that the copy still matches it.</summary>
    /// <remarks>
    ///     A <c>[CStructLayout]</c> attribute needs a constant, so each generated benchmark layout repeats its fixture's
    ///     definition and options. A generated case compared with a runtime case over different layouts would measure
    ///     nothing useful, so a copy that drifted from its fixture stops the benchmark in setup. CI checks the same
    ///     pairings without running a benchmark (<c>tools/quality/benchmark-generated-layouts.test.mjs</c> reads the
    ///     <c>LoadMatching</c> calls); this check still guards a fixture directory chosen with <c>CSTRUCTSHARP_FIXTURES</c>.
    /// </remarks>
    /// <param name="id">The fixture id from <c>manifest.json</c>.</param>
    /// <param name="generatedLayout">The class that carries the fixture's <c>[CStructLayout]</c> copy.</param>
    /// <returns>The loaded fixture.</returns>
    /// <exception cref="InvalidOperationException">The copy's definition, root or options differ from the fixture's.</exception>
    public static FixtureCase LoadMatching(string id, Type generatedLayout)
    {
        FixtureCase fixture = Load(id);
        CStructLayoutAttribute copy = LayoutAttribute(generatedLayout);
        FixtureOptions options = fixture.Document.Options;
        if (copy.Definition != fixture.Document.Definition || copy.Root != fixture.Root || copy.PointerSize != options.PointerSize ||
            copy.Aligned != options.Aligned || copy.LittleEndian != options.LittleEndian)
        {
            throw new InvalidOperationException(
                $"{generatedLayout.Name} no longer matches fixture '{id}': copy the fixture's definition, root and options into its [CStructLayout].");
        }

        return fixture;
    }

    /// <summary>Compiles the runtime layout from a generated layout's own attribute, so both sides measure one definition.</summary>
    /// <param name="generatedLayout">The class that carries the <c>[CStructLayout]</c> attribute.</param>
    /// <returns>The runtime layout.</returns>
    public static CStruct CompileLike(Type generatedLayout)
    {
        CStructLayoutAttribute layout = LayoutAttribute(generatedLayout);
        return new CStruct(layout.Definition!, (byte)layout.PointerSize, layout.Aligned, layout.LittleEndian);
    }

    /// <summary>Loads a fixture document without compiling it or materializing its bytes.</summary>
    /// <param name="id">The fixture id from <c>manifest.json</c>.</param>
    /// <returns>The fixture document.</returns>
    public static FixtureDocument LoadDocument(string id)
    {
        return FixtureLoader.Load(FixtureDirectory, id);
    }

    /// <summary>Parses the fixture bytes from memory with the fixture's root, variables and read options.</summary>
    /// <returns>The parsed root value.</returns>
    public dynamic ParseSpan()
    {
        return this.Layout.Parse(this.Bytes.AsSpan(), this.Root, this.Variables, this.ReadOptions);
    }

    /// <summary>Rewinds a stream and parses it with the fixture's root, variables and read options.</summary>
    /// <param name="stream">A seekable stream holding the fixture bytes.</param>
    /// <returns>The parsed root value.</returns>
    public object Parse(Stream stream)
    {
        stream.Position = 0;
        return this.Layout.Parse(stream, this.Root, this.Variables, this.ReadOptions);
    }

    /// <summary>Reads the <c>[CStructLayout]</c> attribute of a generated layout class.</summary>
    /// <param name="generatedLayout">The layout class.</param>
    /// <returns>The attribute.</returns>
    /// <exception cref="InvalidOperationException">The class has no <c>[CStructLayout]</c> with an inline definition.</exception>
    private static CStructLayoutAttribute LayoutAttribute(Type generatedLayout)
    {
        return generatedLayout.GetCustomAttribute<CStructLayoutAttribute>() is { Definition: not null } attribute
            ? attribute
            : throw new InvalidOperationException($"{generatedLayout.Name} has no [CStructLayout] with an inline definition.");
    }
}
