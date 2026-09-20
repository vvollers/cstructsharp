namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;

/// <summary>What one generator run produced.</summary>
internal sealed class GeneratorResult
{
    public GeneratorResult(
        IReadOnlyList<(string HintName, string Source)> generatedSources,
        ImmutableArray<Diagnostic> generatorDiagnostics,
        Compilation output,
        GeneratorDriverRunResult runResult)
    {
        this.GeneratedSources = generatedSources;
        this.GeneratorDiagnostics = generatorDiagnostics;
        this.Output = output;
        this.RunResult = runResult;
    }

    public IReadOnlyList<(string HintName, string Source)> GeneratedSources { get; }

    public ImmutableArray<Diagnostic> GeneratorDiagnostics { get; }

    public Compilation Output { get; }

    public GeneratorDriverRunResult RunResult { get; }

    /// <summary>The single generated source, when exactly one class was attributed.</summary>
    public string Source => this.GeneratedSources.Count == 1
                                ? this.GeneratedSources[0].Source
                                : throw new InvalidOperationException($"Expected one generated source, found {this.GeneratedSources.Count}: {string.Join(", ", this.GeneratedSources.Select(source => source.HintName))}.");

    /// <summary>The generator diagnostics of one id.</summary>
    public IReadOnlyList<Diagnostic> DiagnosticsWithId(string id) => [.. this.GeneratorDiagnostics.Where(diagnostic => diagnostic.Id == id)];

    /// <summary>Fails the test when the generator reported anything or the consumer compilation (with the generated code) has errors.</summary>
    public GeneratorResult AssertClean()
    {
        Assert.IsEmpty(this.GeneratorDiagnostics, "Generator diagnostics: " + string.Join("\n", this.GeneratorDiagnostics.Select(diagnostic => diagnostic.ToString())));
        ImmutableArray<Diagnostic> errors = this.Output.GetDiagnostics().Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToImmutableArray();
        Assert.IsEmpty(errors, "Output compilation errors:\n" + string.Join("\n", errors.Select(diagnostic => diagnostic.ToString())) + "\n\nGenerated:\n" + string.Join("\n", this.GeneratedSources.Select(source => Excerpt(source.Source, errors))));
        return this;
    }

    /// <summary>The generated lines around each error (with line numbers), so a failure message stays readable.</summary>
    private static string Excerpt(string source, ImmutableArray<Diagnostic> errors)
    {
        string[] lines = source.Split('\n');
        var wanted = new SortedSet<int>();
        foreach (Diagnostic error in errors)
        {
            int line = error.Location.GetLineSpan().StartLinePosition.Line;
            for (int offset = -6; offset <= 6; offset++)
            {
                if (line + offset >= 0 && line + offset < lines.Length)
                {
                    wanted.Add(line + offset);
                }
            }
        }

        return string.Join("\n", wanted.Select(index => (index + 1).ToString(System.Globalization.CultureInfo.InvariantCulture).PadLeft(5) + ": " + lines[index]));
    }

    /// <summary>Compiles the consumer plus the generated code to an in-memory assembly and loads it, so tests can run the generated members.</summary>
    public Assembly Load()
    {
        using var stream = new MemoryStream();
        Microsoft.CodeAnalysis.Emit.EmitResult emitted = this.Output.Emit(stream);
        Assert.IsTrue(emitted.Success, string.Join("\n", emitted.Diagnostics.Where(diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).Select(diagnostic => diagnostic.ToString())));
        return Assembly.Load(stream.ToArray());
    }
}
