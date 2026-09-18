using System.Diagnostics.CodeAnalysis;

/// <summary>The mapped types the Native AOT consumer reads into and writes from.</summary>
public static class Records
{
    /// <summary>A nested mapped class: the attribute keeps its public members and constructor under trimming.</summary>
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor | DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)]
    public sealed class Point
    {
        public short X { get; set; }

        public short Y { get; set; }
    }

    /// <summary>The root type of a typed read; ReadValue&lt;T&gt; preserves it through its own annotation.</summary>
    public sealed class Record
    {
        public byte Tag { get; set; }

        public Point Origin { get; set; } = new();

        public Point[] Corners { get; set; } = [];

        public byte[] Flags { get; set; } = [];
    }

    /// <summary>A collection-interface member, which needs a run-time List&lt;T&gt; and therefore dynamic code.</summary>
    public sealed class InterfaceRecord
    {
        public byte Tag { get; set; }

        public Point Origin { get; set; } = new();

        public IList<Point> Corners { get; set; } = [];

        public byte[] Flags { get; set; } = [];
    }
}
