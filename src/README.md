# Product source

`CStructSharp/` is the .NET 8/.NET 10 library. `CStructSharp.Wasm/` adapts it for JavaScript and contains authored loaders, types, and standalone tutorial files. Keep public assembly and package identities stable. Build managed code with `dotnet build CStructSharp.NonWeb.sln -c Release`; publish WASM with `node tools/packaging/publish-wasm.mjs`. Generated WASM goes to `artifacts/wasm/` and is independently staged by each app.
