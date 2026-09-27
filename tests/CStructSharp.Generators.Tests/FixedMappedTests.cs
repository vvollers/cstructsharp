namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

/// <summary>
///     The direct members of layout-bound mapped classes (<c>ICStructFixedMapped&lt;TSelf&gt;</c>): which classes get
///     them, and that <c>ReadValue&lt;T&gt;</c> and <c>Serialize</c> through them are indistinguishable from the
///     by-name route (<c>ReadFrom</c>/<c>WriteTo</c>) for every input length, limit, value, and runtime layout - including
///     layouts whose fingerprint differs, where the direct members must not be used at all. The comparisons run inside
///     the compiled consumer (its <c>Harness</c>), where an empty variables dictionary forces the by-name route.
/// </summary>
[TestClass]
public class FixedMappedTests
{
    private const string Layout = """
        enum kind : uint8 { a = 1, b = 2 };
        struct vec { float32 x; float32 y; };
        struct vecb { float32 x; float32 y; uint8 flag; };
        struct rec { uint16 id; uint16> be; kind which; char tag[4]; vec pos; vec path[2]; uint32 samples[3]; int24 small; bool ok; struct { uint8 p; uint8 q; }; float64 last; };
        struct plain { uint16 id; vec pos; vec path[2]; uint32 samples[3]; bool ok; int8 s; uint64 big; float64 d; struct { uint8 p; uint8 q; }; uint8 raw[3]; bool flags[2]; int8 deltas[2]; };
        struct other { uint16 id; vecb pos; };
        struct crossed { uint16 id; vec pos; };
        struct dyn { uint8 n; uint8 items[n]; };
        """;

    private const string Consumer = """"
        using System;
        using System.Collections.Generic;
        using System.Linq;
        using System.Threading;
        using CStructSharp;
        using CStructSharp.Diagnostics;
        using CStructSharp.Values;

        namespace Demo;

        [CStructLayout({LAYOUT}, Root = "rec", Aligned = {ALIGNED})]
        public static partial class Wire { }

        public enum Kind : byte { A = 1, B = 2 }

        [CStructMapped(Layout = "vec")]
        public sealed partial class Vec { public float X { get; set; } public float Y { get; set; } }

        [CStructMapped(Layout = "vecb")]
        public sealed partial class VecB { public float X { get; set; } public float Y { get; set; } public byte Flag { get; set; } }

        // Readable directly (enum, text, int24, promoted members); not writable directly (text and enum validate).
        [CStructMapped(Layout = "rec")]
        public sealed partial class Rec
        {
            public ushort Id { get; set; }
            public ushort Be { get; set; }
            public Kind Which { get; set; }
            public string Tag { get; set; } = "";
            public Vec Pos { get; set; } = new();
            public Vec[] Path { get; set; } = [];
            public uint[] Samples { get; set; } = [];
            public int Small { get; set; }
            public bool Ok { get; set; }
            public byte P { get; set; }
            public byte Q { get; set; }
            public double Last { get; set; }
        }

        // Readable and writable directly.
        [CStructMapped(Layout = "plain")]
        public sealed partial class Plain
        {
            public ushort Id { get; set; }
            public Vec Pos { get; set; } = new();
            public Vec[] Path { get; set; } = [];
            public uint[] Samples { get; set; } = [];
            public bool Ok { get; set; }
            public sbyte S { get; set; }
            public ulong Big { get; set; }
            public double D { get; set; }
            public byte? P { get; set; }
            public byte Q { get; set; }
            public byte[] Raw { get; set; } = [];
            public bool[] Flags { get; set; } = [];
            public sbyte[] Deltas { get; set; } = [];
        }

        // Maps only part of the struct: readable directly, never written directly.
        [CStructMapped(Layout = "plain")]
        public sealed partial class PartialPlain { public ushort Id { get; set; } public double D { get; set; } }

        // A converted property type: no direct members at all.
        [CStructMapped(Layout = "plain")]
        public sealed partial class Widened { public long Id { get; set; } public double D { get; set; } }

        // Its nested class is bound to the same nested struct (vecb): read and written directly.
        [CStructMapped(Layout = "other")]
        public sealed partial class Other { public ushort Id { get; set; } public VecB Pos { get; set; } = new(); }

        // Its nested class is bound to another struct (vecb, not vec), so the parent's direct members always decline.
        [CStructMapped(Layout = "crossed")]
        public sealed partial class Crossed { public ushort Id { get; set; } public VecB Pos { get; set; } = new(); }

        // Not fixed (a runtime-sized array): no direct members.
        [CStructMapped(Layout = "dyn")]
        public sealed partial class Dyn { public byte N { get; set; } public byte[] Items { get; set; } = []; }

        public static class Harness
        {
            public static List<string> Run()
            {
                var failures = new List<string>();
                var layouts = new (string Label, CStruct Layout)[]
                {
                    ("generated", Wire.Layout),
                    ("other alignment", new CStruct(Wire.Definition, aligned: !{ALIGNED})),
                    ("big-endian", new CStruct(Wire.Definition, aligned: {ALIGNED}, isLittleEndian: false)),
                };
                foreach ((string label, CStruct layout) in layouts)
                {
                    Reads<Rec>(layout, "rec", RecValue(), label, failures);
                    Reads<Plain>(layout, "plain", PlainValue(), label, failures);
                    Reads<PartialPlain>(layout, "plain", PlainValue(), label, failures);
                    Reads<Widened>(layout, "plain", PlainValue(), label, failures);
                    Reads<Other>(layout, "other", new StructValue { ["id"] = (ushort)5, ["pos"] = new StructValue { ["x"] = 1f, ["y"] = 2f, ["flag"] = (byte)1 } }, label, failures);
                    Reads<Crossed>(layout, "crossed", new StructValue { ["id"] = (ushort)5, ["pos"] = new StructValue { ["x"] = 1f, ["y"] = 2f } }, label, failures);
                    Reads<Dyn>(layout, "dyn", new StructValue { ["n"] = (byte)2, ["items"] = new byte[] { 7, 8 } }, label, failures);

                    Plain plain = layout.ReadValue<Plain>(layout.Serialize("plain", PlainValue()), "plain");
                    foreach ((string valueLabel, object value) in PlainVariants(plain))
                    {
                        Writes(layout, "plain", value, label + " " + valueLabel, failures);
                    }

                    Rec rec = layout.ReadValue<Rec>(layout.Serialize("rec", RecValue()), "rec");
                    Writes(layout, "rec", rec, label + " rec", failures);
                    Writes(layout, "plain", new PartialPlain { Id = 1, D = 2 }, label + " partial", failures);
                    Writes(layout, "other", new Other { Id = 3, Pos = new VecB { X = 1, Y = 2, Flag = 3 } }, label + " other", failures);
                    Writes(layout, "crossed", new Crossed { Id = 3, Pos = new VecB { X = 1, Y = 2, Flag = 3 } }, label + " crossed", failures);
                }

                // The classes that must, or must not, have direct members.
                Expect(typeof(ICStructFixedMapped<Plain>).IsAssignableFrom(typeof(Plain)), "Plain has direct members", failures);
                Expect(typeof(ICStructFixedMapped<Rec>).IsAssignableFrom(typeof(Rec)), "Rec has direct members", failures);
                Expect(typeof(ICStructFixedMapped<PartialPlain>).IsAssignableFrom(typeof(PartialPlain)), "PartialPlain has direct members", failures);
                Expect(!typeof(Widened).GetInterfaces().Any(type => type.Name.StartsWith("ICStructFixedMapped")), "Widened has none", failures);
                Expect(!typeof(Dyn).GetInterfaces().Any(type => type.Name.StartsWith("ICStructFixedMapped")), "Dyn has none", failures);
                byte[] plainBytes = Wire.Layout.Serialize("plain", PlainValue());
                Expect(Plain.TryReadFixed(plainBytes, false, out _), "Plain reads directly", failures);
                Expect(Plain.TryWriteFixed(Wire.Layout.ReadValue<Plain>(plainBytes, "plain"), new byte[plainBytes.Length]), "Plain writes directly", failures);
                Expect(!Rec.TryWriteFixed(new Rec(), new byte[256]), "Rec never writes directly", failures);
                Expect(!PartialPlain.TryWriteFixed(new PartialPlain(), new byte[256]), "PartialPlain never writes directly", failures);
                Expect(Other.TryReadFixed(new byte[64], false, out _), "Other reads directly: its nested class is bound to the same struct", failures);
                Expect(!Crossed.TryReadFixed(new byte[64], false, out _), "Crossed declines: its nested class is bound to another struct", failures);
                Expect(!Crossed.TryWriteFixed(new Crossed(), new byte[64]), "Crossed never writes directly", failures);
                return failures;
            }

            private static IEnumerable<(string Label, object Value)> PlainVariants(Plain plain)
            {
                yield return ("read back", plain);
                yield return ("null nested", Copy(plain, value => value.Pos = null!));
                yield return ("short array", Copy(plain, value => value.Samples = [1]));
                yield return ("null array", Copy(plain, value => value.Samples = null!));
                yield return ("null element", Copy(plain, value => value.Path = [new Vec(), null!]));
                yield return ("null optional", Copy(plain, value => value.P = null));
                yield return ("long raw", Copy(plain, value => value.Raw = [1, 2, 3, 4]));
                yield return ("short flags", Copy(plain, value => value.Flags = [true]));
                yield return ("wrapped", new Dictionary<string, object?> { ["plain"] = plain });
            }

            private static Plain Copy(Plain source, Action<Plain> change)
            {
                var copy = new Plain { Id = source.Id, Pos = source.Pos, Path = source.Path, Samples = source.Samples, Ok = source.Ok, S = source.S, Big = source.Big, D = source.D, P = source.P, Q = source.Q, Raw = source.Raw, Flags = source.Flags, Deltas = source.Deltas };
                change(copy);
                return copy;
            }

            private static StructValue RecValue() => new()
            {
                ["id"] = (ushort)0x0102, ["be"] = (ushort)0x0304, ["which"] = 2, ["tag"] = "IR",
                ["pos"] = new StructValue { ["x"] = 1.5f, ["y"] = -2f },
                ["path"] = new object[] { new StructValue { ["x"] = 3f, ["y"] = 4f }, new StructValue { ["x"] = 5f, ["y"] = 6f } },
                ["samples"] = new uint[] { 1, 2, 3 }, ["small"] = -5, ["ok"] = true, ["p"] = (byte)7, ["q"] = (byte)8, ["last"] = 0.25,
            };

            private static StructValue PlainValue() => new()
            {
                ["id"] = (ushort)0x0102, ["pos"] = new StructValue { ["x"] = 1.5f, ["y"] = -2f },
                ["path"] = new object[] { new StructValue { ["x"] = 3f, ["y"] = 4f }, new StructValue { ["x"] = 5f, ["y"] = 6f } },
                ["samples"] = new uint[] { 1, 2, 3 }, ["ok"] = true, ["s"] = (sbyte)-3, ["big"] = ulong.MaxValue, ["d"] = -0.5,
                ["p"] = (byte)7, ["q"] = (byte)8, ["raw"] = new byte[] { 9, 10, 11 },
                ["flags"] = new bool[] { true, false }, ["deltas"] = new sbyte[] { -1, 2 },
            };

            private static void Reads<T>(CStruct layout, string root, StructValue value, string label, List<string> failures)
            {
                byte[] bytes = layout.Serialize(root, value);
                var inputs = new List<(string Label, byte[] Input, ReadOptions? Options)>();
                for (int length = 0; length <= bytes.Length; length++)
                {
                    inputs.Add(("length " + length, bytes[..length], null));
                }

                inputs.Add(("longer", [.. bytes, 0xEE], null));
                foreach (long budget in new long[] { 1, bytes.Length - 1, bytes.Length })
                {
                    inputs.Add(("budget " + budget, bytes, new ReadOptions { MaxTotalBytesRead = budget }));
                }

                inputs.Add(("nesting 1", bytes, new ReadOptions { MaxNestingDepth = 1 }));
                inputs.Add(("arrays 1", bytes, new ReadOptions { MaxArrayElements = 1 }));
                inputs.Add(("trimmed", bytes, new ReadOptions { TrimFixedText = true }));
                inputs.Add(("invalid", bytes, new ReadOptions { MaxArrayElements = -1 }));
                using var cancelled = new CancellationTokenSource();
                cancelled.Cancel();
                inputs.Add(("cancelled", bytes, new ReadOptions { CancellationToken = cancelled.Token }));
                foreach ((string inputLabel, byte[] input, ReadOptions? options) in inputs)
                {
                    Compare(
                        () => layout.ReadValue<T>(input, root, null, options),
                        () => layout.ReadValue<T>(input, root, new Dictionary<string, int>(), options),
                        $"{label} {typeof(T).Name} {inputLabel}",
                        failures);
                }
            }

            private static void Writes(CStruct layout, string root, object value, string label, List<string> failures)
            {
                int size = layout.GetStructSizeInBytes(root);
                var options = new (string Label, WriteOptions? Options)[]
                {
                    ("default", null),
                    ("reject unknown", new WriteOptions { UnknownMembers = UnknownMemberPolicy.Reject }),
                    ("budget short", new WriteOptions { MaxTotalBytesWritten = size - 1 }),
                    ("budget exact", new WriteOptions { MaxTotalBytesWritten = size }),
                    ("nesting 1", new WriteOptions { MaxNestingDepth = 1 }),
                    ("arrays 1", new WriteOptions { MaxArrayElements = 1 }),
                };
                foreach ((string optionLabel, WriteOptions? writeOptions) in options)
                {
                    Compare(
                        () => Convert.ToHexString(layout.Serialize(root, value, null, writeOptions)),
                        () => Convert.ToHexString(layout.Serialize(root, value, new Dictionary<string, int>(), writeOptions)),
                        $"{label} {optionLabel} array",
                        failures);
                    for (int capacity = size - 1; capacity <= size + 1; capacity++)
                    {
                        byte[] direct = Enumerable.Repeat((byte)0xCC, capacity).ToArray();
                        byte[] general = Enumerable.Repeat((byte)0xCC, capacity).ToArray();
                        Compare(
                            () => layout.Serialize(direct, root, value, null, writeOptions) + ":" + Convert.ToHexString(direct),
                            () => layout.Serialize(general, root, value, new Dictionary<string, int>(), writeOptions) + ":" + Convert.ToHexString(general),
                            $"{label} {optionLabel} span of {capacity}",
                            failures);
                    }
                }
            }

            private static void Compare<T>(Func<T> direct, Func<T> general, string label, List<string> failures)
            {
                string expected = Outcome(general);
                string actual = Outcome(direct);
                if (expected != actual)
                {
                    failures.Add(label + "\n  expected: " + expected + "\n  actual:   " + actual);
                }
            }

            private static string Outcome<T>(Func<T> run)
            {
                try
                {
                    return "value " + Render(run());
                }
                catch (Exception exception) when (exception is CStructException or ArgumentException or OperationCanceledException)
                {
                    var failure = exception as CStructException;
                    return exception.GetType().Name + ": " + exception.Message + " | " + failure?.Path + " | " + failure?.Offset;
                }
            }

            private static string Render(object? value)
            {
                return value switch
                {
                    null => "null",
                    string text => "\"" + text + "\"",
                    System.Collections.IEnumerable items => "[" + string.Join(",", items.Cast<object?>().Select(Render)) + "]",
                    _ when value.GetType().Namespace == "Demo" && !value.GetType().IsEnum =>
                        value.GetType().Name + "{" + string.Join(",", value.GetType().GetProperties().Select(property => property.Name + "=" + Render(property.GetValue(value)))) + "}",
                    Enum enumeration => enumeration.GetType().Name + "." + enumeration + "=" + Convert.ToInt64(enumeration),
                    float or double => value.GetType().Name + ":" + ((IFormattable)value).ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                    IFormattable formattable => value.GetType().Name + ":" + formattable.ToString(null, System.Globalization.CultureInfo.InvariantCulture),
                    _ => value.GetType().Name + ":" + value,
                };
            }

            private static void Expect(bool condition, string label, List<string> failures)
            {
                if (!condition)
                {
                    failures.Add("expected: " + label);
                }
            }
        }
        """";

    /// <summary>For a packed and an aligned layout, the direct members exist exactly where the rules allow, and every direct read and write matches the by-name route.</summary>
    [TestMethod]
    public void DirectMembers_MatchTheByNameRoute()
    {
        foreach (bool aligned in new[] { false, true })
        {
            string source = Consumer.Replace("{LAYOUT}", ReaderParityTests.Literal(Layout), StringComparison.Ordinal)
                                    .Replace("{ALIGNED}", aligned ? "true" : "false", StringComparison.Ordinal);
            GeneratorResult result = GeneratorRunner.Run(source).AssertClean();
            if (!aligned)
            {
                Snapshot.Match("Mapped.FixedPlain", result.GeneratedSources.Single(generated => generated.HintName == "Demo.Plain.CStructMapped.g.cs").Source);
            }

            Assembly assembly = result.Load();
            System.Runtime.CompilerServices.RuntimeHelpers.RunModuleConstructor(assembly.ManifestModule.ModuleHandle);
            var failures = (List<string>)assembly.GetType("Demo.Harness")!.GetMethod("Run")!.Invoke(null, null)!;
            Assert.IsEmpty(failures, $"aligned={aligned}:\n" + string.Join("\n", failures.Take(20)));
        }
    }
}
