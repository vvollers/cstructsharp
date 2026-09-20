using System.Runtime.CompilerServices;
using CStructSharp;
using CStructSharp.Values;

/// <summary>The mapped types the Native AOT consumer reads into and writes from; no reflection, no annotations.</summary>
public static class Records
{
    /// <summary>A nested mapped class, registered from a module initializer.</summary>
    public sealed class Point : ICStructMapped<Point>
    {
        public short X { get; set; }

        public short Y { get; set; }

        public static Point ReadFrom(StructValue source)
        {
            return new Point { X = source.Get<short>("x"), Y = source.Get<short>("y"), };
        }

        public static void WriteTo(Point value, StructValue target)
        {
            target["x"] = value.X;
            target["y"] = value.Y;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<Point>();
        }
    }

    /// <summary>The root type of a typed read, with a nested mapped class, an array of them, and a scalar array.</summary>
    public sealed class Record : ICStructMapped<Record>
    {
        public byte Tag { get; set; }

        public Point Origin { get; set; } = new();

        public Point[] Corners { get; set; } = [];

        public byte[] Flags { get; set; } = [];

        public static Record ReadFrom(StructValue source)
        {
            return new Record
            {
                Tag = source.Get<byte>("tag"),
                Origin = source.Get<Point>("origin"),
                Corners = source.Get<Point[]>("corners"),
                Flags = source.Get<byte[]>("flags"),
            };
        }

        public static void WriteTo(Record value, StructValue target)
        {
            target["tag"] = value.Tag;
            target["origin"] = value.Origin;
            target["corners"] = value.Corners;
            target["flags"] = value.Flags;
        }

        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<Record>();
        }
    }

    /// <summary>A plain class: not a mapping target, and the error says what is.</summary>
    public sealed class PlainRecord
    {
        public byte Tag { get; set; }
    }
}
