/**
 * Shared helpers for the Node tools under tools/: fail-fast assertions, a logged `dotnet` runner, and a small
 * `--name value` argument parser. Every tool exits 1 with its message on the first failed check.
 */
import { spawnSync } from "node:child_process";
import path from "node:path";
import { fileURLToPath } from "node:url";

export const repositoryRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), "../..");

/** Throws `message` when `condition` is false. */
export function assertCondition(condition, message) {
  if (!condition) throw new Error(message);
}

/** Runs the tool's main function and turns a thrown error into a one-line failure and exit code 1. */
export async function main(run) {
  try {
    await run();
  } catch (error) {
    console.error(error instanceof Error ? error.message : String(error));
    process.exitCode = 1;
  }
}

/**
 * Parses `--name value` and `--flag` arguments (case-insensitive names, `-name` accepted for the PowerShell habit).
 * `spec` maps each name to its kind: "string", "number", "flag", or "list" (repeatable / comma-separated).
 */
export function parseArguments(argv, spec, { defaults = {} } = {}) {
  const names = new Map(Object.keys(spec).map((name) => [name.toLowerCase(), name]));
  const values = { ...defaults };
  for (let index = 0; index < argv.length; index++) {
    const token = argv[index];
    const match = /^--?([A-Za-z][\w-]*)(?:=(.*))?$/.exec(token);
    if (!match) throw new Error(`Unexpected argument: ${token}`);
    const name = names.get(match[1].toLowerCase());
    if (!name) throw new Error(`Unknown option: --${match[1]}`);
    const kind = spec[name];
    if (kind === "flag") {
      values[name] = match[2] === undefined ? true : !/^(false|0|no)$/i.test(match[2]);
      continue;
    }
    const raw = match[2] ?? argv[++index];
    if (raw === undefined) throw new Error(`Option --${name} needs a value.`);
    if (kind === "number") {
      const number = Number(raw);
      if (!Number.isFinite(number)) throw new Error(`Option --${name} must be a number; received '${raw}'.`);
      values[name] = number;
    } else if (kind === "list") {
      values[name] = [...(values[name] ?? []), ...raw.split(",").map((item) => item.trim()).filter(Boolean)];
    } else {
      values[name] = raw;
    }
  }
  return values;
}

function displayArgument(argument) {
  return /[\s"]/.test(argument) ? `'${argument.replaceAll("'", "''")}'` : argument;
}

/**
 * Runs `dotnet` with the given arguments, logging the command, its output, and its elapsed time, and throws when
 * it exits non-zero. Returns the elapsed seconds.
 */
export function runDotnet(args, { label = args.join(" "), cwd = repositoryRoot, env } = {}) {
  console.log(`==> dotnet ${args.map(displayArgument).join(" ")}`);
  const started = process.hrtime.bigint();
  const result = spawnSync("dotnet", args, { cwd, env, encoding: "utf8", maxBuffer: 256 * 1024 * 1024 });
  const seconds = Number(process.hrtime.bigint() - started) / 1e9;
  if (result.error) throw result.error;
  if (result.stdout) process.stdout.write(result.stdout.endsWith("\n") ? result.stdout : `${result.stdout}\n`);
  if (result.stderr) process.stderr.write(result.stderr.endsWith("\n") ? result.stderr : `${result.stderr}\n`);
  console.log(`<== ${label}: exit ${result.status}, ${seconds.toFixed(3)} s`);
  if (result.status !== 0) throw new Error(`${label} failed with exit code ${result.status}.`);
  return seconds;
}

/** Runs any command, returning its stdout; throws with the output when it exits non-zero. */
export function runCommand(command, args, { cwd = repositoryRoot, env, allowFailure = false } = {}) {
  const result = spawnSync(command, args, { cwd, env, encoding: "utf8", maxBuffer: 256 * 1024 * 1024 });
  if (result.error) throw result.error;
  if (result.status !== 0 && !allowFailure) {
    throw new Error(`${command} ${args.join(" ")} failed (${result.status}):\n${result.stdout ?? ""}\n${result.stderr ?? ""}`);
  }
  return result;
}
