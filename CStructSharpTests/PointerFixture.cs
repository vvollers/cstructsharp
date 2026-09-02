namespace CStructSharp.Tests;

/// <summary>Owns a compiled one-pointer layout and its initialized stream.</summary>
internal sealed class PointerFixture : IDisposable
{
    /// <summary>Initializes a new instance of the <see cref="PointerFixture"/> class.</summary>
    /// <param name="layout">The compiled pointer layout.</param>
    /// <param name="stream">The initialized pointer and target storage.</param>
    /// <param name="targetAddress">The absolute target address.</param>
    public PointerFixture(CStruct layout, MemoryStream stream, int targetAddress)
    {
        this.Layout = layout;
        this.Stream = stream;
        this.TargetAddress = targetAddress;
    }

    /// <summary>Gets the compiled pointer layout.</summary>
    public CStruct Layout { get; }

    /// <summary>Gets the initialized seekable stream.</summary>
    public MemoryStream Stream { get; }

    /// <summary>Gets the absolute target address encoded by the fixture.</summary>
    public int TargetAddress { get; }

    /// <summary>Disposes the fixture stream.</summary>
    public void Dispose()
    {
        this.Stream.Dispose();
    }
}
