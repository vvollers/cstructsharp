/**
 * The inspector's schema catalog: one registration per file format. The eight formats with teaching samples each
 * have their own file (`bmp.ts`, `riff.ts`, `zip.ts`, `png.ts`, `jpeg.ts`, `pe.ts`, `ico.ts`, `tar.ts`); the
 * detection-only formats are grouped by kind (`images.ts`, `audio.ts`, `video.ts`, `archives.ts`, `documents.ts`,
 * `fonts.ts`, `programs.ts`, `data.ts`, `models.ts`). Each registration owns its file extensions, detection layout
 * and optional teaching samples. A sample can intentionally show a smaller structure than the detection layout, but
 * it is registered beside that layout rather than matched by an ID later. The sample definitions and bytes are
 * checked against tests/CStructSharpTests/Quality/WellKnownFormatTests.cs, and the engine corpus
 * (tests/CStructSharpTests/Engine/InspectorSchemaCatalog.cs) reads every file here except this one.
 */
import { formatLayout } from "@cstructsharp/app-shared/format-layout";

import type { FormatDefinition, InspectorExample } from "./types";
import { formats as bmp } from "./bmp";
import { formats as riff } from "./riff";
import { formats as zip } from "./zip";
import { formats as png } from "./png";
import { formats as jpeg } from "./jpeg";
import { formats as pe } from "./pe";
import { formats as ico } from "./ico";
import { formats as tar } from "./tar";
import { formats as images } from "./images";
import { formats as audio } from "./audio";
import { formats as video } from "./video";
import { formats as archives } from "./archives";
import { formats as documents } from "./documents";
import { formats as fonts } from "./fonts";
import { formats as programs } from "./programs";
import { formats as data } from "./data";
import { formats as models } from "./models";

export type { FormatParserOptions, InspectorExample } from "./types";

// The registration order sets the order of `sampleExamples`: the inspector opens with the first (BMP), and the
// benchmark fixture generator lists the samples in this order.
const formatDefinitions: FormatDefinition[] = [
  ...bmp,
  ...riff,
  ...zip,
  ...png,
  ...jpeg,
  ...pe,
  ...ico,
  ...tar,
  ...images,
  ...audio,
  ...video,
  ...archives,
  ...documents,
  ...fonts,
  ...programs,
  ...data,
  ...models,
];

// Build the lookups once. Reject duplicate registrations so later entries cannot silently replace one.
const formatsByExtension = new Map<
  string,
  { format: FormatDefinition; canonicalExtension: string }
>();
const samplesByExtension = new Map<string, InspectorExample>();
for (const format of formatDefinitions) {
  for (const [extension, canonicalExtension] of [
    ...format.extensions.map((extension) => [extension, extension] as const),
    ...Object.entries(format.aliases ?? {}),
  ]) {
    if (formatsByExtension.has(extension))
      throw new Error("Duplicate format extension: " + extension);
    if (!format.extensions.includes(canonicalExtension))
      throw new Error("Unknown alias target: " + canonicalExtension);
    formatsByExtension.set(extension, { format, canonicalExtension });
  }

  for (const sample of format.samples ?? []) {
    if (formatsByExtension.get(sample.extension)?.format !== format)
      throw new Error("Sample extension is not registered with its format: " + sample.extension);
    if (samplesByExtension.has(sample.extension))
      throw new Error("Duplicate sample extension: " + sample.extension);
    samplesByExtension.set(sample.extension, {
      ...sample,
      coverage: format.coverage ?? "structure",
    });
  }
}

export const detectorExtensions = formatDefinitions.flatMap((format) => format.extensions);
export const detectableFormatCount = detectorExtensions.length;
export const sampleExamples = [...samplesByExtension.values()];

/**
 * Selects a fixed layout from the extension; the file's bytes never change its declarations.
 * @param extension A registered extension or alias, such as `bmp` or `dll`.
 * @returns The detection schema of the extension's format, named after its canonical extension.
 * @throws {Error} When no format registers the extension.
 */
export function schemaForFile(extension: string): InspectorExample {
  const registration = formatsByExtension.get(extension);
  if (!registration) throw new Error("No schema registered for detected type " + extension);
  const { format, canonicalExtension: ext } = registration;
  const name = "file_" + ext.replace(/[^a-zA-Z0-9_]/g, "_");
  const definition = formatLayout(
    "// " +
      ext.toUpperCase() +
      " - " +
      format.family +
      "\n// " +
      format.scope +
      "\n" +
      (format.types ?? "") +
      "\nstruct " +
      name +
      " { " +
      format.fields +
      " };\nstruct root { " +
      name +
      " header; };",
  );
  return {
    id: "detected-" + ext,
    extension: ext,
    title: ext.toUpperCase() + " · " + format.family,
    description: format.family,
    coverage: format.coverage ?? "structure",
    definition,
    binaryHex: "",
    rootType: "root",
    parserOptions: {
      aligned: false,
      littleEndian: format.littleEndian ?? true,
      pointerSize: format.pointerSize ?? 4,
    },
    documentation: { summary: format.scope },
  };
}

// The sidebar prefers a sample for that extension; other entries ask the user to supply a file.
export const schemaCatalog: InspectorExample[] = [...formatsByExtension.keys()]
  .map(
    (extension) =>
      samplesByExtension.get(extension) ?? {
        ...schemaForFile(extension),
        id: "schema-" + extension,
        extension,
        schemaOnly: true,
      },
  )
  .sort((a, b) => a.extension!.localeCompare(b.extension!, undefined, { sensitivity: "base" }));

/**
 * The schema offered for a file whose type is not recognized: one prefix byte, for the user to extend.
 * @returns The editable raw schema.
 */
export function rawFileSchema(): InspectorExample {
  return {
    id: "detected-unknown",
    title: "Unknown format",
    description: "Raw prefix",
    coverage: "prefix",
    definition: formatLayout(
      "// Unrecognized file. Select an example or edit this schema.\nstruct root { uint8 prefix[1]; };",
    ),
    binaryHex: "",
    rootType: "root",
    parserOptions: { aligned: false, littleEndian: true, pointerSize: 4 },
    documentation: { summary: "Raw prefix; no file type recognized." },
  };
}
