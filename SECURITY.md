# Security policy

CStructSharp reads binary data that may come from an untrusted source, so a bug can be a security problem: for
example, an input that makes a read run far past its configured limits, follow a pointer outside the supplied data,
or crash the process instead of failing with a `CStructException`.

## Supported versions

Security fixes are made for the latest published 0.x release of the `CStructSharp` NuGet package and the
`cstructsharp` npm package. Older releases do not receive fixes; upgrade to the latest release to get them.

## Report a vulnerability privately

Do not open a public issue for a suspected vulnerability. Report it privately through GitHub instead:

1. Open the repository's [Security tab](https://github.com/vvollers/cstructsharp/security).
2. Choose **Report a vulnerability** to open a private security advisory that only the maintainer can see
   ([direct link](https://github.com/vvollers/cstructsharp/security/advisories/new)).
3. Describe the affected version, the layout and input bytes (or a small program) that show the problem, and what
   happens compared with what you expected.

A minimal reproduction helps most: the layout text, the exact bytes in hexadecimal, the API call, and its options.

The project has one maintainer, so there is no guaranteed response time. You will get an answer in the advisory
thread, and a confirmed problem is fixed in a new release whose CHANGELOG entry describes it once the fix is public.
