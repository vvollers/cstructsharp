# Packaged-consumer smoke fixture

This executable verifies the public API from the generated NuGet package; it intentionally has no project reference
to `CStructSharp` and is not part of `CStructSharp.sln`, because that solution must restore before a package exists.

Run it through the repository script after packing:

```powershell
dotnet pack .\CStructSharp\CStructSharp.csproj -c Release -o .\artifacts\package
.\tools\Test-PackageConsumer.ps1 -PackageDirectory .\artifacts\package
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
