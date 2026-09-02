# ADR-005: Public exception boundaries

- Status: Accepted
- Reviewed: 2026-07-26

## Decision

Expected layout-operation failures derive from `CStructException` and carry a stable `CStructErrorCode`.
Layout, path, read, read-limit, write, and write-limit failures use their corresponding domain exception. Invalid
arguments and unsupported stream capabilities remain ordinary argument exceptions; unexpected defects and
cancellation are not hidden.

Managed diagnostic detail must not be forwarded unchanged to an untrusted adapter. Browser error categories are a
separately versioned part of the browser interface.

## Related files

- `CStructSharp/CStructException.cs` and the six concrete domain exception types.
- `CStructSharp/CStructErrorCode.cs`.
- `CStructSharpTests/PublicExceptionBoundaryTests.cs`.
