# Packaged-consumer smoke fixture

This executable verifies the public API from the generated NuGet package; it intentionally has no project reference
to `src/CStructSharp` and is not part of `CStructSharp.sln`, because that solution must restore before a package exists.

Run it through the repository script after packing:

```sh
dotnet pack src/CStructSharp/CStructSharp.csproj -c Release -o artifacts/package
node tools/packaging/test-package-consumer.mjs --package-directory artifacts/package
```

The runner reads the package version from its `.nuspec`, restores into a fresh isolated package cache, verifies that
`net8.0` and `net10.0` select their matching `lib/<tfm>/CStructSharp.dll`, and confirms the restored package came from
the supplied directory. It then formats, builds, and executes the consumer on both frameworks. The executable covers
construction, parsing, debug data, address resolution, serialization, writing, in-place updates, and pointer
traversal through the installed public API. It also compiles read-only variable dictionaries against the packaged
parse and serialize overloads and verifies caller-owned variable state is unchanged. The union smoke covers lossless
raw `UnionValue` parse/serialize and explicit selected-member serialization, so a package missing the public union
model or returning an obsolete concrete root shape fails before release.
The enum smoke parses and reserializes `uint64.MaxValue` and verifies the installed `EnumValueResult` exposes its
exact `BigInteger` value, raw bits, width, signedness, canonical storage type, and symbolic name.
The generator smoke uses the package as a consumer would: `layouts/wire.cstruct` is picked up by the package's
`build/CStructSharp.targets`, `[CStructLayout(File = "layouts/wire.cstruct")]` on `WireLayout` generates the class,
its `Parse`/`Serialize`/`Sizes`/view are called, and `[CStructMapped]` on `GeneratedRoot` generates a mapper that
`ReadValue<T>` finds registered. The packaged analyzer runs too: the deliberately wrong `root.missing` path is
suppressed with `#pragma warning disable CSG200` around that negative check.
