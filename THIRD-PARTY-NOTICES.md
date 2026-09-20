# Third-party notices

CStructSharp's NuGet package has no runtime dependencies. The source generator and analyzer it ships
(`analyzers/dotnet/cs/CStructSharp.Generators.dll`) are built with the following components, which are not
redistributed but whose generated or referenced code is part of that assembly:

## PolySharp

Copyright (c) 2022 Sergio Pedri. Licensed under the MIT License (https://github.com/Sergio0694/PolySharp/blob/main/LICENSE).

PolySharp generates the C# polyfill types (`IsExternalInit`, `RequiredMember`, nullable attributes, ...) that let
the generator target `netstandard2.0`; the generated polyfills are compiled into `CStructSharp.Generators.dll`.

## Microsoft.CodeAnalysis (Roslyn)

Copyright (c) .NET Foundation and Contributors. Licensed under the MIT License
(https://github.com/dotnet/roslyn/blob/main/License.txt).

The generator is compiled against Microsoft.CodeAnalysis 4.8 and runs inside the consuming compiler, which supplies
the Roslyn assemblies; none are packaged.
