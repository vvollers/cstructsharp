namespace CStructSharp.Tests;

using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CStructSharp.Fuzzing;
using CStructSharp.Introspection;
using CStructSharp.Values;

/// <summary>
///     Loads the repository's layout corpora as <see cref="EngineCorpusCase"/>s for the differential harness: the
///     generated-parity layout index, the benchmark fixtures, the manual's feature fixtures, the Portable contract's
///     examples and primitives, the well-known-format tests, the inspector's schema catalog, and the managed fuzz corpus.
///     Each corpus is loaded once.
/// </summary>
internal static partial class EngineCorpora
{
    /// <summary>
    ///     The largest benchmark input the corpora include; larger fixtures repeat a smaller fixture's layout at a
    ///     greater count, and rendering millions of elements would dominate the run.
    /// </summary>
    public const int MaximumInputBytes = 70_000;

    /// <summary>The parity layout index's cases, by id.</summary>
    private static readonly Lazy<Dictionary<string, EngineCorpusCase>> ParityCases = new(() => ById(LoadParityLayouts()));

    /// <summary>The benchmark fixtures' cases, by id.</summary>
    private static readonly Lazy<Dictionary<string, EngineCorpusCase>> BenchmarkCases = new(() => ById(LoadBenchmarkFixtures()));

    /// <summary>The manual fixtures' cases, by id.</summary>
    private static readonly Lazy<Dictionary<string, EngineCorpusCase>> ManualCases = new(() => ById(LoadManualFixtures()));

    /// <summary>The Portable contract's cases, by id.</summary>
    private static readonly Lazy<Dictionary<string, EngineCorpusCase>> PortableCases = new(() => ById(LoadPortableContract()));

    /// <summary>The well-known-format tests' cases, by id.</summary>
    private static readonly Lazy<Dictionary<string, EngineCorpusCase>> WellKnownCases = new(() => ById(LoadWellKnownFormats()));

    /// <summary>The inspector catalog's cases, by id.</summary>
    private static readonly Lazy<Dictionary<string, EngineCorpusCase>> InspectorCases = new(() => ById(LoadInspectorCatalog()));

    /// <summary>The fuzz corpus's cases, by target name.</summary>
    private static readonly Lazy<Dictionary<string, EngineCorpusCase[]>> FuzzCases = new(LoadFuzzCorpus);

    /// <summary>Gets the parity layout index's cases.</summary>
    public static IReadOnlyDictionary<string, EngineCorpusCase> Parity => ParityCases.Value;

    /// <summary>Gets the benchmark fixtures' cases.</summary>
    public static IReadOnlyDictionary<string, EngineCorpusCase> Benchmarks => BenchmarkCases.Value;

    /// <summary>Gets the manual fixtures' cases.</summary>
    public static IReadOnlyDictionary<string, EngineCorpusCase> Manual => ManualCases.Value;

    /// <summary>Gets the Portable contract's cases.</summary>
    public static IReadOnlyDictionary<string, EngineCorpusCase> Portable => PortableCases.Value;

    /// <summary>Gets the well-known-format tests' cases.</summary>
    public static IReadOnlyDictionary<string, EngineCorpusCase> WellKnown => WellKnownCases.Value;

    /// <summary>Gets the inspector catalog's cases.</summary>
    public static IReadOnlyDictionary<string, EngineCorpusCase> Inspector => InspectorCases.Value;

    /// <summary>Gets the fuzz corpus's cases, grouped by target.</summary>
    public static IReadOnlyDictionary<string, EngineCorpusCase[]> Fuzz => FuzzCases.Value;

    /// <summary>Gets the ids of the parity layouts whose inputs exceed <see cref="MaximumInputBytes"/>.</summary>
    public static List<string> SkippedParity { get; } = [];

    /// <summary>Gets the ids of the benchmark fixtures whose inputs exceed <see cref="MaximumInputBytes"/>.</summary>
    public static List<string> SkippedBenchmarks { get; } = [];

    /// <summary>Gets the path of the inspector's schema catalog in the repository.</summary>
    private static string InspectorCatalogPath => Path.Combine(TestFixtures.RepositoryRoot, "apps", "inspector", "src", "schema-catalog.ts");

    /// <summary>Returns the ids of a corpus as <see cref="DynamicDataAttribute"/> rows.</summary>
    /// <param name="corpus">The corpus.</param>
    /// <returns>One row per id, in ordinal order.</returns>
    public static IEnumerable<object[]> Rows(IReadOnlyDictionary<string, EngineCorpusCase> corpus)
        => corpus.Keys.Order(StringComparer.Ordinal).Select(id => new object[] { id, });

    /// <summary>Indexes cases by id.</summary>
    /// <param name="cases">The cases; ids must be distinct.</param>
    /// <returns>The index.</returns>
    private static Dictionary<string, EngineCorpusCase> ById(IEnumerable<EngineCorpusCase> cases) => cases.ToDictionary(item => item.Id, StringComparer.Ordinal);

    /// <summary>
    ///     The 207 layouts of <c>tests/CStructSharp.Generated.Parity/layouts.json</c>, with the inputs that parity suite
    ///     uses: manual layouts their fixture bytes and variables, benchmark layouts their fixture's bytes and read
    ///     options, shape layouts the bytes the reference implementation writes from their values, conditional layouts a
    ///     filled buffer, and recipe layouts (which carry no input) 64 deterministic pseudo-random bytes.
    /// </summary>
    /// <returns>The cases.</returns>
    private static IEnumerable<EngineCorpusCase> LoadParityLayouts()
    {
        using JsonDocument index = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "layouts.json")));
        var cases = new List<EngineCorpusCase>();
        foreach (JsonElement entry in index.RootElement.GetProperty("layouts").EnumerateArray())
        {
            string source = entry.GetProperty("source").GetString()!;
            string id = source + "/" + entry.GetProperty("id").GetString();
            string definition = entry.GetProperty("definition").GetString()!;
            string? root = entry.GetProperty("root").GetString();
            var compilation = new CStructCompilationOptions
            {
                CLongWidth = entry.TryGetProperty("cLongWidth", out JsonElement width) && width.GetInt32() > 0 ? width.GetInt32() : 64,
                DefaultEnumStorage = entry.TryGetProperty("defaultEnumStorage", out JsonElement storage) ? storage.GetString() : null,
                BitfieldAllocation = entry.TryGetProperty("bitfieldAllocation", out JsonElement allocation) && allocation.GetString() is { } allocationName
                                         ? Enum.Parse<BitfieldAllocation>(allocationName)
                                         : BitfieldAllocation.LowBitFirst,
                BitfieldPacking = entry.TryGetProperty("bitfieldPacking", out JsonElement packing) ? Enum.Parse<BitfieldPacking>(packing.GetString()!) : BitfieldPacking.SysV,
            };
            byte pointerSize = entry.GetProperty("pointerSize").GetByte();
            bool aligned = entry.GetProperty("aligned").GetBoolean();
            bool littleEndian = entry.GetProperty("littleEndian").GetBoolean();

            // Compiles the entry's layout with its constructor arguments and compilation options.
            CStruct Compile() => new(definition, pointerSize, aligned, littleEndian, compilation);

            IReadOnlyDictionary<string, int>? variables = null;
            ReadOptions? read = null;
            byte[] data;
            switch (source)
            {
            case "Manual":
                data = Convert.FromHexString(entry.GetProperty("bytes").GetString()!);
                variables = Variables(entry);
                break;
            case "Benchmarks":
                string file = Path.Combine(TestFixtures.BenchmarkFixtures, "cases", entry.GetProperty("id").GetString() + ".json");
                using (JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(file), new JsonDocumentOptions { MaxDepth = 4096 }))
                {
                    JsonElement fixtureRoot = fixture.RootElement;
                    if (!Include(fixtureRoot))
                    {
                        SkippedParity.Add(id);
                        continue;
                    }

                    data = FixtureBytes(fixtureRoot, id);
                    variables = Variables(fixtureRoot);
                    read = FixtureReadOptions(fixtureRoot);
                }

                break;
            case "Shapes":
                data = ShapeBytes(Compile(), root!, entry.GetProperty("values"));
                break;
            case "Conditional":
                data = new byte[entry.GetProperty("size").GetInt32()];
                Array.Fill(data, (byte)entry.GetProperty("fill").GetInt32());
                break;
            default:
                data = TestFixtures.Xorshift(Seed(id), 64);
                break;
            }

            cases.Add(new EngineCorpusCase(id, Compile, root, data, variables, read));
        }

        return cases;
    }

    /// <summary>
    ///     The cases of <c>benchmarks/fixtures</c> (<c>manifest.json</c> and <c>cases/</c>) with their bytes, variables and
    ///     read options; the compile-only fixtures, which have no input, read 64 pseudo-random bytes.
    /// </summary>
    /// <returns>The cases.</returns>
    private static IEnumerable<EngineCorpusCase> LoadBenchmarkFixtures()
    {
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestFixtures.BenchmarkFixtures, "manifest.json")));
        var cases = new List<EngineCorpusCase>();
        foreach (JsonElement item in manifest.RootElement.GetProperty("fixtures").EnumerateArray())
        {
            using JsonDocument fixture = JsonDocument.Parse(File.ReadAllText(Path.Combine(TestFixtures.BenchmarkFixtures, item.GetProperty("file").GetString()!)), new JsonDocumentOptions { MaxDepth = 4096 });
            JsonElement root = fixture.RootElement;
            string id = root.GetProperty("id").GetString()!;
            if (!Include(root))
            {
                SkippedBenchmarks.Add(id);
                continue;
            }

            string definition = root.GetProperty("definition").GetString()!;
            JsonElement options = root.GetProperty("options");
            byte pointerSize = options.GetProperty("pointerSize").GetByte();
            bool aligned = options.GetProperty("aligned").GetBoolean();
            bool littleEndian = options.GetProperty("littleEndian").GetBoolean();
            cases.Add(new EngineCorpusCase(
                id,
                () => new CStruct(definition, pointerSize, aligned, littleEndian),
                root.GetProperty("root").GetString(),
                FixtureBytes(root, id),
                Variables(root),
                FixtureReadOptions(root)));
        }

        return cases;
    }

    /// <summary>
    ///     The valid example of every feature pair of <c>contracts/language/manual-fixtures-v1.json</c>, whose offset
    ///     paths are also read and resolved, and every invalid form that fails while reading (<c>stage: read</c>) with
    ///     its string limit.
    /// </summary>
    /// <returns>The cases.</returns>
    private static IEnumerable<EngineCorpusCase> LoadManualFixtures()
    {
        using JsonDocument contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "manual-fixtures-v1.json")));
        var cases = new List<EngineCorpusCase>();
        foreach (JsonElement pair in contract.RootElement.GetProperty("featurePairs").EnumerateArray())
        {
            string id = pair.GetProperty("id").GetString()!;
            JsonElement valid = pair.GetProperty("valid");
            CStructCompilationOptions? compilation = null;
            if (valid.TryGetProperty("compilation", out JsonElement settings))
            {
                compilation = new CStructCompilationOptions
                {
                    CLongWidth = settings.TryGetProperty("cLongWidth", out JsonElement width) ? width.GetInt32() : 64,
                    DefaultEnumStorage = settings.TryGetProperty("defaultEnumStorage", out JsonElement storage) ? storage.GetString() : null,
                    BitfieldAllocation = settings.TryGetProperty("bitfieldAllocation", out JsonElement allocation) ? Enum.Parse<BitfieldAllocation>(allocation.GetString()!) : BitfieldAllocation.LowBitFirst,
                };
            }

            cases.Add(Fixture("valid/" + id, valid, compilation, null));
            JsonElement invalid = pair.GetProperty("invalid");
            if (invalid.GetProperty("stage").GetString() == "read")
            {
                long maxStringBytes = invalid.TryGetProperty("maxStringBytes", out JsonElement limit) ? limit.GetInt64() : 16 * 1024 * 1024;
                cases.Add(Fixture("invalid/" + id, invalid, null, new ReadOptions { MaxStringBytes = maxStringBytes, }));
            }
        }

        return cases;
    }

    /// <summary>
    ///     The layout examples of <c>contracts/language/portable-v1.json</c>, whose offset paths are also read and
    ///     resolved, and one layout per fixed, dynamic-numeric, and terminated primitive spelling -
    ///     <c>struct root { T value; uint8 tail; };</c> - over bytes that hold a NUL and a line feed in both widths.
    /// </summary>
    /// <returns>The cases.</returns>
    private static IEnumerable<EngineCorpusCase> LoadPortableContract()
    {
        using JsonDocument contract = JsonDocument.Parse(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "portable-v1.json")));
        JsonElement root = contract.RootElement;
        var cases = new List<EngineCorpusCase>();
        foreach (JsonElement example in root.GetProperty("layoutExamples").EnumerateArray())
        {
            cases.Add(Fixture("example/" + example.GetProperty("id").GetString(), example, null, null));
        }

        // Text that ends at a NUL or a line feed in one- and two-byte code units, then padding the fixed types read.
        byte[] primitiveInput = [0x41, 0x00, 0x42, 0x00, 0x00, 0x00, 0x0A, 0x00, 0x7E, 0x85, 0x01, 0x10, 0x20, 0x30, 0x40, 0x50, 0x60, 0x70, 0x80, 0x90, 0xA0];
        foreach (string list in (string[])["fixedPrimitives", "dynamicNumericPrimitives", "terminatedPrimitives"])
        {
            foreach (JsonElement primitive in root.GetProperty(list).EnumerateArray())
            {
                string spelling = primitive.GetProperty("spelling").GetString()!;
                string definition = "struct root { " + spelling + " value; uint8 tail; };";
                cases.Add(new EngineCorpusCase("primitive/" + spelling, () => new CStruct(definition), "root", primitiveInput));
            }
        }

        return cases;
    }

    /// <summary>
    ///     The layouts of <c>tests/CStructSharpTests/Quality/WellKnownFormatTests.cs</c>, read from its source: each
    ///     test's <c>definition</c> raw string and constructor arguments, over each byte list the test spells out as
    ///     hexadecimal literals. A test that builds its bytes in code uses the inspector sample with the same
    ///     definition, whose bytes that test verifies.
    /// </summary>
    /// <returns>The cases.</returns>
    private static IEnumerable<EngineCorpusCase> LoadWellKnownFormats()
    {
        string source = File.ReadAllText(Path.Combine(TestFixtures.RepositoryRoot, "tests", "CStructSharpTests", "Quality", "WellKnownFormatTests.cs")).ReplaceLineEndings("\n");
        IReadOnlyList<InspectorSchemaCatalog.Entry> samples = InspectorSchemaCatalog.Load(InspectorCatalogPath);
        var cases = new List<EngineCorpusCase>();
        foreach (string test in TestAttribute().Split(source).Skip(1))
        {
            string name = MethodName().Match(test).Groups[1].Value;
            Match raw = DefinitionLiteral().Match(test);
            if (!raw.Success)
            {
                continue;
            }

            // A raw string literal drops the closing delimiter's indentation from every line.
            string indentation = raw.Groups[2].Value;
            string definition = string.Join('\n', raw.Groups[1].Value.Split('\n').Select(line => line.StartsWith(indentation, StringComparison.Ordinal) ? line[indentation.Length..] : line.TrimStart()));
            Match constructor = Constructor().Match(test);
            string arguments = constructor.Success ? constructor.Groups[1].Value : string.Empty;
            byte pointerSize = PointerSizeArgument().Match(arguments) is { Success: true } size ? byte.Parse(size.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture) : (byte)8;
            bool littleEndian = !arguments.Contains("isLittleEndian: false", StringComparison.Ordinal);
            bool aligned = arguments.Contains("aligned: true", StringComparison.Ordinal);

            var inputs = new List<byte[]>();
            foreach (Match list in ByteList().Matches(test))
            {
                string text = list.Groups[1].Success ? list.Groups[1].Value : list.Groups[2].Value;
                string[] items = text.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                if (items.Length > 0 && items.All(item => ByteLiteral().IsMatch(item)))
                {
                    inputs.Add(items.Select(item => item.StartsWith("0x", StringComparison.Ordinal) ? Convert.ToByte(item[2..], 16) : byte.Parse(item, System.Globalization.CultureInfo.InvariantCulture)).ToArray());
                }
            }

            if (inputs.Count == 0 && samples.FirstOrDefault(sample => Normalize(sample.Definition) == Normalize(definition)) is { Bytes: { } sampleBytes, })
            {
                inputs.Add(sampleBytes);
            }

            for (int index = 0; index < inputs.Count; index++)
            {
                cases.Add(new EngineCorpusCase(name + "#" + index, () => new CStruct(definition, pointerSize, aligned, littleEndian), "root", inputs[index]));
            }
        }

        return cases;
    }

    /// <summary>
    ///     The inspector's schema catalog (<see cref="InspectorSchemaCatalog"/>): every detection schema, over its
    ///     format's sample bytes or 256 pseudo-random bytes, and every teaching sample over its own bytes.
    /// </summary>
    /// <returns>The cases.</returns>
    private static IEnumerable<EngineCorpusCase> LoadInspectorCatalog()
    {
        return InspectorSchemaCatalog.Load(InspectorCatalogPath).Select(
            entry => new EngineCorpusCase(
                entry.Id,
                () => new CStruct(entry.Definition, entry.PointerSize, entry.Aligned, entry.LittleEndian),
                entry.Root,
                entry.Bytes ?? TestFixtures.Xorshift(Seed(entry.Id), 256),
                Read: new ReadOptions { AddressingMode = entry.AddressingMode, }));
    }

    /// <summary>
    ///     The managed fuzz corpus (<c>tests/CStructSharp.Fuzz/corpus/fuzz-corpus.json</c>): every target's retained
    ///     seeds and its default number of mutations, replayed exactly as the fuzz session generates them
    ///     (<see cref="FuzzSession.Inputs"/>), applied as each target applies its input - a definition compiled with the
    ///     corpus limits, an expression sizing an array, a path over the path layout, and bytes read by the binary and
    ///     pointer-union layouts (all three for the generated differential) - with the corpus read and write limits.
    /// </summary>
    /// <returns>The cases by target name.</returns>
    private static Dictionary<string, EngineCorpusCase[]> LoadFuzzCorpus()
    {
        FuzzCorpus corpus = FuzzCorpus.Load(Path.Combine(AppContext.BaseDirectory, "fuzz-corpus.json"));
        FuzzLimits limits = corpus.Limits;
        var compilation = new CStructCompilationOptions
        {
            MaxDefinitionLength = limits.MaxDefinitionLength,
            MaxLayoutNestingDepth = limits.MaxLayoutNestingDepth,
            MaxExpressionNestingDepth = limits.MaxExpressionNestingDepth,
            MaxExpressionTokens = limits.MaxExpressionTokens,
        };
        var read = new ReadOptions
        {
            MaxArrayElements = limits.MaxArrayElements,
            MaxStringBytes = limits.MaxStringBytes,
            MaxTotalBytesRead = limits.MaxTotalBytesRead,
            MaxNestingDepth = limits.MaxNestingDepth,
            MaxPointerDepth = limits.MaxPointerDepth,
            MaxPointerTargetBytes = limits.MaxPointerTargetBytes,
        };
        var write = new WriteOptions
        {
            MaxArrayElements = limits.MaxArrayElements,
            MaxStringBytes = limits.MaxStringBytes,
            MaxTotalBytesWritten = limits.MaxTotalBytesWritten,
            MaxNestingDepth = limits.MaxNestingDepth,
        };

        var targets = new Dictionary<string, EngineCorpusCase[]>(StringComparer.Ordinal);
        foreach (FuzzTargetCorpus target in corpus.Targets)
        {
            var cases = new List<EngineCorpusCase>();
            foreach ((string name, int index, byte[] input) in FuzzSession.Inputs(target, target.Id, corpus.IterationsPerTarget, corpus.GetSeed(), corpus.MaxInputBytes))
            {
                string id = target.Id + "/" + name + "#" + index;
                string text = Encoding.UTF8.GetString(input);
                switch (target.Id)
                {
                case "definition":
                    bool aligned = input.Length % 2 == 0;
                    bool littleEndian = input.Length % 3 != 0;
                    cases.Add(new EngineCorpusCase(id, () => new CStruct(text, 2, aligned, littleEndian, compilation), null, TestFixtures.Xorshift(Seed(id), 32), Read: read, Write: write));
                    break;
                case "expression":
                    string sized = "struct root { byte values[(" + text + ") & 15]; };";
                    cases.Add(new EngineCorpusCase(id, () => new CStruct(sized, 2, compilationOptions: compilation), "root", TestFixtures.Xorshift(Seed(id), 16), Read: read, Write: write));
                    break;
                case "path":
                    cases.Add(new EngineCorpusCase(id, () => PathLayout.Layout, "root", new byte[32], Read: read, Write: write, Paths: [text]));
                    break;
                case "binary-roundtrip":
                    cases.Add(new EngineCorpusCase(id, () => BinaryLayout.Layout, "root", input, Read: read, Write: write));
                    break;
                case "pointer-union":
                    cases.Add(new EngineCorpusCase(id, () => PointerUnionLayout.Layout, "node", input, Read: read, Write: write));
                    break;
                default:
                    cases.Add(new EngineCorpusCase(id + "/binary", () => BinaryLayout.Layout, "root", input, Read: read, Write: write));
                    cases.Add(new EngineCorpusCase(id + "/path", () => PathLayout.Layout, "root", input, Read: read, Write: write));
                    cases.Add(new EngineCorpusCase(id + "/pointer-union", () => PointerUnionLayout.Layout, "node", input, Read: read, Write: write));
                    break;
                }
            }

            targets[target.Id] = [.. cases];
        }

        return targets;
    }

    /// <summary>A case from a fixture object with <c>definition</c>, <c>root</c>, constructor settings, <c>bytes</c> as hex, optional <c>variables</c> and <c>offsets</c>.</summary>
    /// <param name="id">The case id.</param>
    /// <param name="fixture">The fixture object.</param>
    /// <param name="compilation">The compilation options, or <see langword="null"/> for the defaults.</param>
    /// <param name="read">The read options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The case; its paths are the fixture's offset paths.</returns>
    private static EngineCorpusCase Fixture(string id, JsonElement fixture, CStructCompilationOptions? compilation, ReadOptions? read)
    {
        string definition = fixture.GetProperty("definition").GetString()!;
        byte pointerSize = fixture.GetProperty("pointerSize").GetByte();
        bool aligned = fixture.GetProperty("aligned").GetBoolean();
        bool littleEndian = fixture.GetProperty("littleEndian").GetBoolean();
        string[] paths = fixture.TryGetProperty("offsets", out JsonElement offsets) ? offsets.EnumerateObject().Select(offset => offset.Name).ToArray() : [];
        return new EngineCorpusCase(
            id,
            () => new CStruct(definition, pointerSize, aligned, littleEndian, compilation),
            fixture.GetProperty("root").GetString(),
            Convert.FromHexString(fixture.GetProperty("bytes").GetString()!),
            Variables(fixture),
            read,
            Paths: paths);
    }

    /// <summary>Returns whether a benchmark fixture's input is small enough, or it expects an error, which ends the read early.</summary>
    /// <param name="fixture">The fixture case object.</param>
    /// <returns><see langword="true"/> to include the fixture.</returns>
    private static bool Include(JsonElement fixture)
        => fixture.GetProperty("byteLength") is not { ValueKind: JsonValueKind.Number, } length || length.GetInt64() <= MaximumInputBytes ||
           fixture.GetProperty("expectedError").ValueKind == JsonValueKind.String;

    /// <summary>A benchmark fixture's input bytes, or 64 pseudo-random bytes for a compile-only fixture without input.</summary>
    /// <param name="fixture">The fixture case object.</param>
    /// <param name="id">The case id, which seeds the pseudo-random bytes.</param>
    /// <returns>The bytes.</returns>
    private static byte[] FixtureBytes(JsonElement fixture, string id)
        => fixture.GetProperty("bytes").ValueKind == JsonValueKind.Object ? TestFixtures.Materialize(fixture.GetProperty("bytes")) : TestFixtures.Xorshift(Seed(id), 64);

    /// <summary>The caller variables of a fixture's <c>variables</c> object, or <see langword="null"/> when it has none.</summary>
    /// <param name="fixture">The fixture object.</param>
    /// <returns>The variables.</returns>
    private static IReadOnlyDictionary<string, int>? Variables(JsonElement fixture)
    {
        if (!fixture.TryGetProperty("variables", out JsonElement variables) || variables.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        Dictionary<string, int> result = variables.EnumerateObject().ToDictionary(variable => variable.Name, variable => variable.Value.GetInt32(), StringComparer.Ordinal);
        return result.Count == 0 ? null : result;
    }

    /// <summary>The read options of a benchmark fixture's <c>readOptions</c> object; omitted members keep their defaults.</summary>
    /// <param name="fixture">The fixture case object.</param>
    /// <returns>The options, or <see langword="null"/> when the fixture sets none.</returns>
    private static ReadOptions? FixtureReadOptions(JsonElement fixture)
    {
        if (fixture.GetProperty("readOptions") is not { ValueKind: JsonValueKind.Object, } element)
        {
            return null;
        }

        var options = new ReadOptions();
        foreach (JsonProperty property in element.EnumerateObject())
        {
            options = property.Name switch
            {
                "maxArrayElements" => options with { MaxArrayElements = property.Value.GetInt32(), },
                "maxTotalBytesRead" => options with { MaxTotalBytesRead = property.Value.GetInt64(), },
                "maxStringBytes" => options with { MaxStringBytes = property.Value.GetInt64(), },
                "maxPointerDepth" => options with { MaxPointerDepth = property.Value.GetInt32(), },
                "maxNestingDepth" => options with { MaxNestingDepth = property.Value.GetInt32(), },
                "addressingMode" => options with { AddressingMode = Enum.Parse<PointerAddressingMode>(property.Value.GetString()!, ignoreCase: true), },
                "origin" => options with { Origin = property.Value.GetInt64(), },
                "dereferencePointers" => options with { DereferencePointers = property.Value.GetBoolean(), },
                _ => throw new InvalidDataException("Unknown fixture read option " + property.Name),
            };
        }

        return options;
    }

    /// <summary>
    ///     The bytes the engine writes for a
    ///     shape layout's values (a union root sets its one member); a zeroed
    ///     buffer of the root's size when the values cannot be written.
    /// </summary>
    /// <param name="layout">The compiled shape layout.</param>
    /// <param name="root">The shape's root.</param>
    /// <param name="values">The shape's <c>values</c> object.</param>
    /// <returns>The input bytes.</returns>
    private static byte[] ShapeBytes(CStruct layout, string root, JsonElement values)
    {
        object value = ShapeValue(values)!;
        if (layout.Layout.Declarations.First(item => item.Name == root).Kind == LayoutDeclarationKind.Union)
        {
            KeyValuePair<string, object?> member = ((Dictionary<string, object?>)value).Single();
            value = UnionValue.FromMember(root, member.Key, member.Value);
        }

        try
        {
            return layout.Serialize(root, value, options: new WriteOptions());
        }
        catch (Diagnostics.CStructException)
        {
            return new byte[layout.GetStructSizeInBytes(root)];
        }
    }

    /// <summary>Converts a JSON shape value: numbers to the narrowest of <see cref="int"/>, <see cref="long"/>, <see cref="ulong"/>, <see cref="double"/>; arrays and objects recursively.</summary>
    /// <param name="element">The JSON value.</param>
    /// <returns>The value to write.</returns>
    private static object? ShapeValue(JsonElement element)
    {
        switch (element.ValueKind)
        {
        case JsonValueKind.Number:
            if (element.TryGetInt32(out int integer))
            {
                return integer;
            }

            if (element.TryGetInt64(out long wide))
            {
                return wide;
            }

            return element.TryGetUInt64(out ulong unsigned) ? unsigned : element.GetDouble();
        case JsonValueKind.String:
            return element.GetString();
        case JsonValueKind.True:
            return true;
        case JsonValueKind.False:
            return false;
        case JsonValueKind.Array:
            return element.EnumerateArray().Select(ShapeValue).ToArray();
        case JsonValueKind.Object:
            return element.EnumerateObject().ToDictionary(property => property.Name, property => ShapeValue(property.Value), StringComparer.Ordinal);
        default:
            return null;
        }
    }

    /// <summary>A stable 32-bit FNV-1a hash of an id, which seeds the id's pseudo-random input.</summary>
    /// <param name="id">The id.</param>
    /// <returns>The seed.</returns>
    private static uint Seed(string id)
    {
        uint hash = 2166136261;
        foreach (byte item in Encoding.UTF8.GetBytes(id))
        {
            hash = (hash ^ item) * 16777619;
        }

        return hash;
    }

    /// <summary>Collapses whitespace so two spellings of one definition compare equal.</summary>
    /// <param name="definition">The definition.</param>
    /// <returns>The normalized text.</returns>
    private static string Normalize(string definition) => string.Join(' ', definition.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries));

    /// <summary>Matches the test attribute that starts each test method.</summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex(@"\[TestMethod\]")]
    private static partial Regex TestAttribute();

    /// <summary>Matches a test method's name.</summary>
    /// <returns>The expression; group 1 is the name.</returns>
    [GeneratedRegex(@"public (?:async Task|void) (\w+)\(")]
    private static partial Regex MethodName();

    /// <summary>Matches the <c>definition</c> raw string literal.</summary>
    /// <returns>The expression; group 1 is the content lines, group 2 the closing delimiter's indentation.</returns>
    [GeneratedRegex("const string definition = \"\"\"\n(.*?)\n([ ]*)\"\"\";", RegexOptions.Singleline)]
    private static partial Regex DefinitionLiteral();

    /// <summary>Matches the arguments of the <c>new CStruct(definition, ...)</c> call.</summary>
    /// <returns>The expression; group 1 is the arguments after the definition.</returns>
    [GeneratedRegex(@"new CStruct\(definition([^)]*)\)")]
    private static partial Regex Constructor();

    /// <summary>Matches a <c>pointerSize: N</c> argument.</summary>
    /// <returns>The expression; group 1 is the size.</returns>
    [GeneratedRegex(@"pointerSize: (\d+)")]
    private static partial Regex PointerSizeArgument();

    /// <summary>Matches a byte list: <c>byte[] bytes = [ ... ];</c> or <c>new byte[] { ... }</c>.</summary>
    /// <returns>The expression; group 1 (the array form) or group 2 (the <c>new</c> form) is the comma-separated items.</returns>
    [GeneratedRegex(@"(?:byte\[\] bytes =\s*\[([^\]]*)\];|new byte\[\] \{([^}]*)\})", RegexOptions.Singleline)]
    private static partial Regex ByteList();

    /// <summary>Matches one byte literal: hexadecimal such as <c>0x4d</c>, or decimal such as <c>7</c>.</summary>
    /// <returns>The expression.</returns>
    [GeneratedRegex("^(?:0x[0-9A-Fa-f]{1,2}|[0-9]{1,3})$")]
    private static partial Regex ByteLiteral();
}
