import type { OperationRequest } from "./components/OperationPanel.vue";
import { hexToBytes } from "@cstructsharp/app-shared/hex";
import { parseWithDebug, serialize, updateStream } from "@cstructsharp/app-shared/wasm/adapter";
import type { InteropResult } from "@cstructsharp/app-shared/wasm/contract";

/** One operation's envelope and the bytes the result panel shows: the parsed input, or the written output. */
export interface OperationOutcome {
  result: InteropResult;
  bytes: Uint8Array;
}

/**
 * Runs the operation a panel request describes: a parse of the input hex, a serialize of the JSON value, or an
 * update of one path in the input hex. The explorer's result panel and its code generator both run requests here,
 * so they cannot interpret a request differently.
 * @param request The panel's operation, layout, input hex, JSON value, path and options.
 * @returns The envelope, and the input bytes for a parse or the written bytes for a successful serialize or update
 *   (empty when it failed).
 * @throws TypeError when the input hex is not whole bytes of hex digits.
 * @throws SyntaxError when the JSON value is not valid JSON.
 * @throws Error when the runtime has not loaded.
 */
export function runOperation(request: OperationRequest): OperationOutcome {
  if (request.operation === "parse") {
    const bytes = hexToBytes(request.binaryHex);
    return { result: parseWithDebug(request.definition, bytes, request.options), bytes };
  }

  const result =
    request.operation === "serialize"
      ? serialize(request.definition, JSON.parse(request.jsonValue), request.options)
      : updateStream(
          request.definition,
          hexToBytes(request.binaryHex),
          request.path,
          JSON.parse(request.jsonValue),
          request.options,
        );
  return {
    result,
    bytes: result.success && result.data instanceof Uint8Array ? result.data : new Uint8Array(),
  };
}
