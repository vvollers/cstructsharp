namespace CStructSharp.Generators;

/// <summary>
///     How the layout literal is written, so a layout error at "line L, column C" can be placed inside it: a raw
///     string literal contributes its content start and the indentation it strips; anything else is located at the
///     whole argument.
/// </summary>
/// <param name="IsRawMultiLine">Whether the definition is a multi-line raw string literal; the other values are 0 otherwise.</param>
/// <param name="ContentStartLine">The zero-based source line of the literal's first content line, the one after the opening quotes.</param>
/// <param name="Indentation">The number of leading whitespace characters the compiler strips from every content line.</param>
internal readonly record struct DefinitionLiteralShape(bool IsRawMultiLine, int ContentStartLine, int Indentation);
