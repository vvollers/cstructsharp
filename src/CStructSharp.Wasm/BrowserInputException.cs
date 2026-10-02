namespace CStructSharpWeb.Wasm;

using System;

/// <summary>A controlled bridge diagnostic containing only fixed text and numeric limits.</summary>
/// <param name="message">The diagnostic, which the envelope reports verbatim in the <c>invalid-input</c> category.</param>
internal sealed class BrowserInputException(string message) : Exception(message);
