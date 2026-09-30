/** Exports complete recipe programs/pages from executable sources. Usage: node tools/documentation/export-documentation-examples.mjs. */
import fs from "node:fs";
import path from "node:path";
import { recipes, scenarioNames } from "../lib/documentation-recipes.mjs";
import { repositoryRoot as root } from "../lib/tooling.mjs";

const examples = path.join(root, "docs/examples");
const original = fs.readFileSync(path.join(examples, "Program.cs"), "utf8");
// The runner includes scenarios beyond the exported recipes; its final total must come from its registration list.
const scenarioCount = scenarioNames(original).length;
const more = fs.readFileSync(path.join(examples, "MoreExamples.cs"), "utf8");
const binaryTypes = fs.readFileSync(path.join(examples, "BinaryTypeExamples.cs"), "utf8");
const parity = fs.readFileSync(path.join(examples, "DissectParityExamples.cs"), "utf8");
const generated = fs.readFileSync(path.join(examples, "GeneratedExamples.cs"), "utf8");
const asyncExamples = fs.readFileSync(path.join(examples, "AsyncExamples.cs"), "utf8");
const sequenceExamples = fs.readFileSync(path.join(examples, "SequenceExamples.cs"), "utf8");
/**
 * Returns the text of Program.cs from the first occurrence of `start` up to the next `end`, without trailing
 * whitespace.
 */
function between(start, end) {
  const first = original.indexOf(start);
  return original.slice(first, original.indexOf(end, first)).trimEnd();
}
/** The body of a `#region name` in the generated-code examples (the attributed classes a generated recipe needs). */
function generatedRegion(name) {
  const start = generated.indexOf(`#region ${name}\n`);
  if (start < 0) throw new Error(`Missing generated example region ${name}`);
  const body = generated.slice(generated.indexOf("\n", start) + 1, generated.indexOf("#endregion", start));
  return body.trimEnd();
}
const support = [
  [/\bEqual(?:<[^>]*>)?\(/, between("    private static void Equal<T>", "    private static void True")],
  [/\bTrue\(/, between("    private static void True", "    private static void SequenceEqual")],
  [/\bSequenceEqual\(/, between("    private static void SequenceEqual", "    private static void Throws")],
  [/\bThrows</, between("    private static void Throws", "    public sealed class Header")],
  [/(?<!\.)\bHeader[?> ]/, between("    public sealed class Header", "    #region api-guide-map-poco-type")],
  [/\bPoint[> ]/, between("    public sealed class Point", "    #endregion")],
  // The generated-code recipes carry their [CStructLayout]/[CStructMapped] classes; the package's generator fills them in.
  [/\bWire\./, generatedRegion("generated-first-layout-class")],
  [/\bSamples\./, generatedRegion("generated-arrays-strings-enums-class")],
  [/\b(?:HeaderRecord|SampleRecord)\b/, generatedRegion("generated-mapped-classes-class")],
];

const browserLessons = {
  "nested-array": "arrays", "aligned-header": "alignment", "bit-flags": "bitfields",
  "terminated-text": "terminated-text", "preserve-enum": "enum", "preserve-union": "union",
  "follow-pointer": "pointer",
  "integers-24": "integers-24", "bounded-encodings": "bounded-encodings",
  "variable-integers": "variable-integers", "fixed-point": "fixed-point",
  "identifier-order": "identifier-order", "conditional-records": "conditional-records",
  "windows-header": "windows-header", "flags-and-data-sized-arrays": "flags", "header-preprocessor": "header-preprocessor",
};
for (const recipe of recipes) recipe[9] ??= browserLessons[recipe[0]] ?? null;

const out = path.join(examples, "recipes");
fs.mkdirSync(out, { recursive: true });
const toc = ["items:"];
for (const [id, title, level, region, expected, explanation, exercise, answer, guide, lesson] of recipes) {
  const text = [original, more, binaryTypes, parity, generated, asyncExamples, sequenceExamples].find(source => source.includes(`#region ${region}\n`) || source.includes(`#region ${region}\r\n`));
  if (!text) throw new Error(`Missing example region ${region}`);
  // The exact region: a `-class` region shares the prefix of the scenario region it supports.
  const start = text.search(new RegExp(`#region ${region}\\r?\\n`));
  if (start < 0) throw new Error(`Missing example region ${region}`);
  const end = text.indexOf("#endregion", start);
  const method = text.slice(text.indexOf("\n", start) + 1, end).trimEnd();
  // A recipe method is `private static void X()` or, for the awaitable forms, `private static async Task X()`;
  // the runner registers the latter as `() => X().GetAwaiter().GetResult()` and the exported program awaits it.
  const signature = method.match(/private static (void|async Task) (\w+)\(/);
  const methodName = signature?.[2];
  const isAsync = signature?.[1] === "async Task";
  const registration = isAsync ? `("${id}", () => ${methodName}().GetAwaiter().GetResult())` : `("${id}", ${methodName})`;
  if (!methodName || !original.includes(registration)) throw new Error(`Scenario ${id} is not registered`);
  const helpers = support.filter(([pattern]) => pattern.test(method)).map(([, code]) => code).join("\n\n") + "\n";
  const main = isAsync ? `    public static async Task Main()\n    {\n        await ${methodName}();\n        Console.WriteLine("PASS ${id}");\n    }` : `    public static void Main()\n    {\n        ${methodName}();\n        Console.WriteLine("PASS ${id}");\n    }`;
  const code = `// Generated from executable documentation examples. Edit the source region, then regenerate.\nusing System;\nusing System.IO;\nusing System.IO.Pipelines;\nusing System.Linq;\nusing System.Buffers;\nusing System.Collections.Generic;\nusing System.Dynamic;\nusing System.Globalization;\nusing System.Numerics;\nusing System.Runtime.CompilerServices;\nusing System.Threading;\nusing System.Threading.Tasks;\nusing CStructSharp;\nusing CStructSharp.Codecs;\nusing CStructSharp.Diagnostics;\nusing CStructSharp.Introspection;\nusing CStructSharp.Values;\n\ninternal static partial class Program\n{\n${main}\n\n${method}\n\n${helpers}}\n`;
  fs.writeFileSync(path.join(out, `${id}.cs`), code);
  fs.writeFileSync(path.join(out, `${id}.md`), `---\ntitle: ${title}\ndescription: Run the complete ${id} example and check its values and bytes.\n---\n\n# ${title}\n\n**${level} · C#**. ${explanation}\n\n## Run this example\n\nPrerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:\n\n\`\`\`sh\ndotnet run --project docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- ${id}\n\`\`\`\n\nThe runner checks ${expected}. Success includes \`PASS ${id}\`.\n${lesson ? `\n[Try the related browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=${lesson}).\n` : "\nThis example uses the C# API. Browser capabilities and result shapes are described in the [browser guide](../../guides/browser/api.md).\n"}\n## Complete program\n\nThe layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the\nrepository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.\nThese examples follow the source version; use a matching package when testing a release.\n\n[Download the complete C# source](${id}.cs).\n\n[!code-csharp[Complete ${id} program](${id}.cs)]\n\n## Try it and diagnose mistakes\n\n${exercise}\n\nAnswer: ${answer} The program contains assertions for its original inputs. When changing an input intentionally,\nupdate the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.\n\nContinue with [the related guide](../../guides/${guide}.md) or [choose another recipe](../../guides/recipes/index.md).\n`);
  toc.push(`- name: ${title}`, `  href: ${id}.md`);
}
fs.writeFileSync(path.join(out, "toc.yml"), `${toc.join("\n")}\n`);
const catalog = ["---", "title: Tested recipes", "description: Choose a complete executable recipe by task, difficulty, and platform.", "---", "", "# Tested recipes", "", "Start with the [first C# program](../install-and-first-parse.md) or the [Node.js and browser quick start](../browser/index.md).", `These ${recipes.length} recipes include complete programs, exact byte/value checks, exercises, and answers. Browser links identify`, "related lessons; C# streams, spans, typed classes, and runtime-variable dictionaries have no direct browser equivalent.", "", "Run one recipe from the repository root with the .NET 10 SDK:", "", "```sh", "dotnet run --project docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- decode-header", "```", "", "Use `--list` instead of `decode-header` to list names. Omit arguments to run all scenarios. Success ends with", "`PASS all " + scenarioCount + " scenarios`. Complete programs can also be copied into a console project with a matching package."];
for (const level of ["Beginner", "Intermediate", "Advanced"]) {
  catalog.push("", `## ${level}`, "", "| Task | Result checked | Browser lesson |", "| --- | --- | --- |");
  for (const [id, title, , , expected, , , , , lesson] of recipes.filter(item => item[2] === level)) {
    catalog.push(`| [${title}](../../examples/recipes/${id}.md) | ${expected} | ${lesson ? `[Open](https://vvollers.github.io/cstructsharp/explorer/#lesson=${lesson})` : "See browser API limits"} |`);
  }
}
catalog.push("", "## Larger walkthroughs", "", "- [Inspect and edit a binary file](../binary-file-walkthrough.md) validates a signature, version, and count before patching a record.", "- [Build a browser inspector](../browser/inspector.md) adds local file input, a field map, and a verified download.", "");
fs.writeFileSync(path.join(root, "docs/guides/recipes/index.md"), catalog.join("\n"));
console.log(`Exported ${recipes.length} complete, independently runnable programs and recipe pages.`);
