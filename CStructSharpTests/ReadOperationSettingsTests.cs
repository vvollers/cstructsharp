namespace CStructSharp.Tests;

/// <summary>
///     Exercises <see cref="ReadOperationSettings"/> directly, independent of a compiled <see cref="CStruct"/>
///     layout. Only reachable indirectly through the public API before this type was extracted from the God-Object
///     <c>CStruct</c> partial class.
/// </summary>
[TestClass]
public class ReadOperationSettingsTests
{
    /// <summary>Null options must snapshot to the exact documented defaults, not whatever ReadOptions itself defaults to.</summary>
    [TestMethod]
    public void SnapshotReadOptions_NullOptions_UsesDocumentedDefaults()
    {
        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(null);

        Assert.AreEqual(PointerAddressingMode.Absolute, settings.AddressingMode);
        Assert.IsTrue(settings.DereferencePointers);
        Assert.AreEqual(64, settings.MaxPointerDepth);
        Assert.IsNull(settings.MaxPointerTargetBytes);
        Assert.AreEqual(1_000_000, settings.MaxArrayElements);
        Assert.AreEqual(16 * 1024 * 1024, settings.MaxStringBytes);
        Assert.AreEqual(64 * 1024 * 1024, settings.MaxTotalBytesRead);
        Assert.AreEqual(256, settings.MaxNestingDepth);
        Assert.AreEqual(0, settings.Origin);
    }

    /// <summary>Every field of a caller-supplied ReadOptions must carry over unchanged, not just a subset.</summary>
    [TestMethod]
    public void SnapshotReadOptions_SuppliedOptions_CopiesEveryField()
    {
        var options = new ReadOptions
        {
            AddressingMode = PointerAddressingMode.Relative,
            DereferencePointers = false,
            MaxPointerDepth = 3,
            MaxPointerTargetBytes = 128,
            MaxArrayElements = 7,
            MaxStringBytes = 9,
            MaxTotalBytesRead = 11,
            MaxNestingDepth = 5,
            Origin = 42,
        };

        ReadOperationSettings settings = ReadOperationSettings.SnapshotReadOptions(options);

        Assert.AreEqual(PointerAddressingMode.Relative, settings.AddressingMode);
        Assert.IsFalse(settings.DereferencePointers);
        Assert.AreEqual(3, settings.MaxPointerDepth);
        Assert.AreEqual(128L, settings.MaxPointerTargetBytes);
        Assert.AreEqual(7, settings.MaxArrayElements);
        Assert.AreEqual(9L, settings.MaxStringBytes);
        Assert.AreEqual(11L, settings.MaxTotalBytesRead);
        Assert.AreEqual(5, settings.MaxNestingDepth);
        Assert.AreEqual(42L, settings.Origin);
    }

    /// <summary>
    ///     Update-path traversal limits (the Max*Traversal* family) must map onto the equivalent read-operation
    ///     limits, not silently fall back to unrelated write limits.
    /// </summary>
    [TestMethod]
    public void SnapshotTraversalOptions_MapsTraversalLimitsOntoReadOperationSettings()
    {
        var options = new UpdateOptions
        {
            AddressingMode = PointerAddressingMode.Relative,
            AllowPointerDereference = false,
            MaxTraversalPointerDepth = 2,
            MaxTraversalPointerTargetBytes = 64,
            MaxArrayElements = 13,
            MaxTraversalStringBytes = 17,
            MaxTraversalBytesRead = 19,
            MaxTraversalNestingDepth = 4,
            Origin = 99,
        };

        ReadOperationSettings settings = ReadOperationSettings.SnapshotTraversalOptions(options);

        Assert.AreEqual(PointerAddressingMode.Relative, settings.AddressingMode);
        Assert.IsFalse(settings.DereferencePointers);
        Assert.AreEqual(2, settings.MaxPointerDepth);
        Assert.AreEqual(64L, settings.MaxPointerTargetBytes);
        Assert.AreEqual(13, settings.MaxArrayElements);
        Assert.AreEqual(17L, settings.MaxStringBytes);
        Assert.AreEqual(19L, settings.MaxTotalBytesRead);
        Assert.AreEqual(4, settings.MaxNestingDepth);
        Assert.AreEqual(99L, settings.Origin);
    }
}
