namespace CStructSharp.Tests;

using System.Text;
using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Engine;
using CStructSharp.Expressions;
using CStructSharp.Syntax;

/// <summary>
///     The slot table (<see cref="SlotTable"/>) and the per-operation slots (<see cref="VariableSlots"/>): which names
///     get slots, the precomputed dependents and static state, lazy thread-safe construction, and the allocation of the
///     common path.
/// </summary>
[TestClass]
public class SlotTableTests
{
    /// <summary>
    ///     Constructing a layout does not build its slot table; the first access builds it once (the first whole-root
    ///     read, which the compiled engine runs with it, or a direct access), and construction allocates no more than
    ///     without it.
    /// </summary>
    [TestMethod]
    [DoNotParallelize]
    [TestCategory(TestCategories.Allocation)]
    public void SlotTable_IsBuiltLazily_AndNotByConstruction()
    {
        string definition = PrimitiveScopeDefinition(200);
        for (int repeat = 0; repeat < 8; repeat++)
        {
            GC.KeepAlive(new CStruct(definition));
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        var layout = new CStruct(definition);
        long construction = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsFalse(layout.Compilation.HasSlotTable);

        before = GC.GetAllocatedBytesForCurrentThread();
        SlotTable table = layout.Compilation.SlotTable;
        long build = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.IsTrue(layout.Compilation.HasSlotTable);
        Assert.AreSame(table, layout.Compilation.SlotTable);
        _ = layout.Parse(new byte[2 + (200 * 4)], "root");
        Assert.AreSame(table, layout.Compilation.SlotTable, "a read uses the table already built");

        var read = new CStruct(definition);
        _ = read.Parse(new byte[2 + (200 * 4)], "root");
        Assert.IsTrue(read.Compilation.HasSlotTable, "the first whole-root read builds the table the engine reads with");

        // The bound of ConditionalExecutionOptimizationTests.PrimitiveScopeMetadata_HasBoundedAllocation for this shape;
        // building the table is a separate, later cost.
        Assert.IsLessThanOrEqualTo(400_000, construction, $"construction allocated {construction} bytes");
        Assert.IsGreaterThan(0, build);
    }

    /// <summary>Threads that race to build the table all receive the same published instance.</summary>
    [TestMethod]
    public void SlotTable_RacingThreads_ShareOneInstance()
    {
        var layout = new CStruct("#define N (n * 2)\nstruct root { uint8 n; uint8 v[N]; };");
        var tables = new SlotTable[8];
        using var start = new Barrier(tables.Length);
        Parallel.For(0, tables.Length, new ParallelOptions { MaxDegreeOfParallelism = tables.Length, }, index =>
        {
            start.SignalAndWait();
            tables[index] = layout.Compilation.SlotTable;
        });

        foreach (SlotTable table in tables)
        {
            Assert.AreSame(tables[0], table);
        }
    }

    /// <summary>
    ///     Every name an expression can read has a slot - referenced fields, dotted names and their remainders,
    ///     definitions, qualified enum members and the names definitions read - in ordinal order, and nothing else.
    /// </summary>
    [TestMethod]
    public void Slots_CoverEveryReadableName()
    {
        const string definition = """
            enum Color : uint8 { Red = 1, Green = 2 };
            #define MAX 8
            #define TWICE (count * 2)
            struct header { uint8 n; uint8 unused; };
            struct root
            {
                header hdr;
                uint8 count;
                uint8 other;
                uint8 values[hdr.n + TWICE];
                if (count == Color.Green) { uint8 extra; }
            };
            """;
        SlotTable table = new CStruct(definition).Compilation.SlotTable;
        string[] names = Enumerable.Range(0, table.Count).Select(table.GetName).ToArray();

        CollectionAssert.AreEqual(names.Order(StringComparer.Ordinal).ToArray(), names);
        foreach (string name in new[] { "hdr.n", "n", "count", "MAX", "TWICE", "Color.Green", "Color.Red", })
        {
            Assert.IsTrue(table.TryGetSlot(name, out _), name + " has no slot");
        }

        foreach (string name in new[] { "other", "unused", "extra", "hdr", "values", })
        {
            Assert.IsFalse(table.TryGetSlot(name, out int slot), name + " has a slot");
            Assert.AreEqual(-1, slot);
        }
    }

    /// <summary>
    ///     Each slot records the definitions that depend on it transitively, and the static state holds folded
    ///     definitions as literals, definitions that name fields as live expressions, and fields as undefined.
    /// </summary>
    [TestMethod]
    public void Slots_RecordDependentsAndTheStaticState()
    {
        const string definition = "#define A 3\n#define B (A + 1)\n#define D (n * B)\n#define E (D + 1)\nstruct root { uint8 n; uint8 v[E]; };";
        LayoutCompilation compilation = new CStruct(definition).Compilation;
        SlotTable table = compilation.SlotTable;

        // Returns a name's slot.
        int Slot(string name)
        {
            Assert.IsTrue(table.TryGetSlot(name, out int slot), name);
            return slot;
        }

        // Returns the names of a slot's dependents.
        string[] Dependents(string name) => table.GetDependentDefinitions(Slot(name)).Select(table.GetName).Order(StringComparer.Ordinal).ToArray();

        CollectionAssert.AreEqual(new[] { "B", "D", "E", }, Dependents("A"));
        CollectionAssert.AreEqual(new[] { "D", "E", }, Dependents("n"));
        CollectionAssert.AreEqual(new[] { "E", }, Dependents("D"));
        CollectionAssert.AreEqual(Array.Empty<string>(), Dependents("E"));
        Assert.IsTrue(table.HasStaticState);

        using VariableSlots slots = VariableSlots.Create(table, null);
        Assert.AreSame(table, slots.Table);
        Assert.ThrowsExactly<ArgumentNullException>(() => table.Compile(null!));
        Assert.AreEqual(SlotState.Literal, slots.Get(Slot("B")).State);
        Assert.AreEqual((Int128)4, slots.Get(Slot("B")).Value);
        Assert.AreEqual(SlotState.LiveExpression, slots.Get(Slot("D")).State);
        Assert.AreEqual(SlotState.LiveExpression, slots.Get(Slot("E")).State);
        Assert.AreEqual(SlotState.Undefined, slots.Get(Slot("n")).State);
        Assert.IsTrue(((ProgramExpression)slots.Get(Slot("E")).Payload!).IsNative);
        CollectionAssert.AreEqual(new[] { Slot("D"), }, ((ProgramExpression)slots.Get(Slot("E")).Payload!).PreludeSlots.ToArray());
    }

    /// <summary>
    ///     A layout whose definitions fail for every operation without caller variables has no static state, and every
    ///     operation fails as the resolver does; a caller value that makes the definitions resolvable succeeds.
    /// </summary>
    [TestMethod]
    public void UnresolvableStaticState_FailsLikeTheResolver()
    {
        var chain = new StringBuilder("#define A0 (n + 1 + 1)\n");
        for (int index = 1; index < 6; index++)
        {
            chain.Append("#define A").Append(index).Append(" (A").Append(index - 1).Append(" + 1 + 1)\n");
        }

        var options = new CStructCompilationOptions { MaxExpressionTokens = 12, };
        LayoutCompilation compilation = new CStruct(chain + "struct root { uint8 v[A5]; };", compilationOptions: options).Compilation;
        SlotTable table = compilation.SlotTable;
        Assert.IsFalse(table.HasStaticState);

        string expected = ExpressionDifferential.Outcome(() => compilation.LayoutVariableResolver.CreateIntegers(null));
        string actual = ExpressionDifferential.Outcome(() =>
        {
            using VariableSlots slots = VariableSlots.Create(table, null);
            return null;
        });
        StringAssert.Contains(expected, "Maximum expression evaluation work exceeded.");
        Assert.AreEqual(expected, actual);
    }

    /// <summary>Maps a conditional composite's scope names to slots, with -1 for names no expression reads.</summary>
    [TestMethod]
    public void MapNames_MapsConditionalScopeNames()
    {
        var layout = new CStruct("struct root { uint8 kind; uint8 other; if (kind == 1) { uint8 value; } };");
        SlotTable table = layout.Compilation.SlotTable;
        var composite = (CompiledCompositeType)layout.Compilation.CompiledModel.Symbols["root"].Symbol.Definition!;
        IReadOnlyList<string> names = composite.ConditionalScope!.LocalNames;
        int[] mapped = table.MapNames(names);

        Assert.AreEqual(names.Count, mapped.Length);
        for (int index = 0; index < names.Count; index++)
        {
            Assert.AreEqual(names[index] == "kind" ? table.MapNames(["kind"])[0] : -1, mapped[index], names[index]);
        }

        Assert.AreNotEqual(-1, table.MapNames(["kind"])[0]);
    }

    /// <summary>
    ///     The common path allocates nothing: creating the slots without caller variables (once the pool holds an
    ///     array), capturing literals, and evaluating leaf-safe programs, including their failures' absence.
    /// </summary>
    [TestMethod]
    [DoNotParallelize]
    [TestCategory(TestCategories.Allocation)]
    public void LeafPath_AndSlotCreation_AllocateNothing()
    {
        var layout = new CStruct("struct root { uint8 kind; uint16 count; if (kind == 1) { uint8 a[count * 4 + 1]; } else { uint8 b[count ? count - 1 : 0]; } };");
        SlotTable table = layout.Compilation.SlotTable;
        var composite = (CompiledCompositeType)layout.Compilation.CompiledModel.Symbols["root"].Symbol.Definition!;
        ProgramExpression selector = table.Compile(composite.Fields[2].ConditionalBranches[0].Group.Selector);
        ProgramExpression first = table.Compile(composite.Fields[2].Array.CountExpression!);
        ProgramExpression second = table.Compile(composite.Fields[3].Array.CountExpression!);
        Assert.IsTrue(selector.IsLeafSafe && first.IsLeafSafe && second.IsLeafSafe);
        Assert.IsTrue(table.TryGetSlot("kind", out int kind));
        Assert.IsTrue(table.TryGetSlot("count", out int count));

        // Runs one operation's worth of work: create, capture, evaluate, release.
        int Run(int value)
        {
            using VariableSlots slots = VariableSlots.Create(table, null);
            slots.Set(kind, SlotValue.FromLiteral(value & 1));
            slots.Set(count, SlotValue.FromLiteral(value));
            int total = (int)slots.Evaluate(selector, "selector", ExpressionFailureDomain.Read);
            total += slots.EvaluateInt32(first, "a", ExpressionFailureDomain.Read);
            return total + slots.EvaluateInt32(second, "b", ExpressionFailureDomain.Read);
        }

        int warm = 0;
        for (int index = 0; index < 100; index++)
        {
            warm += Run(index);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        int sum = 0;
        for (int index = 0; index < 1000; index++)
        {
            sum += Run(index);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.AreEqual(0, allocated);
        Assert.AreNotEqual(0, sum + warm);
    }

    /// <summary>
    ///     Caller variables that no definition depends on overwrite their slots directly; one a definition depends on
    ///     makes the state the resolver's, with the dependent folded when the operation starts; one without a slot is kept
    ///     beside the slots, for the count of a root spelled at run time.
    /// </summary>
    [TestMethod]
    public void CallerVariables_OverwriteOrUnfoldDependents()
    {
        var layout = new CStruct("#define D (n * 2)\nstruct root { uint8 n; uint8 m; uint8 v[D + m]; };");
        SlotTable table = layout.Compilation.SlotTable;
        Assert.IsTrue(table.TryGetSlot("D", out int d));
        Assert.IsTrue(table.TryGetSlot("m", out int m));

        using (VariableSlots slots = VariableSlots.Create(table, new Dictionary<string, int> { ["m"] = 5, ["unrelated"] = 1, }))
        {
            Assert.AreEqual(SlotValue.FromLiteral(5).Value, slots.Get(m).Value);
            Assert.AreEqual(SlotState.LiveExpression, slots.Get(d).State);
            Assert.AreEqual<Syntax.Expr>(new Syntax.Literal(1), slots.ToDictionary()["unrelated"], "a caller variable without a slot is kept beside the slots, for an expression that names it");
        }

        using (VariableSlots slots = VariableSlots.Create(table, new Dictionary<string, int> { ["n"] = 21, }))
        {
            Assert.AreEqual(SlotState.Literal, slots.Get(d).State);
            Assert.AreEqual((Int128)42, slots.Get(d).Value);
        }
    }

    /// <summary>
    ///     The dictionary view of the slots (<see cref="VariableSlots.AsDictionary"/>) holds exactly the entries the
    ///     materialized dictionary holds: a defined slot's value, no entry for an undefined slot, a caller variable without a
    ///     slot, and nothing else; a missing name fails the indexer, and an enumeration lists the same entries.
    /// </summary>
    [TestMethod]
    public void DictionaryView_HoldsTheMaterializedEntries()
    {
        var layout = new CStruct("struct root { uint8 n; uint8 m; uint8 v[n + m]; };");
        using VariableSlots slots = VariableSlots.Create(layout.Compilation.SlotTable, new Dictionary<string, int> { ["m"] = 5, ["unrelated"] = 1, });
        IReadOnlyDictionary<string, Syntax.Expr> view = slots.AsDictionary();
        Dictionary<string, Syntax.Expr> materialized = slots.ToDictionary();

        Assert.IsTrue(view.TryGetValue("m", out Syntax.Expr? m));
        Assert.AreEqual<Syntax.Expr>(new Syntax.Literal(5), m);
        Assert.IsFalse(view.TryGetValue("n", out _), "an undefined slot has no entry");
        Assert.AreEqual<Syntax.Expr>(new Syntax.Literal(1), view["unrelated"], "a caller variable without a slot is an entry");
        Assert.IsFalse(view.ContainsKey("missing"));
        Assert.IsTrue(view.ContainsKey("m"));
        Assert.Throws<KeyNotFoundException>(() => view["n"]);

        Assert.AreEqual(materialized.Count, view.Count);
        CollectionAssert.AreEquivalent(materialized.Keys.ToArray(), view.Keys.ToArray());
        CollectionAssert.AreEquivalent(materialized.Values.ToArray(), view.Values.ToArray());
        CollectionAssert.AreEquivalent(materialized.ToArray(), view.ToArray());
        Assert.AreEqual(materialized.Count, ((System.Collections.IEnumerable)view).Cast<object>().Count());
    }

    /// <summary>The layout of the bounded-allocation test: a tag and <paramref name="count"/> fields in one conditional group.</summary>
    /// <param name="count">The number of fields.</param>
    /// <returns>The layout text.</returns>
    private static string PrimitiveScopeDefinition(int count)
    {
        var source = new StringBuilder("struct root { uint8 tag; if (tag == 1) { ");
        for (int index = 0; index < count; index++)
        {
            source.Append("uint32 f").Append(index).Append(';');
        }

        return source.Append("} };").ToString();
    }
}
