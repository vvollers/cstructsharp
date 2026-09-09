namespace CStructSharpWeb.Wasm;

/// <summary>Provides the empty .NET entry point required to host the browser exports.</summary>
public static class Program
{
    /// <summary>Starts the WebAssembly host; JavaScript calls <see cref="CStructExports"/> after startup.</summary>
    public static void Main()
    {
    }
}
