import fs from "node:fs";
import path from "node:path";
import { fileURLToPath } from "node:url";

const root = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");
const examples = path.join(root, "docs/examples");
const original = fs.readFileSync(path.join(examples, "Program.cs"), "utf8");
const more = fs.readFileSync(path.join(examples, "MoreExamples.cs"), "utf8");
function between(start, end) {
  const first = original.indexOf(start);
  return original.slice(first, original.indexOf(end, first)).trimEnd();
}
const support = [
  [/\bEqual(?:<[^>]*>)?\(/, between("    private static void Equal<T>", "    private static void True")],
  [/\bTrue\(/, between("    private static void True", "    private static void SequenceEqual")],
  [/\bSequenceEqual\(/, between("    private static void SequenceEqual", "    private static void Throws")],
  [/\bThrows</, between("    private static void Throws", "    public sealed class Header")],
  [/\bHeader[?> ]/, between("    public sealed class Header", "    #region api-guide-map-poco-type")],
  [/\bPoint[> ]/, between("    public sealed class Point", "    #endregion")],
];

// Authored teaching material; code and assertions are extracted from the executable runner.
const recipes = [
  ["decode-header", "Read a fixed header", "Beginner", "api-reference-cstruct", "kind 2, length 6; typed read succeeds and truncated read fails", "A two-byte kind and four-byte length occupy six packed bytes. The typed read maps them to an ordinary C# class.", "Change the first byte to 03 and update the expected kind.", "kind is 3; length stays 6.", "install-and-first-parse", "header"],
  ["header-round-trip", "Write and update a header", "Beginner", "recipe-header-round-trip", "03 00 06 00 00 00 after updating kind", "Serialize creates bytes. UpdateStream changes the existing kind without shifting the length field.", "Change the replacement kind to 4.", "The first byte becomes 04; the other five bytes stay the same.", "header-next-steps", "header-update"],
  ["byte-order", "Diagnose the wrong byte order", "Beginner", "recipe-byte-order", "little-endian kind 2; big-endian kind 512 and length 100663296", "Changing byte order changes the value, not the field width. A read may succeed even when the chosen byte order is wrong.", "Swap the two kind bytes and read with big-endian order.", "00 02 gives kind 2 in big-endian order.", "binary-layout-basics", "byte-order"],
  ["map-poco", "Read into a C# class", "Beginner", "api-guide-map-poco", "Point with X -2 and Y 5", "The complete program includes the Point class. Compatible properties can use C# capitalization when matching is unambiguous.", "Change FE FF to FF FF.", "X becomes -1, not 65535, because int16 is signed.", "typed-values", null],
  ["invalid-path", "Repair an invalid path", "Beginner", "recipe-invalid-path", "Header.kind fails; header.kind reads 2", "Paths are case-sensitive. Fix the selected name instead of changing valid bytes.", "Misspell kind as kinds.", "The selected read raises CStructPathException. Restore kind to fix it.", "reading-values", "invalid-path"],
  ["nested-array", "Read an item in a nested array", "Intermediate", "recipe-nested-array", "packet.items[1].id is 2; exact four-byte round trip", "Array indexes start at zero. Each item occupies two bytes, so the second starts at offset 2.", "Select items[0].id instead.", "The first id is 1.", "reading-values", null],
  ["aligned-header", "Explain padding in an aligned header", "Intermediate", "recipe-aligned-header", "length 6 at offset 4; eight-byte round trip", "Two padding bytes follow kind. A four-byte length begins at an offset divisible by four.", "Predict the position with aligned set to false.", "Packed length starts at 2, so the packed input must omit the two padding bytes.", "binary-layout-basics", null],
  ["bit-flags", "Read flags stored in one byte", "Intermediate", "recipe-bit-flags", "0B stores enabled 1, mode 5, reserved 0", "Portable allocates these bitfields from the low bits. The one-bit enabled field comes before the three-bit mode.", "Change 0B to 0A.", "enabled becomes 0; mode stays 5. Update the assertion before running the modified example.", "../language/bitfields", null],
  ["fixed-text", "Read and write fixed text", "Intermediate", "language-tutorial-fixed-text", "ABC followed by a zero character; XY writes 58 59 00 00", "Fixed-capacity text preserves the complete field when read. Writing shorter text fills the remaining space with zeros.", "Try writing ABCDE into four bytes.", "The write fails because the text exceeds the field capacity.", "strings-and-encodings", "text"],
  ["terminated-text", "Read zero-terminated text", "Intermediate", "recipe-terminated-text", "41 42 00 reads AB and writes back unchanged", "The empty brackets on char mean a terminated string, not a general array that consumes all remaining bytes.", "Remove the final zero byte.", "Reading fails because the terminator is missing. Restore it; do not assume the end of input is a terminator.", "strings-and-encodings", null],
  ["composite-record", "Combine text, an enum, and a union", "Intermediate", "language-tutorial-composite-record", "Text, AB with a zero character, and exact six-byte round trip", "The enum describes a kind; it does not automatically choose a union member. The application decides which interpretation makes sense.", "Inspect both union member values for 34 12.", "small sees 52; large sees 4660. They overlap the same storage.", "../language/tutorial/02-composites-and-layout", null],
  ["inspect-ranges", "Connect a field to its bytes", "Intermediate", "api-reference-debug-data", "uint16 occupies offsets 1 and 2; ResolveAddress returns 1", "Debug ranges use an exclusive end position: [1,3) means offsets 1 and 2. The debug result includes the root wrapper.", "Change the tag byte only.", "The value range remains [1,3). Changing a value does not change these fixed field positions.", "debug-data-and-addresses", "header"],
  ["patch-field", "Patch a nested field in a stream", "Intermediate", "api-reference-update-options", "EE EE 34 12 A5; invalid replacement preserves bytes and position", "The stream begins with a two-byte prefix. Set Position to the start of the selected structure before updating it.", "Try replacing flags with 999.", "It does not fit uint8. Validation fails before changing the stream.", "updating-existing-data", "nested-update"],
  ["preserve-union", "Preserve or select union storage", "Advanced", "api-reference-union", "raw 34 12 round trip; selected small writes A5 00", "Raw storage preserves bytes whose interpretation may be unknown. Explicit member selection is useful when creating a new value.", "Predict the result when selecting large with value 4660.", "The little-endian bytes are 34 12.", "unions", null],
  ["preserve-enum", "Preserve an unknown enum number", "Advanced", "api-reference-enum", "4294967295 with no known name, unsigned 32-bit backing", "An unknown member is still a valid stored integer. Keep its width and signedness instead of forcing it into a named application enum.", "Change the input to 01 00 00 00.", "The enum name is Known and its value is 1.", "enums", null],
  ["runtime-payload", "Supply a runtime array count", "Advanced", "language-tutorial-runtime-payload", "three payload values; second is 32; length lookup preserves position", "The application supplies COUNT through an integer dictionary. A prior field does not automatically become a variable. This dictionary is a C# API feature.", "Set COUNT to 4 without adding input bytes.", "Reading fails because the fourth payload byte is missing. Validate external counts before parsing.", "variables-options-and-limits", null],
  ["follow-pointer", "Follow an absolute stored pointer", "Advanced", "api-reference-pointer-read-options", "stored address 1 points to value 42", "The one-byte pointer describes a position in the supplied bytes. It is not an address in the computer's process memory.", "Replace address 1 with 0.", "Zero is null and is not followed, even when a nonzero origin is configured.", "pointers", null],
  ["relative-pointer", "Follow a pointer relative to an origin", "Advanced", "recipe-relative-pointer", "stored address 1 plus origin 1 reaches offset 2 and value 42", "Pointer width, field position, stored address, and effective target are different concepts. The options bound this example to one pointer level and one target byte.", "Set origin to 0 and predict the target.", "The target becomes offset 1. It is a valid position but does not contain the intended value.", "pointers", null],
  ["bounded-read", "Bound the work of a read", "Advanced", "recipe-bounded-read", "three-byte budget fails; six-byte budget reads length 6", "Limits count work done by an operation. A complete input can fail because its allowed read budget is too small.", "Use a limit of 5.", "The six-byte read still fails. Set limits from the format, not by repeatedly raising them until arbitrary data succeeds.", "variables-options-and-limits", "limits"],
  ["positioned-stream", "Read inside a larger stream", "Advanced", "recipe-positioned-stream", "length address 4; inspection preserves Position 2; length reads 6", "The two-byte prefix belongs to surrounding data. Selected addresses are stream coordinates, so length starts at 2 + 2.", "Start the stream at Position 0.", "The prefix becomes part of the header input and values change. Restore Position 2.", "reading-values", null],
  ["round-trip", "Reuse a layout with different output storage", "Advanced", "api-reference-write-options", "34 12 A5 from array, span, and buffer writer; unused capacity preserved", "An owned array is simplest. A span or buffer writer lets the caller provide storage but has different partial-write behavior.", "Predict how many bytes are initialized in the eight-byte span.", "Only three. Use the returned count rather than treating all capacity as output.", "spans-and-memory", null],
  ["edit-file", "Inspect and edit a complete binary file", "Advanced", "recipe-edit-file", "435301020100100200A5; truncated and excessive-count fixtures rejected", "Validate signature, version, count, and file length in application code. Then supply COUNT and update one fixed field. The example creates and removes its own temporary fixture file.", "Change the record count to 255 without changing file length.", "Application validation rejects it before traversal. Patching cannot insert more records or move following data.", "binary-file-walkthrough", null],
];

const browserLessons = {
  "nested-array": "arrays", "aligned-header": "alignment", "bit-flags": "bitfields",
  "terminated-text": "terminated-text", "preserve-enum": "enum", "preserve-union": "union",
  "follow-pointer": "pointer",
};
for (const recipe of recipes) recipe[9] ??= browserLessons[recipe[0]] ?? null;

const out = path.join(examples, "recipes");
fs.mkdirSync(out, { recursive: true });
const toc = ["items:"];
for (const [id, title, level, region, expected, explanation, exercise, answer, guide, lesson] of recipes) {
  const text = original.includes(`#region ${region}\n`) || original.includes(`#region ${region}\r\n`) ? original : more;
  const start = text.indexOf(`#region ${region}`);
  if (start < 0) throw new Error(`Missing example region ${region}`);
  const end = text.indexOf("#endregion", start);
  const method = text.slice(text.indexOf("\n", start) + 1, end).trimEnd();
  const methodName = method.match(/private static void (\w+)\(/)?.[1];
  if (!methodName || !original.includes(`("${id}", ${methodName})`)) throw new Error(`Scenario ${id} is not registered`);
  const helpers = support.filter(([pattern]) => pattern.test(method)).map(([, code]) => code).join("\n\n") + "\n";
  const code = `// Generated from executable documentation examples. Edit the source region, then regenerate.\nusing System;\nusing System.IO;\nusing System.Linq;\nusing System.Buffers;\nusing System.Collections.Generic;\nusing System.Dynamic;\nusing System.Numerics;\nusing CStructSharp;\n\ninternal static class Program\n{\n    public static void Main()\n    {\n        ${methodName}();\n        Console.WriteLine("PASS ${id}");\n    }\n\n${method}\n\n${helpers}}\n`;
  fs.writeFileSync(path.join(out, `${id}.cs`), code);
  fs.writeFileSync(path.join(out, `${id}.md`), `---\ntitle: ${title}\ndescription: Run the complete ${id} example and check its values and bytes.\n---\n\n# ${title}\n\n**${level} · C#**. ${explanation}\n\n## Run this example\n\nPrerequisites: the repository's .NET 10 SDK and a checkout of this source. Run from the repository root:\n\n\`\`\`sh\ndotnet run --project docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- ${id}\n\`\`\`\n\nThe runner checks ${expected}. Success includes \`PASS ${id}\`.\n${lesson ? `\n[Try the related browser lesson](https://vvollers.github.io/cstructsharp/explorer/#lesson=${lesson}).\n` : "\nThis example uses the C# API. Browser capabilities and result shapes are described in the [browser guide](../../guides/browser/api.md).\n"}\n## Complete program\n\nThe layout, options, input bytes, helper methods, and required types are all included. To adapt it outside the\nrepository, create a .NET 10 console project, add CStructSharp, and replace Program.cs with this complete file.\nThese examples follow the source version; use a matching package when testing a release.\n\n[Download the complete C# source](${id}.cs).\n\n[!code-csharp[Complete ${id} program](${id}.cs)]\n\n## Try it and diagnose mistakes\n\n${exercise}\n\nAnswer: ${answer} The program contains assertions for its original inputs. When changing an input intentionally,\nupdate the expected assertion too; an unchanged assertion is not evidence that the new value is wrong.\n\nContinue with [the related guide](../../guides/${guide}.md) or [choose another recipe](../../guides/recipes/index.md).\n`);
  toc.push(`- name: ${title}`, `  href: ${id}.md`);
}
fs.writeFileSync(path.join(out, "toc.yml"), `${toc.join("\n")}\n`);
const catalog = ["---", "title: Tested recipes", "description: Choose a complete executable recipe by task, difficulty, and platform.", "---", "", "# Tested recipes", "", "Start with the [first C# program](../install-and-first-parse.md) or the [Node.js and browser quick start](../browser/index.md).", "These 22 recipes include complete programs, exact byte/value checks, exercises, and answers. Browser links identify", "related lessons; C# streams, spans, typed classes, and runtime-variable dictionaries have no direct browser equivalent.", "", "Run one recipe from the repository root with the .NET 10 SDK:", "", "```sh", "dotnet run --project docs/examples/CStructSharp.Docs.Examples.csproj -c Release -- decode-header", "```", "", "Use `--list` instead of `decode-header` to list names. Omit arguments to run all 22 scenarios. Success ends with", "`PASS all 22 scenarios`. Complete programs can also be copied into a console project with a matching package."];
for (const level of ["Beginner", "Intermediate", "Advanced"]) {
  catalog.push("", `## ${level}`, "", "| Task | Result checked | Browser lesson |", "| --- | --- | --- |");
  for (const [id, title, , , expected, , , , , lesson] of recipes.filter(item => item[2] === level)) {
    catalog.push(`| [${title}](../../examples/recipes/${id}.md) | ${expected} | ${lesson ? `[Open](https://vvollers.github.io/cstructsharp/explorer/#lesson=${lesson})` : "See browser API limits"} |`);
  }
}
catalog.push("", "## Larger walkthroughs", "", "- [Inspect and edit a binary file](../binary-file-walkthrough.md) validates a signature, version, and count before patching a record.", "- [Build a browser inspector](../browser/inspector.md) adds local file input, a field map, and a verified download.", "");
fs.writeFileSync(path.join(root, "docs/guides/recipes/index.md"), catalog.join("\n"));
console.log(`Exported ${recipes.length} complete, independently runnable programs and recipe pages.`);
