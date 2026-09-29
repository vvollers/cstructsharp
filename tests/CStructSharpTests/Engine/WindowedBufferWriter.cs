namespace CStructSharp.Tests;

using System.Buffers;

/// <summary>
///     A buffer writer that hands out small windows: each <see cref="GetSpan"/> or <see cref="GetMemory"/> returns a
///     fresh window of exactly <c>max(window, sizeHint)</c> bytes, so a write must split its output across many windows
///     and never sees more room than it asked for. Advanced bytes are appended to <see cref="Written"/>.
/// </summary>
internal sealed class WindowedBufferWriter : IBufferWriter<byte>
{
    /// <summary>The smallest window size in bytes.</summary>
    private readonly int window;

    /// <summary>The bytes advanced so far.</summary>
    private readonly List<byte> written = [];

    /// <summary>The window handed out last, or empty when none is open.</summary>
    private byte[] active = [];

    /// <summary>Creates a writer.</summary>
    /// <param name="window">The window size in bytes for requests that hint a smaller size; at least one.</param>
    public WindowedBufferWriter(int window)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(window);
        this.window = window;
    }

    /// <summary>Gets the bytes advanced so far, in order.</summary>
    public byte[] Written => this.written.ToArray();

    /// <summary>Keeps the first <paramref name="count"/> bytes of the open window and closes it.</summary>
    /// <param name="count">The bytes written into the window.</param>
    /// <exception cref="InvalidOperationException"><paramref name="count"/> exceeds the open window.</exception>
    public void Advance(int count)
    {
        if (count < 0 || count > this.active.Length)
        {
            throw new InvalidOperationException("Advanced " + count + " bytes past a window of " + this.active.Length + ".");
        }

        this.written.AddRange(this.active.AsSpan(0, count).ToArray());
        this.active = [];
    }

    /// <summary>Opens a new window of <c>max(window, sizeHint)</c> bytes, filled with 0xCC so unwritten bytes stay visible.</summary>
    /// <param name="sizeHint">The requested size, or 0 for any size.</param>
    /// <returns>The window.</returns>
    public Memory<byte> GetMemory(int sizeHint = 0)
    {
        this.active = new byte[Math.Max(this.window, sizeHint)];
        this.active.AsSpan().Fill(EngineOperations.Unwritten);
        return this.active;
    }

    /// <summary>Opens a new window through <see cref="GetMemory"/>.</summary>
    /// <param name="sizeHint">The requested size, or 0 for any size.</param>
    /// <returns>The window.</returns>
    public Span<byte> GetSpan(int sizeHint = 0) => this.GetMemory(sizeHint).Span;
}
