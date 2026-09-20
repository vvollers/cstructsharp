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
        string assemblyName = "Consumer")
    {
        var parseOptions = new CSharpParseOptions(languageVersion);
        SyntaxTree tree = CSharpSyntaxTree.ParseText(source, parseOptions, path: "Consumer.cs");
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [tree],
            References.Value,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable, allowUnsafe: true));

        GeneratorDriver driver = CSharpGeneratorDriver.Create([new CStructLayoutGenerator().AsSourceGenerator()], parseOptions: parseOptions);
        if (additionalFiles is { Count: > 0 })
        {
            driver = driver.AddAdditionalTexts([.. additionalFiles.Select(file => (AdditionalText)new InMemoryAdditionalText(file.Path, file.Text))]);
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
