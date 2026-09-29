namespace CStructSharp.Tests;

using System.Runtime.CompilerServices;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     Verifies that every write path binds the caller's data the same way: <see cref="UnknownMemberPolicy.Reject"/>
///     accepts the members of anonymous promoted structs and unions, which the caller supplies on the parent, and still
///     rejects an undeclared key with one message; and a mapped instance nested inside a dictionary or
///     <see cref="StructValue"/> root is written like the same instance inside a mapped root.
/// </summary>
[TestClass]
public class WriteBindingConsistencyTests
{
    /// <summary>The execution paths that must agree.</summary>
    private static readonly ExecutionPath[] Paths = [ExecutionPath.Fastest, ExecutionPath.NoDirectAccess, ExecutionPath.GeneralOnly];

    /// <summary>
    ///     Gets the promoted-member layouts: a case name, the definition, and the input a parse turns into the value to
    ///     write. The parsed value carries exactly the declared members, promoted ones included.
    /// </summary>
    public static IEnumerable<object[]> PromotedLayouts =>
    [
        ["fixed promoted struct", "struct root { uint8 kind; struct { uint8 x; uint8 y; }; };", new byte[] { 1, 2, 3, }],
        ["runtime-sized promoted struct", "struct root { uint8 kind; struct { uint8 n; uint8 d[n]; }; uint8 tail; };", new byte[] { 1, 2, 7, 8, 9, }],
        ["transitively promoted structs", "struct root { uint8 kind; struct { uint8 x; struct { uint8 y; }; }; };", new byte[] { 1, 2, 3, }],
        ["promoted struct holding a named struct", "struct inner { uint8 a; }; struct root { uint8 kind; struct { inner i; uint8 x; }; };", new byte[] { 1, 2, 3, }],
        ["promoted union", "struct root { uint8 kind; union { uint8 a; uint16 w; }; uint8 tail; };", new byte[] { 1, 2, 3, 9, }],
        ["struct promoted through a union", "struct root { uint8 kind; union { struct { uint8 lo; uint8 hi; }; uint16 w; }; uint8 tail; };", new byte[] { 1, 2, 3, 9, }],
    ];

    /// <summary>
    ///     Gets the nested-mapped-instance layouts: a case name and the definition. <c>nested</c> and both elements of
    ///     <c>items</c> are <c>inner</c> structs; the first layout is fixed (the static write plan and direct root writes
    ///     apply), the second is not.
    /// </summary>
    public static IEnumerable<object[]> NestedMappedLayouts =>
    [
        ["fixed", "struct inner { uint8 a; }; struct root { uint16 kind; inner nested; inner items[2]; };"],
        ["runtime-sized", "struct inner { uint8 a; }; struct root { uint16 kind; inner nested; uint8 n; inner items[n]; };"],
    ];

    /// <summary>
    ///     Under <see cref="UnknownMemberPolicy.Reject"/>, the parsed value and the same members in a dictionary write
    ///     the input's bytes on every path, and an undeclared key fails with the same message on every path.
    /// </summary>
    /// <param name="name">The case name.</param>
    /// <param name="definition">The layout source.</param>
    /// <param name="data">The input.</param>
    [TestMethod]
    [DynamicData(nameof(PromotedLayouts))]
    public void Reject_AcceptsPromotedMembersOnEveryPath(string name, string definition, byte[] data)
    {
        var layout = new CStruct(definition);
        StructValue parsed = layout.Parse(data.AsSpan(), "root");
        Dictionary<string, object?> dictionary = ToDictionary(parsed);
        var withExtra = new Dictionary<string, object?>(dictionary) { ["zz"] = 1, };

        var unexpected = new List<string>();
        foreach ((string form, object value) in new (string, object)[] { ("StructValue", parsed), ("dictionary", dictionary), })
        {
            foreach ((string operation, OperationOutcome[] outcomes) in WriteEverywhere(layout, value, data.Length))
            {
                for (int index = 0; index < outcomes.Length; index++)
                {
                    if (outcomes[index].Failure is { } failure)
                    {
                        unexpected.Add(form + ", " + operation + " (" + Paths[index] + "): " + failure.Message);
                    }
                    else
                    {
                        CollectionAssert.AreEqual(data, (byte[])outcomes[index].Result!, name + ", " + form + ", " + operation + " (" + Paths[index] + ")");
                    }
                }
            }
        }

        Assert.AreEqual(0, unexpected.Count, name + ":\n" + string.Join("\n", unexpected));
        foreach ((string operation, OperationOutcome[] outcomes) in WriteEverywhere(layout, withExtra, data.Length))
        {
            for (int index = 0; index < outcomes.Length; index++)
            {
                Assert.IsInstanceOfType<CStructWriteException>(outcomes[index].Failure, name + ", " + operation + " (" + Paths[index] + ")");
                StringAssert.Contains(outcomes[index].Failure!.Message, "'zz' is not a member of 'root'", name + ", " + operation + " (" + Paths[index] + ")");
                OperationOutcome.AssertSame(outcomes[^1], outcomes[index], name + ", " + operation + " (" + Paths[index] + " vs " + ExecutionPath.GeneralOnly + ")");
            }
        }
    }

    /// <summary>
    ///     Under <see cref="UnknownMemberPolicy.Reject"/>, an undeclared key inside a named struct that a promoted struct
    ///     holds is still found, with the same message and member on every path.
    /// </summary>
    [TestMethod]
    public void Reject_FindsUnknownKeysBelowAPromotedStruct()
    {
        var layout = new CStruct("struct inner { uint8 a; }; struct root { uint8 kind; struct { inner i; uint8 x; }; uint8 n; uint8 d[n]; };");
        var value = new Dictionary<string, object?>
        {
            ["kind"] = (byte)1,
            ["i"] = new Dictionary<string, object?> { ["a"] = (byte)2, ["zz"] = 3, },
            ["x"] = (byte)4,
            ["n"] = (byte)0,
            ["d"] = Array.Empty<byte>(),
        };
        foreach ((string operation, OperationOutcome[] outcomes) in WriteEverywhere(layout, value, 5))
        {
            for (int index = 0; index < outcomes.Length; index++)
            {
                Assert.IsInstanceOfType<CStructWriteException>(outcomes[index].Failure, operation + " (" + Paths[index] + ")");
                StringAssert.Contains(outcomes[index].Failure!.Message, "'zz' is not a member of 'inner'", operation + " (" + Paths[index] + ")");
                StringAssert.Contains(outcomes[index].Failure!.Message, "field 'i'", operation + " (" + Paths[index] + ")");
                OperationOutcome.AssertSame(outcomes[^1], outcomes[index], operation + " (" + Paths[index] + " vs " + ExecutionPath.GeneralOnly + ")");
            }
        }
    }

    /// <summary>
    ///     Under <see cref="UnknownMemberPolicy.Reject"/>, a mapped class that supplies a promoted struct's members on the
    ///     parent writes on every path.
    /// </summary>
    [TestMethod]
    public void Reject_AcceptsAMappedRootWithPromotedMembers()
    {
        var layout = new CStruct("struct root { uint8 kind; struct { uint8 n; uint8 d[n]; }; uint8 tail; };");
        var value = new PromotedRoot { Kind = 1, N = 2, D = [7, 8], Tail = 9, };
        foreach ((string operation, OperationOutcome[] outcomes) in WriteEverywhere(layout, value, 5))
        {
            for (int index = 0; index < outcomes.Length; index++)
            {
                Assert.IsNull(outcomes[index].Failure, operation + " (" + Paths[index] + "): " + outcomes[index].Failure?.Message);
                CollectionAssert.AreEqual(new byte[] { 1, 2, 7, 8, 9, }, (byte[])outcomes[index].Result!, operation + " (" + Paths[index] + ")");
            }
        }
    }

    /// <summary>
    ///     A mapped <see cref="SharpEdgeOptionTests.InnerPoco"/> nested in a dictionary or <see cref="StructValue"/> root
    ///     - as a member and as array elements - writes the same bytes as the equivalent dictionary on every path, and
    ///     an update that writes a mapped instance into a nested struct changes the same bytes.
    /// </summary>
    /// <param name="name">The case name.</param>
    /// <param name="definition">The layout source.</param>
    [TestMethod]
    [DynamicData(nameof(NestedMappedLayouts))]
    public void NestedMappedInstance_WritesOnEveryPath(string name, string definition)
    {
        var layout = new CStruct(definition);
        bool counted = definition.Contains("uint8 n;", StringComparison.Ordinal);
        byte[] expected = counted ? [1, 0, 5, 2, 6, 7] : [1, 0, 5, 6, 7];
        var mappedDictionary = new Dictionary<string, object?>
        {
            ["kind"] = (ushort)1,
            ["nested"] = new SharpEdgeOptionTests.InnerPoco { A = 5, },
            ["items"] = new object[] { new SharpEdgeOptionTests.InnerPoco { A = 6, }, new SharpEdgeOptionTests.InnerPoco { A = 7, }, },
        };
        if (counted)
        {
            mappedDictionary["n"] = (byte)2;
        }

        StructValue mappedStructValue = layout.Parse(expected.AsSpan(), "root");
        mappedStructValue["nested"] = new SharpEdgeOptionTests.InnerPoco { A = 5, };
        mappedStructValue["items"] = new List<object?> { new SharpEdgeOptionTests.InnerPoco { A = 6, }, new SharpEdgeOptionTests.InnerPoco { A = 7, }, };

        var unexpected = new List<string>();
        foreach ((string form, object value) in new (string, object)[] { ("dictionary", mappedDictionary), ("StructValue", mappedStructValue), })
        {
            foreach (UnknownMemberPolicy policy in (UnknownMemberPolicy[])[UnknownMemberPolicy.Ignore, UnknownMemberPolicy.Reject])
            {
                foreach ((string operation, OperationOutcome[] outcomes) in WriteEverywhere(layout, value, expected.Length, policy))
                {
                    for (int index = 0; index < outcomes.Length; index++)
                    {
                        if (outcomes[index].Failure is { } failure)
                        {
                            unexpected.Add(form + ", " + policy + ", " + operation + " (" + Paths[index] + "): " + failure.Message);
                        }
                        else
                        {
                            CollectionAssert.AreEqual(expected, (byte[])outcomes[index].Result!, name + ", " + form + ", " + policy + ", " + operation + " (" + Paths[index] + ")");
                        }
                    }
                }
            }
        }

        foreach (ExecutionPath path in Paths)
        {
            OperationOutcome update = OperationOutcome.Of(() =>
            {
                byte[] copy = (byte[])expected.Clone();
                layout.Update(copy.AsSpan(), "root.nested", new SharpEdgeOptionTests.InnerPoco { A = 0x42, }, null, new UpdateOptions { ExecutionPath = path, });
                return copy;
            });
            if (update.Failure is { } failure)
            {
                unexpected.Add("Update nested (" + path + "): " + failure.Message);
            }
            else
            {
                byte[] changed = (byte[])expected.Clone();
                changed[2] = 0x42;
                CollectionAssert.AreEqual(changed, (byte[])update.Result!, name + ", Update nested (" + path + ")");
            }
        }

        Assert.AreEqual(0, unexpected.Count, name + ":\n" + string.Join("\n", unexpected));
    }

    /// <summary>Copies a parsed value's members into a plain dictionary, nested values included.</summary>
    /// <param name="value">The parsed value.</param>
    /// <returns>The dictionary.</returns>
    private static Dictionary<string, object?> ToDictionary(StructValue value)
    {
        var result = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, object?> member in value)
        {
            result[member.Key] = member.Value is StructValue nested ? ToDictionary(nested) : member.Value;
        }

        return result;
    }

    /// <summary>Writes one value to a new array, a span and a stream, on every execution path.</summary>
    /// <param name="layout">The compiled layout.</param>
    /// <param name="value">The root value.</param>
    /// <param name="length">The encoded length.</param>
    /// <param name="policy">The unknown-member policy.</param>
    /// <returns>Each operation's name and its outcome per path, in <see cref="Paths"/> order; a result is the written bytes.</returns>
    private static IEnumerable<(string Operation, OperationOutcome[] Outcomes)> WriteEverywhere(CStruct layout, object value, int length, UnknownMemberPolicy policy = UnknownMemberPolicy.Reject)
    {
        // Each write gets fresh options for its path.
        WriteOptions Options(ExecutionPath path) => new() { UnknownMembers = policy, ExecutionPath = path, };

        var operations = new (string Name, Func<ExecutionPath, object?> Run)[]
        {
            ("Serialize", path => layout.Serialize("root", value, null, Options(path))),
            ("Serialize span", path =>
            {
                byte[] destination = new byte[length + 3];
                int written = layout.Serialize(destination.AsSpan(), "root", value, null, Options(path));
                return destination[..written];
            }),
            ("Write", path =>
            {
                using var stream = new MemoryStream();
                layout.Write(stream, "root", value, null, Options(path));
                return stream.ToArray();
            }),
        };
        foreach ((string operation, Func<ExecutionPath, object?> run) in operations)
        {
            yield return (operation, Paths.Select(path => OperationOutcome.Of(() => run(path))).ToArray());
        }
    }

    /// <summary>
    ///     A mapped class for <c>struct root { uint8 kind; struct { uint8 n; uint8 d[n]; }; uint8 tail; }</c>: the
    ///     promoted struct's members <c>n</c> and <c>d</c> are properties of the root.
    /// </summary>
    internal sealed class PromotedRoot : ICStructMapped<PromotedRoot>
    {
        /// <summary>Gets or sets the <c>kind</c> field.</summary>
        public byte Kind { get; set; }

        /// <summary>Gets or sets the promoted <c>n</c> field.</summary>
        public byte N { get; set; }

        /// <summary>Gets or sets the promoted <c>d</c> array.</summary>
        public byte[] D { get; set; } = [];

        /// <summary>Gets or sets the <c>tail</c> field.</summary>
        public byte Tail { get; set; }

        /// <summary>Builds the class from a parsed record.</summary>
        /// <param name="source">The parsed record.</param>
        /// <returns>The mapped value.</returns>
        public static PromotedRoot ReadFrom(StructValue source)
        {
            return new PromotedRoot { Kind = source.Get<byte>("kind"), N = source.Get<byte>("n"), D = source.Get<byte[]>("d"), Tail = source.Get<byte>("tail"), };
        }

        /// <summary>Copies the class into a record to write.</summary>
        /// <param name="value">The mapped value.</param>
        /// <param name="target">The record to fill.</param>
        public static void WriteTo(PromotedRoot value, StructValue target)
        {
            target["kind"] = value.Kind;
            target["n"] = value.N;
            target["d"] = value.D;
            target["tail"] = value.Tail;
        }

        /// <summary>Registers the mapping when the test assembly loads.</summary>
        [ModuleInitializer]
        internal static void Register()
        {
            MappedTypes.Register<PromotedRoot>();
        }
    }
}
