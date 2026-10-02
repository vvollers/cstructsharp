namespace CStructSharp.Tests;

using System.Text.Json;
using CStructSharp.Diagnostics;
using CStructSharpWeb.Wasm;

/// <summary>
///     The browser bridge's failure categories: every exception an export catches maps to one stable envelope error
///     <c>code</c>. The mapping is compiled into this project because the WebAssembly project has no managed test host.
/// </summary>
[TestClass]
public class BrowserErrorCategoryTests
{
    /// <summary>
    ///     Running out of memory is <c>resource-exhausted</c>, with advice to use a fresh runtime or a smaller result,
    ///     instead of the uninformative <c>operation-failed</c> fallback.
    /// </summary>
    [TestMethod]
    public void OutOfMemory_IsResourceExhausted()
    {
        foreach (Exception exception in new Exception[] { new OutOfMemoryException(), new InsufficientMemoryException(), })
        {
            (string code, string message) = InteropErrorCategories.GetBrowserError(exception);

            Assert.AreEqual("resource-exhausted", code, exception.GetType().Name);
            Assert.AreEqual("WebAssembly memory is exhausted. Run very large debug parses in a fresh worker or process, or narrow the root.", message);
        }
    }

    /// <summary>Caller input, malformed JSON, and library failures keep their categories; anything else is the fallback.</summary>
    [TestMethod]
    public void OtherFailures_KeepTheirCategories()
    {
        Assert.AreEqual(("invalid-input", "Too long."), InteropErrorCategories.GetBrowserError(new BrowserInputException("Too long.")));
        Assert.AreEqual("invalid-json", InteropErrorCategories.GetBrowserError(new JsonException()).Code);
        Assert.AreEqual("read-budget", InteropErrorCategories.GetBrowserError(new CStructReadLimitException("Limit.")).Code);
        Assert.AreEqual(("operation-failed", "The operation failed unexpectedly."), InteropErrorCategories.GetBrowserError(new InvalidOperationException("internal detail")));
    }
}
