using System.Runtime.CompilerServices;
using CStructSharp;
using CStructSharp.Values;

/// <summary>The mapped types the Native AOT consumer reads into and writes from; no reflection, no annotations.</summary>
public static class Records
{
    /// <summary>A nested mapped class, registered from a module initializer.</summary>
    public sealed class Point : ICStructMapped<Point>
    {
        /// <summary>Gets or sets the <c>int16 x</c> field.</summary>
        public short X { get; set; }

        /// <summary>Gets or sets the <c>int16 y</c> field.</summary>
        public short Y { get; set; }

        /// <summary>Creates a point from its parsed <c>point</c> value.</summary>
        /// <param name="source">The parsed struct.</param>
        /// <returns>The mapped point.</returns>
        public static Point ReadFrom(StructValue source)
        {
            return new Point { X = source.Get<short>("x"), Y = source.Get<short>("y"), };
        }

        /// <summary>Copies a point into a <c>point</c> value for writing.</summary>
        /// <param name="value">The mapped point.</param>
        /// <param name="target">The struct value to fill.</param>
        public static void WriteTo(Point value, StructValue target)
        {
            target["x"] = value.X;
            target["y"] = value.Y;
        }

        /// <summary>Registers the <see cref="Point"/> mapping when the module loads, so no reflection is needed.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<Point>();
        }
    }

    /// <summary>The root type of a typed read, with a nested mapped class, an array of them, and a scalar array.</summary>
    public sealed class Record : ICStructMapped<Record>
    {
        /// <summary>Gets or sets the <c>uint8 tag</c> field.</summary>
        public byte Tag { get; set; }

        /// <summary>Gets or sets the nested <c>point origin</c>.</summary>
        public Point Origin { get; set; } = new();

        /// <summary>Gets or sets the <c>point corners[2]</c> array.</summary>
        public Point[] Corners { get; set; } = [];

        /// <summary>Gets or sets the <c>uint8 flags[3]</c> array.</summary>
        public byte[] Flags { get; set; } = [];

        /// <summary>Creates a record from its parsed <c>record</c> value, reading the nested points as mapped classes.</summary>
        /// <param name="source">The parsed struct.</param>
        /// <returns>The mapped record.</returns>
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

        /// <summary>Copies a record into a <c>record</c> value for writing.</summary>
        /// <param name="value">The mapped record.</param>
        /// <param name="target">The struct value to fill.</param>
        public static void WriteTo(Record value, StructValue target)
        {
            target["tag"] = value.Tag;
            target["origin"] = value.Origin;
            target["corners"] = value.Corners;
            target["flags"] = value.Flags;
        }

        /// <summary>Registers the <see cref="Record"/> mapping when the module loads, so no reflection is needed.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<Record>();
        }
    }

    /// <summary>A plain class: not a mapping target, and the error says what is.</summary>
    public sealed class PlainRecord
    {
        /// <summary>Gets or sets a tag value; the class is never mapped.</summary>
        public byte Tag { get; set; }
    }
}
