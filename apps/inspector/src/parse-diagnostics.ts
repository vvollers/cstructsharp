import { INTEROP_CONTRACT_VERSION, type InteropResult } from "./wasm/cstruct-contract";

export function parseFailure(
  message: string,
  code = "invalid-input",
  offset: number | null = null,
): InteropResult {
  return {
    ContractVersion: INTEROP_CONTRACT_VERSION,
    Operation: "parse",
    Success: false,
    Data: null,
    DebugData: [],
    Error: { Code: code, Message: message, Offset: offset, Path: null },
  };
}
