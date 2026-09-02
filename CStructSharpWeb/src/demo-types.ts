export interface ParserOptions {
  aligned: boolean;
  littleEndian: boolean;
  pointerSize: number;
}

export interface TestEntry {
  id: string;
  className: string;
  methodName: string;
  filePath: string;
  line: number;
  runnable: boolean;
  reason?: string;
  definition?: string;
  binaryHex?: string;
  rootType?: string | null;
  parserOptions?: ParserOptions;
  documentation?: {
    summary: string;
    usage: string;
  };
}

export interface TestManifest {
  sourceRoot: string;
  totalTests: number;
  runnableTests: number;
  tests: TestEntry[];
}

export function isRunnable(test: TestEntry | null | undefined): test is TestEntry & {
  definition: string;
  binaryHex: string;
} {
  return (
    test?.runnable === true &&
    typeof test.definition === "string" &&
    typeof test.binaryHex === "string"
  );
}
