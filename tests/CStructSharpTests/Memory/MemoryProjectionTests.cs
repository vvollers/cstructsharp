namespace CStructSharp.Tests;

using System.Buffers.Binary;
using System.Text;
using CStructSharp.Diagnostics;
using CStructSharp.Memory;
using CStructSharp.Memory.Metadata;

/// <summary>Exercises immutable, field-directed ISF/BTF imports with native placement, availability and validation scope.</summary>
[TestClass]
public class MemoryProjectionTests
{
    /// <summary>Unrequested malformed dependencies stay outside validation and are distinguishable from missing metadata.</summary>
    [TestMethod]
    public void Isf_LeavesUnselectedTargetsExplicitAndDoesNotMutateEarlierSchemas()
    {
        var metadata = Isf();
        MetadataImportResult first = metadata.ImportProjection("root", ["pid", "next", "ignored"]);
        Assert.AreEqual(80, first.Schema.GetType(first.RootTypeId).Size);
        Assert.IsTrue(first.Schema.GetType(first.RootTypeId).IsProjected);
        Assert.IsTrue(first.Schema.GetType("isf:user:child").IsUnselected);
        Assert.IsTrue(first.Schema.GetType("isf:user:broken").IsUnselected);
        CollectionAssert.AreEqual(new[] { "items", "choice", "anonymous", }, first.Schema.GetType(first.RootTypeId).UnselectedMembers.ToArray());
        Assert.AreEqual(0, first.Diagnostics.Count);
        Assert.IsFalse(first.Schema.TrySelect(first.RootTypeId, "next.value.value", out _, out MemoryReadFailure? target));
        Assert.AreEqual(MemoryReadFailureKind.UnselectedType, target!.Kind);
        Assert.IsFalse(first.Schema.TryBind<uint>(first.RootTypeId, "items[0].value", out _, out MemoryReadFailure? member));
        Assert.AreEqual(MemoryReadFailureKind.UnselectedMember, member!.Kind);

        MetadataImportResult second = metadata.ImportProjection("root", ["next.value.value"]);
        Assert.IsFalse(second.Schema.GetType("isf:user:child").IsUnselected);
        Assert.IsTrue(first.Schema.GetType("isf:user:child").IsUnselected);

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<CStructLayoutException>(() => metadata.Import("root"));

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<CStructLayoutException>(() => metadata.ImportProjection("root", ["ignored.value.bad"]));
        Assert.IsTrue(first.Schema.GetType("isf:user:broken").IsUnselected);
    }

    /// <summary>Multiple paths share one closure, including pointer targets, array stride, unions and anonymous promotion.</summary>
    [TestMethod]
    public void Isf_ProjectsNestedPathsWithFullOffsetsAndTypedValues()
    {
        var metadata = Isf();
        string[] paths = ["pid", "next.value.value", "items[1].other", "choice.word", "promotedValue", "pid"];
        MetadataImportResult result = metadata.ImportProjection("root", paths);
        Assert.AreEqual(5, result.SelectedPaths!.Count);
        paths[0] = "changed";
        Assert.AreEqual("pid", result.SelectedPaths[0]);
        Assert.AreEqual(36L, result.Schema.Select(result.RootTypeId, "items[1].other").Offset);
        Assert.AreEqual(56L, result.Schema.Select(result.RootTypeId, "promotedValue").Offset);
        Assert.IsNull(result.Schema.Select(result.RootTypeId, "next.value.value").Offset);
        byte[] bytes = new byte[112];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 7);
        BinaryPrimitives.WriteUInt64LittleEndian(bytes.AsSpan(8), 96);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(36), 22);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(48), 33);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(56), 44);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(96), 55);
        var region = new MemoryRegion(new BorrowedMemorySource("projection", bytes), 0, 80);
        var session = new MemorySession(result.Schema);
        Assert.AreEqual(7U, result.Schema.Bind<uint>(result.RootTypeId, "pid").Read(session, region));
        Assert.AreEqual(22U, result.Schema.Bind<uint>(result.RootTypeId, "items[1].other").Read(session, region));
        Assert.AreEqual(33U, result.Schema.Bind<uint>(result.RootTypeId, "choice.word").Read(session, region));
        Assert.AreEqual(44U, result.Schema.Bind<uint>(result.RootTypeId, "promotedValue").Read(session, region));
        Assert.AreEqual(55U, result.Schema.Bind<uint>(result.RootTypeId, "next.value.value").Read(session, region));
        Assert.Throws<CStructWriteException>(() => session.Serialize(result.RootTypeId, new Dictionary<string, object?> { ["pid"] = 1U, }));
        Assert.AreEqual(7U, session.Read(region, result.RootTypeId, "pid"));
        Assert.IsFalse(result.Schema.TrySelect("isf:user:broken", string.Empty, out _, out _));
    }

    /// <summary>Terminal value selection includes embedded fields while storing pointers without walking their unrelated target graphs.</summary>
    [TestMethod]
    public void Isf_TerminalCompositeSelectionIncludesByValueClosure()
    {
        var metadata = Isf();
        MetadataImportResult result = metadata.ImportProjection("root", ["items", "choice"]);
        Assert.AreEqual(3, result.Schema.GetType("isf:user:child").Fields.Count);
        Assert.AreEqual(2, result.Schema.GetType("isf:user:choice").Fields.Count);
        Assert.AreEqual(0, result.Schema.GetType("isf:user:child").UnselectedMembers.Count);
        Assert.IsFalse(result.Schema.Types.ContainsKey("isf:user:broken"));
        var session = new MemorySession(result.Schema);
        byte[] value = session.Serialize("isf:user:child", new Dictionary<string, object?> { ["value"] = 1U, ["other"] = 2U, ["next"] = new StoredPointer(0, 8), });
        Assert.AreEqual(16, value.Length);
    }

    /// <summary>Selected path validation still rejects ambiguity, unavailable targets and invalid array traversal.</summary>
    [TestMethod]
    public void Isf_RejectsInvalidPathsAndRetainsSelectedPlacementValidation()
    {
        var metadata = Isf();
        foreach (string path in new[] { "absent", "items[2].value", "pid.value", "choice.nope", "next.value.absent", })
        {
            // Import must preserve the selected path, metadata or resource-limit failure category.
            Assert.Throws<CStructPathException>(() => metadata.ImportProjection("root", [path]));
        }

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<CStructPathException>(() => metadata.ImportProjection("ambiguous", ["promotedValue"]));

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<ArgumentException>(() => metadata.ImportProjection("root", []));

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<ArgumentNullException>(() => metadata.ImportProjection("root", null!));

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<ArgumentNullException>(() => metadata.ImportProjection("root", [null!]));

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<CStructLayoutException>(() => metadata.ImportProjection("missing", ["pid"]));

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<CStructLayoutException>(() => metadata.ImportProjection("badPlacement", ["value"]));
        MetadataImportResult bestEffort = metadata.ImportProjection("badPlacement", ["value"], new MetadataImportOptions { BestEffort = true, });
        Assert.AreEqual(MemoryTypeKind.RawBytes, bestEffort.Schema.GetType(bestEffort.RootTypeId).Kind);
        Assert.IsTrue(bestEffort.Schema.GetType(bestEffort.RootTypeId).IsProjected);
        Assert.AreEqual(1, bestEffort.Diagnostics.Count);
    }

    /// <summary>Document-owned compilation reuse does not share mutable projection masks, budgets or cancellation state.</summary>
    [TestMethod]
    public void Isf_ProjectionBudgetsCancellationAndConcurrentSelectionsAreIndependent()
    {
        var metadata = Isf();
        _ = metadata.ImportProjection("root", ["pid"]);
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<OperationCanceledException>(() => metadata.ImportProjection("root", ["pid"], cancellationToken: canceled.Token));

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<CStructLayoutException>(() => metadata.ImportProjection("root", ["pid", "next"], new MetadataImportOptions { MaxTypes = 1, }));

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<ArgumentOutOfRangeException>(() => metadata.ImportProjection("root", ["pid"], new MetadataImportOptions { PointerSize = 3, }));

        // Parallel callers must never see one another's selected-member sets or mutable contexts.
        Parallel.For(0, 16, index =>
        {
            string path = index % 2 == 0 ? "pid" : "items[0].other";
            MetadataImportResult result = metadata.ImportProjection("root", [path]);
            Assert.AreEqual(1, result.Schema.GetType(result.RootTypeId).Fields.Count);
            Assert.IsTrue(result.Schema.TryBind<uint>(result.RootTypeId, path, out _, out _));
        });
    }

    /// <summary>Whole selected values agree with full imports and share successful scalar compilation, while partial reads do not invent omitted values.</summary>
    [TestMethod]
    public void Isf_FullValueParityAndPartialReadOwnership()
    {
        var metadata = Isf();
        MetadataImportResult full = metadata.Import("child");
        MetadataImportResult whole = metadata.ImportProjection("child", [string.Empty]);
        MemoryTypeDefinition scalar = full.Schema.GetType("isf:base:u32");
        Assert.AreSame(full.Schema.GetCodec(scalar).Layout, whole.Schema.GetCodec(whole.Schema.GetType(scalar.Id)).Layout);
        var values = new Dictionary<string, object?> { ["value"] = 42U, ["other"] = 7U, ["next"] = new StoredPointer(0xF000000000000001, 8), };
        byte[] bytes = new MemorySession(full.Schema).Serialize(full.RootTypeId, values);
        CollectionAssert.AreEqual(bytes, new MemorySession(whole.Schema).Serialize(whole.RootTypeId, values));

        MetadataImportResult partial = metadata.ImportProjection("child", ["value"]);
        var region = new MemoryRegion(new BorrowedMemorySource("prefix", bytes, readableLength: 4), 0, 16);
        var session = new MemorySession(partial.Schema);
        var read = (IReadOnlyDictionary<string, object?>)session.Read(region, partial.RootTypeId)!;
        Assert.AreEqual(1, read.Count);
        Assert.AreEqual(42U, read["value"]);
        Assert.IsFalse(read.ContainsKey("other"));
        Assert.IsNotNull(session.PlanUpdate(region, partial.RootTypeId, "value", 99U));
        Assert.AreEqual(42U, session.Read(region, partial.RootTypeId, "value"));

        // Full inspection must still reject the unreadable suffix; projection cannot alter another schema.
        Assert.Throws<MemoryAccessException>(() => new MemorySession(full.Schema).Read(region, full.RootTypeId));
    }

    /// <summary>Selected signed bit slices, enums, byte order and pointer widths use the normal importer codec paths.</summary>
    [TestMethod]
    public void Isf_BitStorageDependenciesAndEnumsRetainNormalDecoding()
    {
        const string json = """
        {"metadata":{"format":"6.2.0"},"base_types":{"u32":{"kind":"int","size":4,"signed":false,"endian":"big"},"i32":{"kind":"int","size":4,"signed":true,"endian":"big"}},"enums":{"E":{"size":4,"base":"u32","constants":{"One":1}}},"user_types":{"root":{"kind":"struct","size":16,"fields":{
        "pointer":{"offset":0,"type":{"kind":"pointer","base":"u32","subtype":{"kind":"base","name":"u32"}}},
        "bits":{"offset":4,"type":{"kind":"bitfield","bit_position":0,"bit_length":4,"type":{"kind":"base","name":"i32"}}},
        "tag":{"offset":8,"type":{"kind":"enum","name":"E"}},
        "ignored":{"offset":12,"type":{"kind":"vector"}}
        }}}}
        """;
        var metadata = new IsfMetadata(Encoding.UTF8.GetBytes(json));
        MetadataImportResult result = metadata.ImportProjection("root", ["pointer", "bits", "tag"], new MetadataImportOptions { PointerSize = 4, });
        Assert.IsFalse(result.Schema.GetType("isf:base:u32").IsUnselected);
        byte[] bytes = new byte[16];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, 0xF0000001);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(4), 15);
        BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(8), 1);
        var session = new MemorySession(result.Schema);
        var region = new MemoryRegion(new BorrowedMemorySource("bits", bytes), 0, 16);
        Assert.AreEqual(-1L, session.Read(region, result.RootTypeId, "bits"));
        Assert.AreEqual(0xF0000001UL, result.Schema.Bind<StoredPointer>(result.RootTypeId, "pointer").Read(session, region).Address);
        Assert.AreEqual(1, ((CStructSharp.Values.EnumValueResult)session.Read(region, result.RootTypeId, "tag")!).Value);
    }

    /// <summary>Omitted malformed ordinary members are not parsed, but names, promotion cycles and requested descriptors remain checked.</summary>
    [TestMethod]
    public void Isf_NarrowValidationDoesNotTurnMalformedSelectedDataIntoAbsence()
    {
        const string json = """
        {"metadata":{"format":"6.2.0"},"base_types":{"u32":{"kind":"int","size":4,"signed":false,"endian":"little"}},"user_types":{
        "root":{"kind":"struct","size":16,"fields":{"value":{"offset":0,"type":{"kind":"base","name":"u32"}},"bad":null,"pointer":{"offset":8,"type":{"kind":"pointer","subtype":{"kind":"vector"}}}}},
        "cycle":{"kind":"struct","size":4,"fields":{"self":{"offset":0,"anonymous":true,"type":{"kind":"struct","name":"cycle"}}}}
        }}
        """;
        var metadata = new IsfMetadata(Encoding.UTF8.GetBytes(json));
        MetadataImportResult result = metadata.ImportProjection("root", ["value", "pointer"]);
        Assert.AreEqual(2, result.Schema.GetType(result.RootTypeId).Fields.Count);
        Assert.IsTrue(result.Schema.Types.Values.Any(type => type.IsUnselected));

        // Selected malformed members and malformed promotion candidates must produce metadata errors.
        Assert.Throws<CStructLayoutException>(() => metadata.ImportProjection("root", ["bad"]));

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<CStructLayoutException>(() => metadata.ImportProjection("root", ["pointer.value"]));

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<CStructLayoutException>(() => metadata.ImportProjection("cycle", ["absent"]));

        // Cancellation during caller enumeration remains terminating after the first path was prepared.
        using var cancellation = new CancellationTokenSource();

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<OperationCanceledException>(() => metadata.ImportProjection("root", CancelAfterFirstPath(cancellation), cancellationToken: cancellation.Token));
    }

    /// <summary>Enumerates one valid path, then cancels before the next planner step.</summary>
    /// <param name="cancellation">Operation-local cancellation source.</param>
    /// <returns>Two paths whose enumeration requests cancellation between them.</returns>
    private static IEnumerable<string> CancelAfterFirstPath(CancellationTokenSource cancellation)
    {
        yield return "value";
        cancellation.Cancel();
        yield return "pointer";
    }

    /// <summary>Equal anonymous descriptor text does not merge distinct metadata locations or widen another selected pointer.</summary>
    [TestMethod]
    public void Isf_AnonymousTargetsKeepOccurrenceIdentityAndRejectUnavailableValues()
    {
        const string json = """
        {"metadata":{"format":"6.2.0"},"base_types":{"u32":{"kind":"int","size":4,"signed":false,"endian":"little"},"void":{"kind":"void","size":0}},"user_types":{"root":{"kind":"struct","size":32,"fields":{
        "a":{"offset":0,"type":{"kind":"pointer","subtype":{"kind":"array","count":2,"subtype":{"kind":"base","name":"u32"}}}},
        "b":{"offset":8,"type":{"kind":"pointer","subtype":{"kind":"array","count":2,"subtype":{"kind":"base","name":"u32"}}}},
        "nothing":{"offset":16,"type":{"kind":"pointer","subtype":{"kind":"base","name":"void"}}},
        "badArray":{"offset":24,"type":{"kind":"array","count":-1,"subtype":{"kind":"base","name":"u32"}}}
        }}}}
        """;
        var metadata = new IsfMetadata(Encoding.UTF8.GetBytes(json));
        MetadataImportResult result = metadata.ImportProjection("root", ["a.value[1]", "b", "nothing.address"]);
        MemoryTypeDefinition root = result.Schema.GetType(result.RootTypeId);
        MemoryTypeDefinition firstPointer = result.Schema.GetType(root.Fields[0].TypeId);
        MemoryTypeDefinition secondPointer = result.Schema.GetType(root.Fields[1].TypeId);
        Assert.AreNotEqual(firstPointer.ElementTypeId, secondPointer.ElementTypeId);
        Assert.IsFalse(result.Schema.GetType(firstPointer.ElementTypeId!).IsUnselected);
        Assert.IsTrue(result.Schema.GetType(secondPointer.ElementTypeId!).IsUnselected);

        // A selected value must have a representation; opaque addresses do not fabricate void or malformed arrays.
        Assert.Throws<CStructPathException>(() => metadata.ImportProjection("root", ["nothing.value"]));

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<CStructLayoutException>(() => metadata.ImportProjection("root", ["badArray[0]"]));
    }

    /// <summary>Shared enum storage cannot depend on whether a pointer or its enum was imported first.</summary>
    /// <param name="enumFirst">Whether the enum precedes the pointer in native field order.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Isf_EnumStorageClosureIsIndependentOfFieldOrder(bool enumFirst)
    {
        const string prefix = """
        {"metadata":{"format":"6.2.0"},"base_types":{"u32":{"kind":"int","size":4,"signed":false,"endian":"little"}},"enums":{"E":{"base":"u32","size":4,"constants":{"One":1}}},"user_types":{"root":{"kind":"struct","size":16,"fields":{
        """;
        const string pointer = """ "pointer":{"offset":0,"type":{"kind":"pointer","subtype":{"kind":"base","name":"u32"}}}""";
        const string tag = """ "tag":{"offset":8,"type":{"kind":"enum","name":"E"}}""";
        string fields = enumFirst ? tag + "," + pointer : pointer + "," + tag;
        var metadata = new IsfMetadata(Encoding.UTF8.GetBytes(prefix + fields + "}}}}"));
        MetadataImportResult result = metadata.ImportProjection("root", ["pointer", "tag"]);
        Assert.IsFalse(result.Schema.GetType("isf:base:u32").IsUnselected);
        Assert.AreEqual(0, result.Diagnostics.Count);
    }

    /// <summary>Repeated anonymous subgraphs are searched once per type/name rather than once per exponentially many occurrence paths.</summary>
    [TestMethod]
    public void Isf_SharedPromotionDagHasBoundedSearchAndExplicitWorkLimits()
    {
        var json = new StringBuilder("""{"metadata":{"format":"6.2.0"},"base_types":{"u32":{"kind":"int","size":4,"signed":false,"endian":"little"}},"user_types":{""");
        for (int index = 0; index <= 30; index++)
        {
            if (index > 0)
            {
                json.Append(',');
            }

            json.Append("\"r").Append(index).Append("\":{\"kind\":\"union\",\"size\":4,\"fields\":{");
            if (index == 30)
            {
                json.Append("\"value\":{\"offset\":0,\"type\":{\"kind\":\"base\",\"name\":\"u32\"}}");
            }
            else
            {
                json.Append("\"left\":{\"offset\":0,\"anonymous\":true,\"type\":{\"kind\":\"union\",\"name\":\"r").Append(index + 1)
                    .Append("\"}},\"right\":{\"offset\":0,\"anonymous\":true,\"type\":{\"kind\":\"union\",\"name\":\"r").Append(index + 1).Append("\"}}");
            }

            json.Append("}}");
        }

        json.Append("}}");
        var metadata = new IsfMetadata(Encoding.UTF8.GetBytes(json.ToString()));
        var options = new MetadataImportOptions { MaxTypes = 64, MaxProjectionWork = 500, };

        // Absence and ambiguity must be resolved within this small work bound, despite over a billion occurrence paths.
        Assert.Contains("absent", Assert.Throws<CStructPathException>(() => metadata.ImportProjection("r0", ["absent"], options)).Message);

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Contains("Ambiguous", Assert.Throws<CStructPathException>(() => metadata.ImportProjection("r0", ["value"], options)).Message);

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<CStructLayoutException>(() => metadata.ImportProjection("r0", ["absent"], options with { MaxProjectionWork = 8, }));

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<ArgumentOutOfRangeException>(() => metadata.ImportProjection("r0", ["left"], options with { MaxProjectionWork = 0, }));
    }

    /// <summary>Path text and token counts are rejected before allocating or caching an ordinary parser's full token graph.</summary>
    [TestMethod]
    public void Isf_PathPlanningLimitsApplyBeforeOrdinaryParsing()
    {
        var metadata = Isf();
        string longPath = string.Join('.', Enumerable.Repeat("next", 2048));

        // A tiny text-work allowance wins before parsing can reach its separate nesting-depth diagnostic.
        Assert.Throws<CStructLayoutException>(() => metadata.ImportProjection("root", [longPath], new MetadataImportOptions { MaxProjectionWork = 8, }));

        // With enough text work, the bounded scan rejects excessive member/index counts before parser allocation.
        Assert.Throws<CStructPathException>(() => metadata.ImportProjection("root", [longPath]));
        Assert.Throws<CStructLayoutException>(() => metadata.ImportProjection("root", [new string(' ', 4096)], new MetadataImportOptions { MaxProjectionWork = 8, }));
    }

    /// <summary>Malformed promoted values cannot be silently treated as empty composites during a narrowed search.</summary>
    /// <param name="descriptor">An invalid anonymous promotion target.</param>
    [TestMethod]
    [DataRow("{\"kind\":\"base\",\"name\":\"u32\"}")]
    [DataRow("{\"kind\":\"pointer\",\"subtype\":{\"kind\":\"base\",\"name\":\"u32\"}}")]
    [DataRow("{\"kind\":\"array\",\"count\":1,\"subtype\":{\"kind\":\"base\",\"name\":\"u32\"}}")]
    [DataRow("{\"kind\":\"struct\",\"name\":\"undefined\"}")]
    public void Isf_RejectsMalformedPromotionCandidates(string descriptor)
    {
        const string json = """
        {"metadata":{"format":"6.2.0"},"base_types":{"u32":{"kind":"int","size":4,"signed":false,"endian":"little"}},"user_types":{"root":{"kind":"struct","size":16,"fields":{"pid":{"offset":0,"type":{"kind":"base","name":"u32"}},"bad":{"offset":8,"anonymous":true,"type":TARGET}}}}}
        """;
        var metadata = new IsfMetadata(Encoding.UTF8.GetBytes(json.Replace("TARGET", descriptor, StringComparison.Ordinal)));

        // Both a valid direct name and an absent name still examine competing promotion shapes.
        Assert.Throws<CStructLayoutException>(() => metadata.ImportProjection("root", ["pid"]));

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<CStructLayoutException>(() => metadata.ImportProjection("root", ["absent"]));
    }

    /// <summary>BTF's unnamed scalar, pointer, array and void members are not valid promoted composite shapes.</summary>
    [TestMethod]
    public void Btf_RejectsMalformedPromotionCandidates()
    {
        foreach (uint member in new uint[] { 1, 2, 7, 0, })
        {
            byte[] bytes = Btf(true);
            BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(24 + (47 * 4)), member);
            var metadata = new BtfMetadata(bytes);

            // Replacing a valid anonymous composite must produce a metadata error, not a missing-member outcome.
            Assert.Throws<CStructLayoutException>(() => metadata.ImportProjection(8, ["value"]));
        }
    }

    /// <summary>Both BTF byte orders preserve declared alias identities, array strides and explicitly unselected targets.</summary>
    /// <param name="littleEndian">Whether metadata and values use little-endian storage.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void Btf_ProjectsAliasesArraysAndPointerIdentities(bool littleEndian)
    {
        var metadata = new BtfMetadata(Btf(littleEndian));
        MetadataImportResult result = metadata.ImportProjection(11, ["value", "ignored", "choice.word"]);
        Assert.AreEqual("btf:11", result.RootTypeId);
        Assert.AreEqual(32, result.Schema.GetType(result.RootTypeId).Size);
        Assert.IsTrue(result.Schema.GetType("btf:5").IsUnselected);
        Assert.AreEqual(0, result.Diagnostics.Count);
        Assert.AreEqual("btf:5", result.Schema.GetType("btf:4").ElementTypeId);

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<CStructLayoutException>(() => metadata.Import(3));

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<CStructLayoutException>(() => metadata.ImportProjection(3, ["ignored.value"]));
        MetadataImportResult array = metadata.ImportProjection(7, ["[1].value"]);
        Assert.AreEqual(32L, array.Schema.Select(array.RootTypeId, "[1].value").Offset);
        byte[] bytes = new byte[64];
        Word(bytes.AsSpan(32), 42, littleEndian);
        Assert.AreEqual(42U, array.Schema.Bind<uint>(array.RootTypeId, "[1].value").Read(new MemorySession(array.Schema), new MemoryRegion(new BorrowedMemorySource("btf", bytes), 0, 64)));
    }

    /// <summary>BTF anonymous promotion retains all ambiguity candidates and supports direct pointer-address selection.</summary>
    [TestMethod]
    public void Btf_PreservesPromotionAmbiguityAndCallerLimits()
    {
        var metadata = new BtfMetadata(Btf(true));
        Assert.AreEqual(0L, metadata.ImportProjection(8, ["value"]).Schema.Select("btf:8", "value").Offset);

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<CStructPathException>(() => metadata.ImportProjection(10, ["value"]));

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<CStructPathException>(() => metadata.ImportProjection(7, ["[2].value"]));

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<CStructLayoutException>(() => metadata.ImportProjection(3, ["value"], new MetadataImportOptions { MaxTypes = 1, }));
        using var canceled = new CancellationTokenSource();
        canceled.Cancel();

        // Import must preserve the selected path, metadata or resource-limit failure category.
        Assert.Throws<OperationCanceledException>(() => metadata.ImportProjection(3, ["value"], cancellationToken: canceled.Token));
        MetadataImportResult address = metadata.ImportProjection(3, ["ignored.address"]);
        Assert.IsTrue(address.Schema.GetType("btf:5").IsUnselected);
        Assert.IsTrue(address.Schema.TryBind<StoredPointer>(address.RootTypeId, "ignored.address", out _, out _));
    }

    /// <summary>Builds synthetic metadata with valid selected layouts and a deliberately unsupported pointer-only graph.</summary>
    /// <returns>The parsed document shared by independent imports.</returns>
    private static IsfMetadata Isf()
    {
        const string json = """
        {"metadata":{"format":"6.2.0"},"base_types":{"u32":{"kind":"int","size":4,"signed":false,"endian":"little"},"u64":{"kind":"int","size":8,"signed":false,"endian":"little"}},"user_types":{
        "root":{"kind":"struct","size":80,"fields":{
        "pid":{"offset":0,"type":{"kind":"base","name":"u32"}},
        "next":{"offset":8,"type":{"kind":"pointer","subtype":{"kind":"struct","name":"child"}}},
        "items":{"offset":16,"type":{"kind":"array","count":2,"subtype":{"kind":"struct","name":"child"}}},
        "choice":{"offset":48,"type":{"kind":"union","name":"choice"}},
        "anonymous":{"offset":56,"anonymous":true,"type":{"kind":"struct","name":"promoted"}},
        "ignored":{"offset":64,"type":{"kind":"pointer","subtype":{"kind":"struct","name":"broken"}}}}},
        "child":{"kind":"struct","size":16,"fields":{"value":{"offset":0,"type":{"kind":"base","name":"u32"}},"other":{"offset":4,"type":{"kind":"base","name":"u32"}},"next":{"offset":8,"type":{"kind":"pointer","subtype":{"kind":"struct","name":"child"}}}}},
        "choice":{"kind":"union","size":8,"fields":{"word":{"offset":0,"type":{"kind":"base","name":"u32"}},"wide":{"offset":0,"type":{"kind":"base","name":"u64"}}}},
        "promoted":{"kind":"struct","size":4,"fields":{"promotedValue":{"offset":0,"type":{"kind":"base","name":"u32"}}}},
        "ambiguous":{"kind":"struct","size":8,"fields":{"left":{"offset":0,"anonymous":true,"type":{"kind":"struct","name":"promoted"}},"right":{"offset":4,"anonymous":true,"type":{"kind":"struct","name":"promoted"}}}},
        "broken":{"kind":"struct","size":8,"fields":{"bad":{"offset":0,"type":{"kind":"vector"}}}},
        "badPlacement":{"kind":"struct","size":4,"fields":{"value":{"offset":4,"type":{"kind":"base","name":"u32"}}}}
        }}
        """;
        return new IsfMetadata(Encoding.UTF8.GetBytes(json));
    }

    /// <summary>Encodes an independent BTF graph with aliases, arrays, unions, recursive pointers and anonymous members.</summary>
    /// <param name="littleEndian">Metadata byte order.</param>
    /// <returns>BTF v1 bytes.</returns>
    private static byte[] Btf(bool littleEndian)
    {
        string[] names = [string.Empty, "u32", "node", "value", "next", "ignored", "choice", "word", "wide", "alias"];
        var offsets = new Dictionary<string, uint>(StringComparer.Ordinal);
        var strings = new List<byte>();
        foreach (string name in names)
        {
            offsets.Add(name, (uint)strings.Count);
            strings.AddRange(Encoding.UTF8.GetBytes(name));
            strings.Add(0);
        }

        uint[] types = [
            offsets["u32"], 1U << 24, 4, 32,
            0, 2U << 24, 3,
            offsets["node"], (4U << 24) | 4, 32, offsets["value"], 1, 0, offsets["next"], 2, 64, offsets["ignored"], 4, 128, offsets["choice"], 6, 192,
            0, 2U << 24, 5,
            0, 16U << 24, 3,
            0, (5U << 24) | 2, 8, offsets["word"], 1, 0, offsets["wide"], 1, 0,
            0, 3U << 24, 0, 3, 1, 2,
            0, (4U << 24) | 1, 4, 0, 9, 0,
            0, (4U << 24) | 1, 4, offsets["value"], 1, 0,
            0, (4U << 24) | 2, 8, 0, 9, 0, 0, 9, 32,
            offsets["alias"], 8U << 24, 3,
        ];
        byte[] result = new byte[24 + (types.Length * 4) + strings.Count];
        result[0] = littleEndian ? (byte)0x9f : (byte)0xeb;
        result[1] = littleEndian ? (byte)0xeb : (byte)0x9f;
        result[2] = 1;
        uint[] header = [24, 0, (uint)(types.Length * 4), (uint)(types.Length * 4), (uint)strings.Count];
        for (int index = 0; index < header.Length; index++)
        {
            Word(result.AsSpan(4 + (index * 4)), header[index], littleEndian);
        }

        for (int index = 0; index < types.Length; index++)
        {
            Word(result.AsSpan(24 + (index * 4)), types[index], littleEndian);
        }

        strings.CopyTo(result, 24 + (types.Length * 4));
        return result;
    }

    /// <summary>Writes one independently encoded metadata or value word.</summary>
    /// <param name="bytes">At least four destination bytes.</param>
    /// <param name="value">Unsigned word.</param>
    /// <param name="littleEndian">Byte order.</param>
    private static void Word(Span<byte> bytes, uint value, bool littleEndian)
    {
        if (littleEndian)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        }
        else
        {
            BinaryPrimitives.WriteUInt32BigEndian(bytes, value);
        }
    }
}
