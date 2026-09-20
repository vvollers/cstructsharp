namespace CStructSharp.PackageConsumer;

/// <summary>
///     The packaged generator over a layout file: the package's build targets add <c>layouts/wire.cstruct</c> as an
///     additional file and the analyzer assembly under <c>analyzers/dotnet/cs</c> generates the class from it.
/// </summary>
[CStructLayout(File = "layouts/wire.cstruct", PointerSize = 1)]
public static partial class WireLayout
{
}
