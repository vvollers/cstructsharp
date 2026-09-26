namespace CStructSharp.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CStructSharp.Diagnostics;
using CStructSharp.Values;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
///     Pins <see cref="FieldAccessor{T}"/> and <see cref="StructView"/>: a prepared read must be indistinguishable from
///     the path-string read it replaces - <c>StructValue.Get&lt;T&gt;</c> for a parsed struct, <c>ReadValue&lt;T&gt;</c>
///     for bytes - in value, exception type, message, path and offset, for every member kind, input length and limit.
/// </summary>
[TestClass]
public class AccessorAndViewTests
{
    private const string Layout = """
        enum kind : uint8 { a = 1, b = 2 };
        struct vec { float32 x; float32 y; };
        union u { uint16 wide; uint8 narrow; };
        struct rec {
            uint16 id;
            uint16> be;
            kind which;
            char tag[4];
            vec pos;
            vec path[2];
            uint32 samples[3];
            int24 small;
            bool ok;
            struct { uint8 p; uint8 q; };
            u choice;
            uint8 low : 4;
            uint8 high : 4;
        };
        struct dyn { uint8 n; uint8 items[n]; uint16 after; };
        struct flat {
            uint16 id;
            uint16> be;
            kind which;
            char tag[4];
            vec pos;
            vec path[2];
            uint32 samples[3];
            int24 small;
            bool ok;
            struct { uint8 p; uint8 q; };
            double last;
        };
        """;

    private static readonly string[] RecPaths =
    [
        "id", "be", "which", "tag", "tag[1]", "pos", "pos.x", "pos.y", "path", "path[1]", "path[1].y", "path[2].x",
        "samples", "samples[0]", "samples[2]", "samples[3]", "small", "ok", "p", "q", "choice", "choice.wide",
        "choice.narrow", "low", "high", "missing", "pos.z", "samples[1].x", string.Empty,
    ];

    private static readonly string[] FlatPaths =
    [
        "id", "be", "which", "tag", "tag[1]", "pos", "pos.x", "pos.y", "path", "path[1]", "path[1].y", "path[2].x",
        "samples", "samples[0]", "samples[2]", "samples[3]", "small", "ok", "p", "q", "last", "missing", "pos.z", string.Empty,
    ];

    private static readonly string[] DynPaths = ["n", "items", "items[0]", "items[1]", "items[5]", "after", "missing"];

    /// <summary>An accessor's read of a parsed struct equals <c>Get&lt;T&gt;</c> of the same path, for every member kind and type.</summary>
    [TestMethod]
    public void Accessor_Get_MatchesStructValueGet()
    {
        foreach (bool aligned in new[] { false, true })
        {
            var layout = new CStruct(Layout, aligned: aligned);
            StructValue rec = layout.Parse(RecBytes(layout), "rec");
            StructValue dyn = layout.Parse(DynBytes(layout), "dyn");
            StructValue flat = layout.Parse(FlatBytes(layout), "flat");
            var assembled = new StructValue { ["id"] = (ushort)7, ["pos"] = new StructValue { ["x"] = 1f } };
            foreach (string path in RecPaths)
            {
                foreach (StructValue value in new[] { rec, assembled })
                {
                    AssertAllTypes(layout, "rec", path, value, $"aligned={aligned} rec.{path}");
                }
            }

            foreach (string path in DynPaths)
            {
                AssertAllTypes(layout, "dyn", path, dyn, $"aligned={aligned} dyn.{path}");
            }

            foreach (string path in FlatPaths)
            {
                AssertAllTypes(layout, "flat", path, flat, $"aligned={aligned} flat.{path}");
            }
        }
    }

    /// <summary>An accessor's read of bytes and a view's member read equal <c>ReadValue&lt;T&gt;</c> of the same path, for every input length and limit.</summary>
    [TestMethod]
    public void Accessor_Read_And_View_Get_MatchReadValue()
    {
        foreach (bool aligned in new[] { false, true })
        {
            var layout = new CStruct(Layout, aligned: aligned);
            foreach ((string root, byte[] bytes, string[] paths) in new[] { ("rec", RecBytes(layout), RecPaths), ("dyn", DynBytes(layout), DynPaths), ("flat", FlatBytes(layout), FlatPaths) })
            {
                var inputs = new List<(string Label, byte[] Input, ReadOptions? Options)>();
                for (int length = 0; length <= bytes.Length; length += Math.Max(1, bytes.Length / 12))
                {
                    inputs.Add(($"length {length}", bytes[..length], null));
                }

                inputs.Add(("whole", bytes, null));
                inputs.Add(("longer", [.. bytes, 0xEE], null));
                foreach (long budget in new long[] { 1, 2, 5, bytes.Length - 1, bytes.Length })
                {
                    inputs.Add(($"budget {budget}", bytes, new ReadOptions { MaxTotalBytesRead = budget }));
                }

                inputs.Add(("nesting 1", bytes, new ReadOptions { MaxNestingDepth = 1 }));
                inputs.Add(("nesting 2", bytes, new ReadOptions { MaxNestingDepth = 2 }));
                inputs.Add(("arrays 1", bytes, new ReadOptions { MaxArrayElements = 1 }));
                inputs.Add(("trimmed", bytes, new ReadOptions { TrimFixedText = true }));
                inputs.Add(("invalid", bytes, new ReadOptions { MaxArrayElements = -1 }));
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                inputs.Add(("cancelled", bytes, new ReadOptions { CancellationToken = cancelled.Token }));

                foreach ((string label, byte[] input, ReadOptions? options) in inputs)
                {
                    foreach (string path in paths)
                    {
                        string full = path.Length == 0 ? root : root + "." + path;
                        string caseLabel = $"aligned={aligned} {full} {label}";
                        AssertReadTypes(layout, root, full, path, input, options, caseLabel);
                    }
                }
            }
        }
    }

    /// <summary>A view reads its own bytes, parses to the same struct as <c>Parse</c>, and rejects inputs and accessors that do not fit it.</summary>
    [TestMethod]
    public void View_ChecksItsInputAndAccessors()
    {
        var layout = new CStruct(Layout);
        byte[] bytes = [.. FlatBytes(layout), 0xEE, 0xEE];
        StructView view = layout.CreateView(bytes, "flat");
        Assert.AreEqual("flat", view.Root);
        Assert.AreEqual(layout.GetStructSizeInBytes("flat"), view.Bytes.Length);
        Assert.AreEqual(Render(layout.Parse(bytes, "flat")), Render(view.ToStructValue()));
        Assert.AreEqual((ushort)0x0102, view.Get<ushort>("id"));
        Assert.AreEqual(2u, view.Get(layout.GetAccessor<uint>("flat.samples[1]")));

        // The members of a fixed struct read as their own type have constant offsets; everything else reads through ReadValue.
        Assert.IsTrue(layout.GetAccessor<ushort>("flat.be").HasFixedOffset);
        Assert.IsTrue(layout.GetAccessor<float>("flat.path[1].y").HasFixedOffset);
        Assert.IsTrue(layout.GetAccessor<int>("flat.small").HasFixedOffset);
        Assert.IsTrue(layout.GetAccessor<byte>("flat.q").HasFixedOffset, "a promoted member");
        Assert.IsFalse(layout.GetAccessor<long>("flat.samples[1]").HasFixedOffset, "a converted type");
        Assert.IsFalse(layout.GetAccessor<byte>("flat.which").HasFixedOffset, "an enum");
        Assert.IsFalse(layout.GetAccessor<ushort>("rec.id").HasFixedOffset, "a struct with a union and bitfields");
        Assert.IsFalse(layout.GetAccessor<byte>("dyn.n").HasFixedOffset, "a runtime-sized struct");

        CStructReadException shortInput = Assert.ThrowsExactly<CStructReadException>(() => layout.CreateView(bytes.AsSpan(0, 3), "flat"));
        StringAssert.StartsWith(shortInput.Message, "Not enough bytes");
        Assert.AreEqual("flat", shortInput.Path);
        Assert.AreEqual(0L, shortInput.Offset);
        Assert.ThrowsExactly<CStructPathException>(() => layout.CreateView(bytes, "u"), "a union is not a view root");
        Assert.ThrowsExactly<CStructPathException>(() => layout.CreateView(bytes, "kind"), "an enum is not a view root");
        Assert.ThrowsExactly<CStructPathException>(() => layout.CreateView(bytes, "nothing"));

        var other = new CStruct(Layout);
        FieldAccessor<ushort> foreign = other.GetAccessor<ushort>("rec.id");
        FieldAccessor<byte> otherRoot = layout.GetAccessor<byte>("dyn.n");
        Assert.ThrowsExactly<ArgumentException>(() => layout.CreateView(RecBytes(layout), "rec").Get(foreign));
        Assert.ThrowsExactly<ArgumentException>(() => layout.CreateView(RecBytes(layout), "rec").Get(otherRoot));

        StructView dynamic = layout.CreateView(DynBytes(layout), "dyn");
        Assert.AreEqual((ushort)0x0405, dynamic.Get<ushort>("after"), "a runtime-sized struct reads through ReadValue");
        Assert.AreEqual(DynBytes(layout).Length, dynamic.Bytes.Length);
    }

    /// <summary>Accessor paths start with a struct declaration; anything else is refused when the accessor is created.</summary>
    [TestMethod]
    public void GetAccessor_RequiresAStructRoot()
    {
        var layout = new CStruct(Layout);
        Assert.ThrowsExactly<ArgumentNullException>(() => layout.GetAccessor<int>(null!));
        Assert.ThrowsExactly<CStructPathException>(() => layout.GetAccessor<int>("nothing.id"));
        Assert.ThrowsExactly<CStructPathException>(() => layout.GetAccessor<int>("rec..id"));
        Assert.ThrowsExactly<CStructPathException>(() => layout.GetAccessor<int>("u.wide"), "a union root");
        Assert.ThrowsExactly<CStructPathException>(() => layout.GetAccessor<int>("kind"), "an enum root");
        Assert.AreEqual("rec.pos.x", layout.GetAccessor<float>("rec.pos.x").Path);
        Assert.AreEqual("rec", layout.GetAccessor<float>("rec.pos.x").Root);
    }

    /// <summary>Checks one path for several requested types: the member's own type, conversions, and a failing type.</summary>
    private static void AssertAllTypes(CStruct layout, string root, string path, StructValue value, string label)
    {
        string full = path.Length == 0 ? root : root + "." + path;
        AssertGet<ushort>(layout, full, path, value, label);
        AssertGet<long>(layout, full, path, value, label);
        AssertGet<byte>(layout, full, path, value, label);
        AssertGet<float>(layout, full, path, value, label);
        AssertGet<uint>(layout, full, path, value, label);
        AssertGet<int>(layout, full, path, value, label);
        AssertGet<bool>(layout, full, path, value, label);
        AssertGet<string>(layout, full, path, value, label);
        AssertGet<object>(layout, full, path, value, label);
        AssertGet<uint[]>(layout, full, path, value, label);
        AssertGet<StructValue>(layout, full, path, value, label);
    }

    /// <summary>Compares an accessor's read of a parsed struct with <c>Get&lt;T&gt;</c>.</summary>
    private static void AssertGet<T>(CStruct layout, string full, string path, StructValue value, string label)
    {
        FieldAccessor<T> accessor = layout.GetAccessor<T>(full);
        AssertSame(Run(() => value.Get<T>(path)), Run(() => accessor.Get(value)), $"{label} as {typeof(T).Name}");
    }

    /// <summary>Compares accessor and view reads of bytes with <c>ReadValue&lt;T&gt;</c> for several requested types.</summary>
    private static void AssertReadTypes(CStruct layout, string root, string full, string path, byte[] input, ReadOptions? options, string label)
    {
        AssertRead<ushort>(layout, root, full, path, input, options, label);
        AssertRead<uint>(layout, root, full, path, input, options, label);
        AssertRead<long>(layout, root, full, path, input, options, label);
        AssertRead<float>(layout, root, full, path, input, options, label);
        AssertRead<int>(layout, root, full, path, input, options, label);
        AssertRead<bool>(layout, root, full, path, input, options, label);
        AssertRead<byte>(layout, root, full, path, input, options, label);
        AssertRead<string>(layout, root, full, path, input, options, label);
    }

    /// <summary>Compares <see cref="FieldAccessor{T}.Read"/>, <see cref="StructView.Get{T}(FieldAccessor{T})"/> and <see cref="StructView.Get{T}(string)"/> with <c>ReadValue&lt;T&gt;</c>.</summary>
    private static void AssertRead<T>(CStruct layout, string root, string full, string path, byte[] input, ReadOptions? options, string label)
    {
        string caseLabel = $"{label} as {typeof(T).Name}";
        FieldAccessor<T> accessor = layout.GetAccessor<T>(full);
        (object? Value, Exception? Error) expected = Run(() => layout.ReadValue<T>(input, full, options: options));
        AssertSame(expected, Run(() => accessor.Read(input, options)), caseLabel + " / Read");

        (object? Value, Exception? Error) created = Run(() => layout.CreateView(input, root, options).Root);
        if (created.Error is not null)
        {
            // A fixed struct shorter than its size cannot become a view; that input is covered by Read above.
            Assert.IsInstanceOfType<CStructReadException>(created.Error, caseLabel);
            return;
        }

        AssertSame(expected, Run(() => layout.CreateView(input, root, options).Get(accessor)), caseLabel + " / view accessor");
        if (path.Length > 0)
        {
            AssertSame(expected, Run(() => layout.CreateView(input, root, options).Get<T>(path)), caseLabel + " / view path");
        }
    }

    /// <summary>Asserts that two outcomes agree: exception type, message, path and offset, or the rendered value.</summary>
    private static void AssertSame((object? Value, Exception? Error) expected, (object? Value, Exception? Error) actual, string label)
    {
        Assert.AreEqual(expected.Error?.GetType(), actual.Error?.GetType(), label + ": " + (actual.Error ?? expected.Error)?.Message);
        Assert.AreEqual(expected.Error?.Message, actual.Error?.Message, label);
        Assert.AreEqual((expected.Error as CStructException)?.Path, (actual.Error as CStructException)?.Path, label + ": path");
        Assert.AreEqual((expected.Error as CStructException)?.Offset, (actual.Error as CStructException)?.Offset, label + ": offset");
        Assert.AreEqual(Render(expected.Value), Render(actual.Value), label);
    }

    /// <summary>Runs a read, capturing the exceptions the library documents.</summary>
    private static (object? Value, Exception? Error) Run(Func<object?> read)
    {
        try
        {
            return (read(), null);
        }
        catch (Exception exception) when (exception is CStructException or ArgumentException or OperationCanceledException or InvalidCastException)
        {
            return (null, exception);
        }
    }

    /// <summary>Renders a value as text, with its runtime type, for comparison.</summary>
    private static string Render(object? value)
    {
        return value switch
        {
            null => "null",
            StructValue s => "{" + string.Join(",", s.Select(pair => pair.Key + ":" + Render(pair.Value))) + "}",
            UnionValue u => "union(" + string.Join(",", u.Members.Select(pair => pair.Key + ":" + Render(pair.Value))) + ")",
            EnumValueResult e => e.Enum + "." + (e.Name ?? "?") + "=" + e.Value,
            string text => "\"" + text + "\"",
            System.Collections.IEnumerable items => "[" + string.Join(",", items.Cast<object?>().Select(Render)) + "]",
            _ => value.GetType().Name + ":" + value,
        };
    }

    /// <summary>A sample <c>rec</c>, encoded by the layout under test.</summary>
    private static byte[] RecBytes(CStruct layout)
    {
        return layout.Serialize("rec", new StructValue
        {
            ["id"] = (ushort)0x0102,
            ["be"] = (ushort)0x0304,
            ["which"] = 2,
            ["tag"] = "IR",
            ["pos"] = new StructValue { ["x"] = 1.5f, ["y"] = -2f },
            ["path"] = new object[] { new StructValue { ["x"] = 3f, ["y"] = 4f }, new StructValue { ["x"] = 5f, ["y"] = 6f } },
            ["samples"] = new uint[] { 1, 2, 3 },
            ["small"] = -5,
            ["ok"] = true,
            ["p"] = (byte)7,
            ["q"] = (byte)8,
            ["choice"] = UnionValue.FromRaw("u", new byte[] { 0x34, 0x12 }),
            ["low"] = (byte)3,
            ["high"] = (byte)9,
        });
    }

    /// <summary>A sample <c>dyn</c> with two items, encoded by the layout under test.</summary>
    private static byte[] DynBytes(CStruct layout)
    {
        return layout.Serialize("dyn", new StructValue { ["n"] = (byte)2, ["items"] = new byte[] { 0xA1, 0xA2 }, ["after"] = (ushort)0x0405 });
    }

    /// <summary>A sample <c>flat</c>, whose members all have build-time offsets, encoded by the layout under test.</summary>
    private static byte[] FlatBytes(CStruct layout)
    {
        return layout.Serialize("flat", new StructValue
        {
            ["id"] = (ushort)0x0102,
            ["be"] = (ushort)0x0304,
            ["which"] = 2,
            ["tag"] = "IR",
            ["pos"] = new StructValue { ["x"] = 1.5f, ["y"] = -2f },
            ["path"] = new object[] { new StructValue { ["x"] = 3f, ["y"] = 4f }, new StructValue { ["x"] = 5f, ["y"] = 6f } },
            ["samples"] = new uint[] { 1, 2, 3 },
            ["small"] = -5,
            ["ok"] = true,
            ["p"] = (byte)7,
            ["q"] = (byte)8,
            ["last"] = 0.25,
        });
    }
}
