#!/usr/bin/env node
/**
 * Creates serial comparison cases for immutable discovery probes. No build or measurement is started.
 * Usage: node make-cases.mjs library|client|text|transport|byte-preparation REFERENCE_PATTERN CANDIDATE_PATTERN OUTPUT [AUTHORIZED_IMAGE]
 * Patterns contain {runtime}, replaced with jit and aot; image cases are omitted unless explicitly supplied.
 */
import fs from "node:fs";
import path from "node:path";

const [kind, referencePattern, candidatePattern, output, image] = process.argv.slice(2);
if (!["library", "client", "text", "transport", "byte-preparation"].includes(kind) || !output) {
  throw new Error("Supply kind, immutable reference/candidate directory patterns and an output JSON path.");
}
const cases = [];

/** Adds one complete-operation pair, preserving explicitly different control arguments. */
function add(name, reference, referenceArguments, candidate, candidateArguments) {
  cases.push({ name, reference: { executable: reference, arguments: referenceArguments }, candidate: { executable: candidate, arguments: candidateArguments } });
}

for (const runtime of ["jit", "aot"]) {
  const file = ["client", "transport"].includes(kind) ? "DiscoveryProbe.exe" : "DiscoveryLibraryProbe.exe";
  const reference = path.resolve(referencePattern.replace("{runtime}", runtime), file);
  const candidate = path.resolve(candidatePattern.replace("{runtime}", runtime), file);
  if (kind === "byte-preparation") {
    add(`byte-preparation-${runtime}`, candidate, ["first-bytes", "small", "1"], candidate, ["bind-bytes", "small", "100000"]);
  } else if (kind === "text") {
    add(`text-control-${runtime}`, candidate, ["text-only", "small", "100000"], candidate, ["bytes", "small", "100000"]);
  } else if (kind === "transport") {
    for (const mode of ["credential-transport", "credential-transport-sparse"]) add(`${runtime}-${mode}`, reference, [mode, "10"], candidate, [mode, "10"]);
  } else if (kind === "library") {
    for (const scenario of ["success", "reject", "short"]) {
      add(`library-runtime-${runtime}-${scenario}`, reference, ["runtime", scenario, "200000"], candidate, ["runtime", scenario, "200000"]);
      add(`library-prepared-${runtime}-${scenario}`, reference, ["runtime", scenario, "200000"], candidate, ["prepared", scenario, "200000"]);
      add(`library-generated-${runtime}-${scenario}`, reference, ["generated", scenario, "2000000"], candidate, ["generated", scenario, "2000000"]);
      add(`library-probe-${runtime}-${scenario}`, candidate, ["manual", scenario, "2000000"], candidate, ["probe", scenario, "2000000"]);
    }
    for (const mode of ["cold", "first-runtime"]) add(`library-${mode}-${runtime}`, reference, [mode, "success", "1"], candidate, [mode, "success", "1"]);
    add(`library-preparation-${runtime}`, candidate, ["first-runtime", "success", "1"], candidate, ["preparation", "success", "1"]);
    add(`library-first-prepared-${runtime}`, candidate, ["first-runtime", "success", "1"], candidate, ["first-prepared", "success", "1"]);
    add(`library-lookup-${runtime}`, candidate, ["plan", "success", "200000"], candidate, ["lookup", "success", "200000"]);
    for (const size of ["small", "large"]) {
      add(`library-fields-${runtime}-${size}`, reference, ["fields", size, "50000"], candidate, ["fields", size, "50000"]);
      for (const mode of ["elementwise", "coalesced"]) add(`library-${mode}-${runtime}-${size}`, candidate, ["fields", size, "50000"], candidate, [mode, size, "50000"]);
    }
    add(`library-bytes-${runtime}`, candidate, ["strings", "small", "100000"], candidate, ["bytes", "small", "100000"]);
  } else {
    for (const mode of ["history", "dense", "patterns", "markers", "graph-unique", "graph-shared", "graph-unique-valid", "graph-shared-valid"]) {
      const iterations = mode.startsWith("graph") ? "20" : "100";
      add(`client-${runtime}-${mode}`, reference, [mode, iterations], candidate, [mode, iterations]);
    }
    for (const corpus of ["history", "dense"]) {
      for (const method of ["gated", "reverse"]) add(`client-${runtime}-${corpus}-${method}`, reference, [corpus, "100"], candidate, [`${corpus}-${method}`, "100"]);
    }
    for (const corpus of ["multi", "common"]) add(`client-${runtime}-${corpus}`, candidate, [`${corpus}-repeated`, "10"], candidate, [`${corpus}-shared`, "10"]);
    if (image) {
      add(`client-${runtime}-bootstrap`, reference, ["bootstrap", image], candidate, ["bootstrap", image]);
      add(`client-${runtime}-cancel`, reference, ["cancel-bootstrap", image], candidate, ["cancel-bootstrap", image]);
    }
  }
}
fs.writeFileSync(output, `${JSON.stringify(cases, null, 2)}\n`);
