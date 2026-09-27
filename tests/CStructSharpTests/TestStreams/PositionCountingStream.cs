namespace CStructSharp.Tests;

/// <summary>A memory stream that counts how often its position is read, with otherwise ordinary behavior.</summary>
internal sealed class PositionCountingStream : MemoryStream
{
    /// <summary>Gets the number of position reads.</summary>
    public int PositionReads { get; private set; }

    /// <summary>Gets the position, counting the read, or sets it without counting.</summary>
    public override long Position
    {
        get
        {
            this.PositionReads++;
            return base.Position;
        }

        set => base.Position = value;
    }
}
