import fs from "node:fs";
import os from "node:os";
import path from "node:path";
import { fileURLToPath } from "node:url";
import { spawnSync } from "node:child_process";

const webRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "..");
const bundle = path.resolve(process.argv[2] ?? path.join(webRoot, "artifacts/wasm-package"));
const work = fs.mkdtempSync(path.join(os.tmpdir(), "cstructsharp-types-"));
try {
  // Use only files from the packaged distribution, never the explorer's internal types.
  for (const name of ["cstructsharp-wasm.js", "cstructsharp-api.js", "cstructsharp-wasm.d.ts"])
    fs.copyFileSync(path.join(bundle, name), path.join(work, name));
  fs.writeFileSync(path.join(work, "package.json"), '{"type":"module"}');
  fs.copyFileSync(
    path.join(webRoot, "tests/types/public-consumer.ts"),
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
  const result = spawnSync(
    process.execPath,
    [path.join(webRoot, "node_modules/typescript/bin/tsc"), "-p", work],
    { stdio: "inherit" },
  );
  if (result.status !== 0) throw new Error("Packaged TypeScript consumer failed.");
  console.log("PASS strict TypeScript consumer against packaged public API");
} finally {
  fs.rmSync(work, { recursive: true, force: true });
}
