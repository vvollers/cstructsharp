#!/usr/bin/env node
/**
 * Onboarding check against the built NuGet package: the packaged README's code equals the tested starter, every
 * recipe is regenerated, and a fresh console project on net8.0 and net10.0 runs the starter, its continuation,
 * the byte-array consumer, and every recipe against the package from a temporary feed.
 *
 *   node tools/packaging/test-onboarding-package.mjs --package-directory <dir>
 */
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { assertCondition, main, parseArguments, repositoryRoot, runCommand } from "../lib/tooling.mjs";
import { readManifest, singlePackage } from "../lib/nuget.mjs";

const options = parseArguments(process.argv.slice(2), { "package-directory": "string" });
assertCondition(options["package-directory"], "Option --package-directory is required.");

function checkedDotnet(args, cwd, env) {
  const result = runCommand("dotnet", args, { cwd, env, allowFailure: true });
  const output = `${result.stdout ?? ""}${result.stderr ?? ""}`;
  if (result.status !== 0) throw new Error(output);
  return output;
}

const escapeXml = (text) => text.replace(/&/g, "&amp;").replace(/</g, "&lt;").replace(/>/g, "&gt;").replace(/"/g, "&quot;").replace(/'/g, "&apos;");

await main(() => {
  const packagePath = singlePackage(options["package-directory"], "Provide a directory containing exactly one CStructSharp .nupkg.");
  const { version, archive } = readManifest(packagePath);
  if (!archive.entries.some((entry) => entry.name === "README.md")) throw new Error("The package README is missing.");
  const readme = archive.read("README.md").toString("utf8");

  const exportResult = runCommand(process.execPath, [path.join(repositoryRoot, "tools/documentation/export-documentation-examples.mjs")], { allowFailure: true });
  process.stdout.write(exportResult.stdout ?? "");
  if (exportResult.status !== 0) throw new Error("Recipe generation failed.");
  const recipesDirectory = path.join(repositoryRoot, "docs/examples/recipes");
  const recipes = fs.readdirSync(recipesDirectory).filter((name) => name.endsWith(".cs")).sort();
  if (recipes.length !== 33) throw new Error("Expected all 33 recipes.");
  const starter = path.join(repositoryRoot, "docs/examples/starter");
  const documentedCode = (/```csharp\r?\n([\s\S]*?)\r?\n```/.exec(readme)?.[1] ?? "").trim();
  if (documentedCode !== fs.readFileSync(path.join(starter, "Program.cs"), "utf8").trim()) throw new Error("The packaged README code does not match the tested starter.");

  const work = fs.mkdtempSync(path.join(os.tmpdir(), "cstructsharp-onboarding-"));
  const source = escapeXml(path.dirname(packagePath));
  fs.writeFileSync(
    path.join(work, "NuGet.config"),
    `<configuration><packageSources><clear/><add key="candidate" value="${source}"/><add key="nuget" value="https://api.nuget.org/v3/index.json"/></packageSources><packageSourceMapping><packageSource key="candidate"><package pattern="CStructSharp"/></packageSource><packageSource key="nuget"><package pattern="*"/></packageSource></packageSourceMapping></configuration>\n`,
  );
  const program = path.join(work, "Program.cs");
  const setProgram = (file) => fs.writeFileSync(program, fs.readFileSync(file, "utf8"));
  const normalize = (text) => text.replaceAll("\r\n", "\n").trim();
  try {
    for (const framework of ["net8.0", "net10.0"]) {
      checkedDotnet(["new", "console", "-n", "Starter", "-o", ".", "-f", "net10.0", "--no-restore", "--force"], work);
      const csproj = path.join(work, "Starter.csproj");
      if (framework === "net8.0") {
        fs.writeFileSync(csproj, fs.readFileSync(csproj, "utf8").replace("<TargetFramework>net10.0</TargetFramework>", "<TargetFramework>net8.0</TargetFramework>"));
      }
      checkedDotnet(["add", "Starter.csproj", "package", "CStructSharp", "--version", version, "--no-restore"], work);
      setProgram(path.join(starter, "Program.cs"));
      checkedDotnet(["restore", "Starter.csproj", "--packages", path.join(work, "packages"), "--force"], work);
      let output = checkedDotnet(["run", "--project", "Starter.csproj", "--no-restore"], work);
      if (normalize(output) !== "kind = 2\nlength = 6") throw new Error(`Unexpected starter output: ${output}`);
      setProgram(path.join(starter, "Next.cs"));
      output = checkedDotnet(["run", "--project", "Starter.csproj", "--no-restore"], work);
      for (const line of ["Created: 020006000000", "Updated: 030006000000", "Kind = 3; Length = 6", "Truncated read succeeds = False"]) {
        if (!output.includes(line)) throw new Error(`Missing expected continuation output '${line}': ${output}`);
      }
      setProgram(path.join(starter, "Generated.cs"));
      output = checkedDotnet(["run", "--project", "Starter.csproj", "--no-restore"], work);
      for (const line of ["kind = 2; length = 6", "Serialized: 030006000000", "Updated: 020007000000 (offset 2, size 6)", "view length = 7", "mapped kind = 2"]) {
        if (!output.includes(line)) throw new Error(`Missing expected generated-starter output '${line}': ${output}`);
      }
      console.log(`PASS external ${framework} starter, continuation, and generated starter against package ${version}`);
      const languageVersion = framework === "net8.0" ? "12.0" : "latest";
      setProgram(path.join(repositoryRoot, "tools/fixtures/byte-array-consumer.cs"));
      output = checkedDotnet(["run", "--project", "Starter.csproj", "--no-restore", `-p:LangVersion=${languageVersion}`], work);
      if (!output.includes("PASS byte-array consumer")) throw new Error(`Byte-array consumer failed: ${output}`);
      console.log(`PASS external ${framework} byte-array consumer with C# ${languageVersion}`);
    }
    for (const recipe of recipes) {
      setProgram(path.join(recipesDirectory, recipe));
      const output = checkedDotnet(["run", "--project", "Starter.csproj", "--no-restore"], work);
      const name = recipe.slice(0, -3);
      if (!output.includes(`PASS ${name}`)) throw new Error(`Recipe did not report success: ${output}`);
      console.log(`PASS external recipe ${name}`);
    }
  } finally {
    fs.rmSync(work, { recursive: true, force: true });
  }
});
