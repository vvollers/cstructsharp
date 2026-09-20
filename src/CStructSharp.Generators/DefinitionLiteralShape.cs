namespace CStructSharp.Generators;

/// <summary>
///     How the layout literal is written, so a layout error at "line L, column C" can be placed inside it: a raw
///     string literal contributes its content start and the indentation it strips; anything else is located at the
///     whole argument.
/// </summary>
internal readonly record struct DefinitionLiteralShape(bool IsRawMultiLine, int ContentStartLine, int Indentation);
