extern alias Generators;

namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Generators::CStructSharp.Generators;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

/// <summary>
///     Runs <see cref="CStructLayoutGenerator"/> over one consumer source file the way the compiler would: the
///     consumer compilation references the runtime assembly, the generator adds its output, and the result is the
///     generated sources, the generator's diagnostics, and the output compilation (so a test can assert that the
///     generated code compiles, or load and run it).
/// </summary>
internal static class GeneratorRunner
{
    private static readonly Lazy<ImmutableArray<MetadataReference>> References = new(CreateReferences);

    public static GeneratorResult Run(
        string source,
        IReadOnlyList<(string Path, string Text)>? additionalFiles = null,
        LanguageVersion languageVersion = LanguageVersion.CSharp12,
        string assemblyName = "Consumer",
        IReadOnlyDictionary<string, string>? globalOptions = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? fileOptions = null)
    {
        var parseOptions = new CSharpParseOptions(languageVersion);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, parseOptions, path: "Consumer.cs");
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));

        GeneratorDriver driver = CSharpGeneratorDriver.Create([new CStructLayoutGenerator().AsSourceGenerator(), new CStructMappedGenerator().AsSourceGenerator()], parseOptions: parseOptions);
        if (additionalFiles is { Count: > 0 })
        {
            driver = driver.AddAdditionalTexts([.. additionalFiles.Select(file => (AdditionalText)new InMemoryAdditionalText(file.Path, file.Text))]);
        }

        if (globalOptions is not null || fileOptions is not null)
        {
            // The build's CompilerVisibleProperty values and AdditionalFiles metadata, as MSBuild hands them to analyzers.
            driver = driver.WithUpdatedAnalyzerConfigOptions(new InMemoryOptionsProvider(globalOptions, fileOptions));
        }

        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out Compilation output, out ImmutableArray<Diagnostic> generatorDiagnostics);
        GeneratorDriverRunResult runResult = driver.GetRunResult();
        var generated = runResult.Results
            .SelectMany(result => result.GeneratedSources)
            .Select(generatedSource => (generatedSource.HintName, generatedSource.SourceText.ToString()))
            .ToArray();
        return new GeneratorResult(generated, generatorDiagnostics, output, runResult);
    }

    private static ImmutableArray<MetadataReference> CreateReferences()
    {
        var references = new List<MetadataReference>();
        string? trusted = AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES") as string;
        foreach (string path in (trusted ?? string.Empty).Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.StartsWith("System.", StringComparison.Ordinal) || name is "mscorlib" or "netstandard" || name.StartsWith("Microsoft.CSharp", StringComparison.Ordinal))
            {
                references.Add(MetadataReference.CreateFromFile(path));
            }
        }

        references.Add(MetadataReference.CreateFromFile(typeof(CStruct).Assembly.Location));
        return [.. references];
    }

    /// <summary>The build's options as an analyzer sees them, for running the analyzer over a compilation.</summary>
    public static AnalyzerConfigOptionsProvider OptionsProvider(IReadOnlyDictionary<string, string>? globalOptions, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? fileOptions)
        => new InMemoryOptionsProvider(globalOptions, fileOptions);

    private sealed class InMemoryOptionsProvider : AnalyzerConfigOptionsProvider
    {
        private readonly InMemoryOptions global;
        private readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> files;

        public InMemoryOptionsProvider(IReadOnlyDictionary<string, string>? globalOptions, IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? fileOptions)
        {
            this.global = new InMemoryOptions(globalOptions ?? new Dictionary<string, string>(StringComparer.Ordinal));
            this.files = fileOptions ?? new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
        }

        public override AnalyzerConfigOptions GlobalOptions => this.global;

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => this.global;

        public override AnalyzerConfigOptions GetOptions(AdditionalText textFile)
            => this.files.TryGetValue(textFile.Path, out IReadOnlyDictionary<string, string>? options) ? new InMemoryOptions(options) : new InMemoryOptions(new Dictionary<string, string>(StringComparer.Ordinal));

        private sealed class InMemoryOptions : AnalyzerConfigOptions
        {
            private readonly IReadOnlyDictionary<string, string> values;

            public InMemoryOptions(IReadOnlyDictionary<string, string> values)
            {
                this.values = values;
            }

            public override bool TryGetValue(string key, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? value) => this.values.TryGetValue(key, out value);
        }
    }

    private sealed class InMemoryAdditionalText : AdditionalText
    {
        private readonly SourceText text;

        public InMemoryAdditionalText(string path, string text)
        {
            this.Path = path;
            this.text = SourceText.From(text, Encoding.UTF8);
        }

        public override string Path { get; }

        public override SourceText GetText(System.Threading.CancellationToken cancellationToken = default) => this.text;
    }
}
