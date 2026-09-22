namespace CStructSharp.Tests;

using System.Diagnostics.Tracing;

/// <summary>Observes exact pool rentals and returns without assuming which array a later rental will choose.</summary>
/// <remarks>Tests enabling process-wide pool diagnostics must not run alongside allocation measurements.</remarks>
internal sealed class PoolReturnListener : EventListener
{
    private readonly int observingThread = Environment.CurrentManagedThreadId;
    private int watched;
    private int returned;
    private int lastRental;
    private int lastRentalLength;

    public bool Returned => Volatile.Read(ref this.returned) == 1;

    public int LastRental => this.lastRental;

    public int LastRentalLength => this.lastRentalLength;

    /// <summary>Starts observing one currently rented array, clearing the previous observation.</summary>
    /// <param name="bufferId">Runtime array identity reported to ArrayPool's event source.</param>
    public void Watch(int bufferId)
    {
        Volatile.Write(ref this.returned, 0);
        Volatile.Write(ref this.watched, bufferId);
    }

    /// <summary>Enables only the runtime ArrayPool diagnostics when that source exists or is created.</summary>
    /// <param name="eventSource">The source announced by the runtime.</param>
    protected override void OnEventSourceCreated(EventSource eventSource)
    {
        if (eventSource.Name == "System.Buffers.ArrayPoolEventSource")
        {
            this.EnableEvents(eventSource, EventLevel.Verbose);
        }
    }

    /// <summary>Records rentals on the observing thread and returns for the watched array only.</summary>
    /// <param name="eventData">The runtime event, whose first payload is the array identity and second is its length.</param>
    protected override void OnEventWritten(EventWrittenEventArgs eventData)
    {
        if (eventData.EventName == "BufferRented" && Environment.CurrentManagedThreadId == this.observingThread && eventData.Payload?[0] is int rental)
        {
            this.lastRental = rental;
            this.lastRentalLength = (int)eventData.Payload[1]!;
        }

        // Runtime source contract: dotnet/runtime System/Buffers/ArrayPoolEventSource.cs, BufferReturned.
        if (eventData.EventName == "BufferReturned" && eventData.Payload?[0] is int id && id == Volatile.Read(ref this.watched))
        {
            Volatile.Write(ref this.returned, 1);
        }
    }
}
