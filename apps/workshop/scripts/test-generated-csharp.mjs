import { createServer } from "vite";
import { mkdir, writeFile } from "node:fs/promises";
import { resolve } from "node:path";
import { spawnSync } from "node:child_process";

const server = await createServer({ server: { middlewareMode: true } });
try {
  const { generateExample } = await server.ssrLoadModule("/src/generate-example.ts");
  const { lessons } = await server.ssrLoadModule("/src/lessons.ts");
  const directory = resolve("artifacts/generated-csharp-check");
  await mkdir(directory, { recursive: true });
  const imports = new Set();
  const classes = [];
  const calls = [];
  for (const [index, lesson] of lessons.entries()) {
    const preset = lesson.operations[lesson.operation];
    const source = generateExample({
      operation: lesson.operation,
      definition: lesson.definition,
      binaryHex: lesson.binaryHex,
      jsonValue: preset.json ?? "{}",
      path: preset.path ?? "",
      options: { rootTypeName: lesson.rootType, ...lesson.parserOptions, ...lesson.options },
    }).csharp;
    const body = source.replace(/^using .+;$/gm, (line) => {
      imports.add(line);
      return "";
    });
    classes.push(`static class Scenario${index} { public static void Run() {\n${body}\n} }`);
    calls.push(
      `Check(${JSON.stringify(lesson.id)}, Scenario${index}.Run, ${!!preset.expected.error}, ${preset.expected.hex ? JSON.stringify(preset.expected.hex.replaceAll(" ", "").toUpperCase()) : "null"});`,
    );
  }
  const runner = `
${calls.join("\n")}
static void Check(string id, Action run, bool expectError, string? expectedHex)
{
    var stdout = Console.Out; var stderr = Console.Error;
    using var output = new StringWriter(); using var error = new StringWriter();
    try { Console.SetOut(output); Console.SetError(error); run(); }
    finally { Console.SetOut(stdout); Console.SetError(stderr); }
    if (expectError != (error.ToString().Length > 0)) throw new Exception(id + ": " + error + output);
    if (!expectError && expectedHex != null && output.ToString().Trim() != expectedHex) throw new Exception(id + ": wrong output " + output);
    if (!expectError && expectedHex == null) { using var parsed = JsonDocument.Parse(output.ToString()); }
    Console.WriteLine("PASS generated C# " + id);
}
`;
  await writeFile(
    resolve(directory, "Program.cs"),
    [...imports].join("\n") + runner + classes.join("\n"),
  );
  const project = resolve("../../src/CStructSharp/CStructSharp.csproj").replaceAll("&", "&amp;");
  await writeFile(
    resolve(directory, "Generated.csproj"),
    `<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><OutputType>Exe</OutputType><TargetFramework>net10.0</TargetFramework><Nullable>enable</Nullable></PropertyGroup><ItemGroup><ProjectReference Include="${project}" /></ItemGroup></Project>`,
  );
  const result = spawnSync(
    "dotnet",
    ["run", "--project", resolve(directory, "Generated.csproj"), "-c", "Release"],
    { stdio: "inherit" },
  );
  if (result.error) throw result.error;
  process.exitCode = result.status ?? 1;
} finally {
  await server.close();
}
