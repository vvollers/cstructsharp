#!/usr/bin/env node
/**
 * Copies the package's runtime into a new directory for static hosting; the directory must not exist yet.
 *
 *   cstructsharp-copy --out <new-runtime-directory>
 */
import { copyRuntime } from "./assets.js";

const args = process.argv.slice(2);
if (args.length !== 2 || args[0] !== "--out" || !args[1]) {
  console.error("Usage: cstructsharp-copy --out <new-runtime-directory>");
  process.exitCode = 1;
} else {
  try {
    console.log(`Copied CStructSharp runtime to ${copyRuntime(args[1])}`);
  } catch (error) {
    console.error(error.message);
    process.exitCode = 1;
  }
}
