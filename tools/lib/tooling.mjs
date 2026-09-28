/**
 * Shared helpers for the Node tools under tools/: fail-fast assertions, a logged `dotnet` runner, command and npm
 * runners, and a small `--name value` argument parser. Every tool exits 1 with its message on the first failed check.
 */
import { spawnSync } from "node:child_process";
import fs from "node:fs";
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
 * Parses `--name value` and `--flag` arguments (case-insensitive names; a single leading dash is also accepted).
 * `spec` maps each name to its kind: "string", "number", "flag", "list" (repeatable, comma-separated), or
 * "repeat" (repeatable, each value kept whole, for values that may contain commas).
 * @param {string[]} argv The arguments after the script name.
 * @param {Record<string, "string" | "number" | "flag" | "list" | "repeat">} spec The accepted options.
 * @param {{ defaults?: object, positionals?: boolean }} [options] Default values, and whether bare arguments are
 *   accepted; when they are, they are returned in order as `_`.
 * @returns {object} The option values by name, plus `_` with the positional arguments when accepted.
 * @throws {Error} For an unknown option, a missing value, a non-numeric number, or an unexpected bare argument.
 */
export function parseArguments(argv, spec, { defaults = {}, positionals = false } = {}) {
  const names = new Map(Object.keys(spec).map((name) => [name.toLowerCase(), name]));
  const values = { ...defaults };
  if (positionals) values._ = [];
  for (let index = 0; index < argv.length; index++) {
    const token = argv[index];
    const match = /^--?([A-Za-z][\w-]*)(?:=(.*))?$/.exec(token);
    if (!match && positionals) {
      values._.push(token);
      continue;
    }
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
    } else if (kind === "repeat") {
      values[name] = [...(values[name] ?? []), raw];
    } else {
      values[name] = raw;
    }
  }
  return values;
}

/** Quotes an argument for the logged command line when it contains whitespace or a double quote. */
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

/**
 * Runs any command and returns the `spawnSync` result. Without `allowFailure` it throws when the command cannot be
 * started or exits non-zero; with it, both cases are returned (a start failure as `result.error`, status null).
 */
export function runCommand(command, args, { cwd = repositoryRoot, env, allowFailure = false } = {}) {
  const result = spawnSync(command, args, { cwd, env, encoding: "utf8", maxBuffer: 256 * 1024 * 1024 });
  if (allowFailure) return result;
  if (result.error) throw result.error;
  if (result.status !== 0) {
    throw new Error(`${command} ${args.join(" ")} failed (${result.status}):\n${result.stdout ?? ""}\n${result.stderr ?? ""}`);
  }
  return result;
}

/**
 * The command and leading arguments that start npm without a shell: the npm CLI that launched this script through
 * `npm run`, otherwise the npm CLI installed next to this Node executable. Windows cannot start `npm.cmd` without a
 * shell, so the JavaScript entry point is run with Node directly; the plain `npm` command is the fallback.
 */
export function npmCommand() {
  const bundled = path.join(path.dirname(process.execPath), "node_modules", "npm", "bin", "npm-cli.js");
  const launcher = process.env.npm_execpath;
  const entry = launcher?.endsWith(".js") ? launcher : fs.existsSync(bundled) ? bundled : null;
  return entry ? { command: process.execPath, prefix: [entry] } : { command: "npm", prefix: [] };
}

/** Runs npm with `args` through {@link runCommand}, with the same options and failure behavior. */
export function runNpm(args, options = {}) {
  const { command, prefix } = npmCommand();
  return runCommand(command, [...prefix, ...args], options);
}
