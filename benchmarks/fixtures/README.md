# Benchmark fixtures

Shared inputs for the .NET, Node, and browser benchmark harnesses. Each
`cases/<id>.json` carries the layout source, constructor options, root type, optional read options, the input bytes
(inline hex ≤ 16 KiB, a sidecar under `data/`, or a seeded `xorshift` generator that C# and JS reproduce
byte-for-byte), and the expected canonical JSON result produced by the managed library (inline ≤ 64 KiB, otherwise
`expectedSha256` + `expectedJsonLength`). Malformed inputs record `expectedError` instead.

```sh
node benchmarks/fixtures/generate-fixtures.mjs                                   # deterministic; idempotent
dotnet run --project benchmarks/CStructSharp.FixtureTool -c Release -f net10.0 -- fill      # record expectations from C#
dotnet run --project benchmarks/CStructSharp.FixtureTool -c Release -f net10.0 -- verify    # re-check (exit 1 on drift)
```

Real-format fixtures are imported from `apps/inspector/src/schema-catalog.ts`, which `WellKnownFormatTests.cs` verifies;
conditional fixtures reuse `benchmarks/fixtures/conditional-cases.json` definitions. The canonical JSON shape mirrors
the WASM bridge (`CStructJsonConversion.cs`) so the JS harness can compare `result.data` directly.
Expectations are inputs to the correctness gate: never regenerate them to hide a behavior change.
`verify` checks the expected exception type, the SHA-256 and length of the canonical JSON, and the inline value when
one is stored. `tests/CStructSharpTests/Quality/BenchmarkFixtureExpectationTests.cs` applies the same check to every
case (through the tool's `FixtureVerification`) in the ordinary managed test run.
