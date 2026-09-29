namespace CStructSharp.Engine;

using System;

/// <summary>
///     One open test recording of engine decisions, returned by <see cref="EngineDiagnostics.Record"/>. Disposing it
///     restores the recording it replaced on the calling flow; disposing it again does nothing.
/// </summary>
internal sealed class EngineRecording : IDisposable
{
    /// <summary>The recorder that was current on the opening flow, restored on disposal.</summary>
    private readonly EngineDiagnostics? previous;

    /// <summary>Whether the recording has been closed.</summary>
    private bool disposed;

    /// <summary>Creates a recording; <see cref="EngineDiagnostics.Record"/> publishes it.</summary>
    /// <param name="diagnostics">The recorder the recording counts into.</param>
    /// <param name="previous">The recorder current on the opening flow.</param>
    internal EngineRecording(EngineDiagnostics diagnostics, EngineDiagnostics? previous)
    {
        this.Diagnostics = diagnostics;
        this.previous = previous;
    }

    /// <summary>Gets the recorder that counts the decisions made while the recording is open.</summary>
    public EngineDiagnostics Diagnostics { get; }

    /// <summary>Closes the recording on the calling flow, which must be the flow that opened it.</summary>
    public void Dispose()
    {
        if (!this.disposed)
        {
            this.disposed = true;
            EngineDiagnostics.Close(this.previous);
        }
    }
}
