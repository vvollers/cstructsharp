/**
 * Type-checks the strict TypeScript consumer (packages/cstructsharp/tests/types/public-consumer.ts) against the
 * declarations and entry points of a packaged standalone bundle, never the repository sources.
 *
 *   node tools/packaging/test-public-types.mjs [bundle-directory]   (default: artifacts/wasm-package)
 */
import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { parseArguments, repositoryRoot, runCommand } from "../lib/tooling.mjs";

const {
  _: [bundleArgument],
} = parseArguments(process.argv.slice(2), {}, { positionals: true });
const bundle = path.resolve(bundleArgument ?? path.join(repositoryRoot, "artifacts/wasm-package"));
const work = fs.mkdtempSync(path.join(os.tmpdir(), "cstructsharp-types-"));
try {
  // Use only files from the packaged distribution, never the repository's sources.
  for (const name of ["cstructsharp-wasm.js", "cstructsharp-api.js", "cstructsharp-wasm.d.ts"])
    fs.copyFileSync(path.join(bundle, name), path.join(work, name));
  fs.writeFileSync(path.join(work, "package.json"), '{"type":"module"}');
  fs.copyFileSync(
    path.join(repositoryRoot, "packages/cstructsharp/tests/types/public-consumer.ts"),
    path.join(work, "consumer.ts"),
  );
  fs.writeFileSync(
    path.join(work, "tsconfig.json"),
    JSON.stringify({
      compilerOptions: {
        strict: true,
        noEmit: true,
        target: "ES2022",
        module: "NodeNext",
        types: [],
        skipLibCheck: false,
      },
      files: ["consumer.ts"],
    }),
  );
  const result = runCommand(
    process.execPath,
    [path.join(repositoryRoot, "node_modules/typescript/bin/tsc"), "-p", work],
    { allowFailure: true },
  );
  // The compiler's diagnostics explain a failure, so they are shown before the verdict.
  process.stdout.write(result.stdout ?? "");
  process.stderr.write(result.stderr ?? "");
  if (result.status !== 0) throw new Error("Packaged TypeScript consumer failed.");
  console.log("PASS strict TypeScript consumer against packaged public API");
} finally {
  fs.rmSync(work, { recursive: true, force: true });
}
