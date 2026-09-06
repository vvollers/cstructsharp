namespace CStructSharp.Tests;

using CStructSharp.Fuzzing;

/// <summary>Checks the replay CLI and malformed corpus boundaries that are not reached by the default campaign.</summary>
[TestClass]
public class ManagedFuzzCliTests
{
    /// <summary>
    ///     The fuzz command-line tool must show help, list targets, save a report, and replay a supplied input
    ///     successfully.
    /// </summary>
    /// <remarks>
    ///     The input file contains a simple one-byte struct declaration. This tests the tool used to reproduce
    ///     robustness failures, not the workbench's interactive parser.
    /// </remarks>
    [TestMethod]
    public void Run_SupportsDiscoveryReportsAndSingleInputReplay()
    {
        string temporaryDirectory = CreateTemporaryDirectory();
        try
        {
            string reportPath = Path.Combine(temporaryDirectory, "reports", "definition.json");
            string inputPath = Path.Combine(temporaryDirectory, "input.bin");
            File.WriteAllText(inputPath, "struct root { byte value; };");

            Assert.AreEqual(0, Program.Run(["--help",]));
            Assert.AreEqual(0, Program.Run(["--list-targets",]));
            Assert.AreEqual(
                0,
                Program.Run(
                [
                    "--target",
                    "definition",
                    "--iterations",
                    "0",
                    "--seed",
                    "0x0123456789ABCDEF",
                    "--max-input-bytes",
                    "128",
                    "--report",
                    reportPath,
                ]));
            Assert.IsTrue(File.Exists(reportPath));
            Assert.AreEqual(
                0,
                Program.Run(
                [
                    "--target",
                    "definition",
                    "--input",
                    inputPath,
                ]));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    /// <summary>
    ///     Unknown or repeated switches, missing values, invalid iteration counts or seeds, and incomplete replay
    ///     arguments must be rejected before a fuzz target runs.
    /// </summary>
    /// <remarks>
    ///     Clear argument failures prevent an accidental test configuration from being mistaken for meaningful parser
    ///     evidence.
    /// </remarks>
    [TestMethod]
    public void Run_RejectsInvalidOptionsAndValues()
    {
        Assert.Throws<ArgumentException>(() => Program.Run(["--unknown",]));
        Assert.Throws<ArgumentException>(() => Program.Run(["--target",]));
        Assert.Throws<ArgumentException>(() => Program.Run(["--help", "--help",]));
        Assert.Throws<ArgumentException>(() => Program.Run(["--iterations", "not-a-number",]));
        Assert.Throws<ArgumentException>(() => Program.Run(["--seed", "not-hex",]));
        Assert.Throws<ArgumentException>(() => Program.Run(["--input", "unused.bin",]));
    }

    /// <summary>
    ///     The corpus loader receives invalid metadata, seed values, encodings, and target definitions.
    /// </summary>
    /// <remarks>
    ///     Each must fail rather than silently skip or reinterpret inputs. A trustworthy fuzz run requires validating
    ///     its test data and resource bounds before testing the library itself.
    /// </remarks>
    [TestMethod]
    public void Corpus_RejectsMalformedDocumentsAndSeedModels()
    {
        Assert.Throws<InvalidDataException>(() => new FuzzCorpus { Seed = "invalid", }.GetSeed());
        Assert.Throws<InvalidDataException>(
            () => new FuzzSeed { Id = "bad", Encoding = "base64", Data = "AA==", }.Decode());

        AssertInvalidCorpus("{}");
        AssertInvalidCorpus(
            """
            {
              "schemaVersion": 1,
              "seed": "0x0000000000000001",
              "iterationsPerTarget": 1,
              "maxInputBytes": 4,
              "limits": {},
              "targets": [{ "id": "", "seeds": [] }]
            }
            """);
        AssertInvalidCorpus(
            """
            {
              "schemaVersion": 1,
              "seed": "0x0000000000000001",
              "iterationsPerTarget": 1,
              "maxInputBytes": 1,
              "limits": {},
              "targets": [{
                "id": "definition",
                "seeds": [{ "id": "large", "encoding": "hex", "data": "AABB" }]
              }]
            }
            """);
        AssertInvalidCorpus(
            """
            {
              "schemaVersion": 1,
              "seed": "0x0000000000000001",
              "iterationsPerTarget": 1,
              "maxInputBytes": 1,
              "limits": {},
              "targets": [{
                "id": "definition",
                "seeds": [{ "id": "empty", "encoding": "utf8", "data": "" }]
              }]
            }
            """);
    }

    private static void AssertInvalidCorpus(string json)
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "cstructsharp-fuzz-invalid-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            File.WriteAllText(path, json);
            Assert.Throws<InvalidDataException>(() => FuzzCorpus.Load(path));
        }
        finally
        {
            File.Delete(path);
        }
    }

    private static string CreateTemporaryDirectory()
    {
        string path = Path.Combine(
            Path.GetTempPath(),
            "cstructsharp-fuzz-cli-" + Guid.NewGuid().ToString("N"));
        return Directory.CreateDirectory(path).FullName;
    }
}
