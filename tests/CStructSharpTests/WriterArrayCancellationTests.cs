namespace CStructSharp.Tests;

/// <summary>Checks cancellation between array materialization and composite-target element conversion.</summary>
[TestClass]
public class WriterArrayCancellationTests
{
    /// <summary>Cancelling after input enumeration takes precedence over an invalid composite-target pointer value.</summary>
    [TestMethod]
    public void CompositePointerArray_CancelsBeforeConvertingAnElement()
    {
        var layout = new CStruct("struct child { uint8 value; }; struct root { child *values[1]; };");
        using var cancellation = new CancellationTokenSource();
        var data = new Dictionary<string, object?> { ["values"] = CancelAfterValue(cancellation), };
        using var destination = new MemoryStream();

        // State construction starts uncancelled; enumeration requests cancellation before element conversion.
        OperationCanceledException failure = Assert.Throws<OperationCanceledException>(() =>
            layout.Write(destination, "root", data, options: new WriteOptions { CancellationToken = cancellation.Token, }));
        Assert.AreEqual(cancellation.Token, failure.CancellationToken);
        Assert.AreEqual(0L, destination.Length);
    }

    /// <summary>Yields one invalid pointer address, then requests cancellation when materialization completes.</summary>
    /// <param name="cancellation">The operation's cancellation source.</param>
    /// <returns>One negative physical address.</returns>
    private static IEnumerable<object> CancelAfterValue(CancellationTokenSource cancellation)
    {
        yield return -1L;
        cancellation.Cancel();
    }
}
