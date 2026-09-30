/**
 * The shapes of the inspector's schema catalog: the example the editor shows, and the registration each format file
 * (`images.ts`, `audio.ts`, ...) lists. `index.ts` turns the registrations into the catalog.
 */

export interface FormatParserOptions {
  aligned: boolean;
  littleEndian: boolean;
  pointerSize: number;
  addressingMode?: "Absolute" | "Relative";
}

/** One selectable editor example. Sample examples also carry bytes. */
export interface InspectorExample {
  id: string;
  title: string;
  description: string;
  definition: string;
  binaryHex: string;
  rootType: string;
  parserOptions: FormatParserOptions;
  documentation: { summary: string };
  coverage: "structure" | "prefix";
  extension?: string;
  schemaOnly?: boolean;
}

/** A teaching sample registered with its format; the index adds the format's coverage. */
export type SampleExample = Omit<InspectorExample, "coverage" | "schemaOnly"> & {
  extension: string;
};

/**
 * One format's registration: its file extensions (and aliases to them), the detection layout (`types` declarations
 * plus the `fields` of the file header struct) with its byte order and pointer size, and optional teaching samples.
 */
export interface FormatDefinition {
  extensions: string[];
  aliases?: Record<string, string>;
  family: string;
  scope: string;
  fields: string;
  types?: string;
  littleEndian?: boolean;
  pointerSize?: number;
  coverage?: "structure" | "prefix";
  samples?: SampleExample[];
}

/** The parser options most teaching samples use: packed, little-endian, 8-byte pointers. */
export const sampleParserOptions: FormatParserOptions = {
  aligned: false,
  littleEndian: true,
  pointerSize: 8,
};
