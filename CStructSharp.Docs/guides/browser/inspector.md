---
title: Build a browser binary inspector
description: Open a local teaching file, inspect field ranges, update flags, and download verified bytes.
---

# Build a browser binary inspector

This advanced example builds on the [browser starter](index.md). It uses the same teaching format as the
[C# file walkthrough](../binary-file-walkthrough.md), with a fixed count of two records. The browser API does not
accept the C# runtime-variable dictionary, so this page explicitly checks that the stored count is 2.

## Run it

Serve the complete bundle with `node serve.mjs` and open `http://127.0.0.1:8080/starter/inspector.html`.
Select **Load the ten-byte example**, then select a field in the field map. You can also choose a local ten-byte
file with this format. The file is read in your browser.

The fixture is `43 53 01 02 01 00 10 02 00 20`. Its signature is CS, version is 1, count is 2, and the records
have ids 1 and 2. Set record 1's flags to 165 and select **Update flags**. The final byte becomes `A5`.
Select **Download updated file** to save the result. The other nine bytes must remain unchanged.

## Complete implementation

[!code-html[Inspector page](../../../CStructSharpWeb/wasm/starter/inspector.html)]

[!code-javascript[Inspector logic](../../../CStructSharpWeb/wasm/starter/inspector.js)]

The application checks file size before reading the file into memory, then validates signature, version, and count.
It passes explicit read limits. `DebugData` provides field positions with an exclusive end offset, so a range
`[7,9)` covers bytes 7 and 8. The field buttons expose positions and bytes as text, without relying on color.

An update result contains a `Uint8Array`. The example verifies the unchanged prefix and reads the new
bytes again before offering a download. It revokes old download URLs when data changes. An operation error is shown
with its stable code; loading failures are reported separately.

## Extend it carefully

Try changing the final byte to `FF`, or load a nine-byte file. Answer: `FF` is valid flags 255; a nine-byte file is
rejected as incomplete. A count other than 2 is also rejected even if the byte length happens to fit.

The current record uses small integers. If you add `uint64`, preserve decimal strings or BigInt values rather than
converting everything to JavaScript Number. See [value conversion](api.md). If you support larger files, define
format-specific count and traversal limits, and consider how processing affects the page's responsiveness.

Adding variable-length records requires a different format and application workflow. A fixed-field update cannot
insert space or relocate pointer targets automatically.
