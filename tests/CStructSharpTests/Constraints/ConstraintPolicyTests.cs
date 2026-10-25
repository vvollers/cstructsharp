namespace CStructSharp.Tests;

using System.Runtime.CompilerServices;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     <see cref="ConstraintPolicy.Skip"/> on the runtime layout: a read with the policy evaluates and collects no constraint,
///     whichever path reads it, and the policy travels through <see cref="ReadLimits"/> without growing it.
/// </summary>
[TestClass]
public class ConstraintPolicyTests
{
    private const string Fixed = "struct fx { uint32 a @expect(a != 0, \"fx.a\"); uint32 b @supports(b < 100, \"fx.b\"); };";

    private const string Nested = "struct inner { uint8 v @expect(v == 1); }; struct outer { inner first; uint8 n @expect(n != 0); uint8 data[n]; uint8 last @expect(last == 9); };";

    private static readonly ReadOptions Skip = new() { Constraints = ConstraintPolicy.Skip };

    /// <summary><see cref="ReadLimits"/> holds the policy and is still 40 bytes.</summary>
    [TestMethod]
    public void ReadLimits_StaysFortyBytes_AndCarriesThePolicy()
    {
        Assert.AreEqual(40, Unsafe.SizeOf<ReadLimits>());
        var limits = new ReadLimits { Constraints = ConstraintPolicy.Skip, InvalidText = InvalidTextHandling.Replace, TrimFixedText = true, OffsetBase = 7 };
        Assert.AreEqual(ConstraintPolicy.Skip, limits.Constraints);
        Assert.AreEqual(InvalidTextHandling.Replace, limits.InvalidText);
        Assert.IsTrue(limits.TrimFixedText);
        Assert.AreEqual(7, limits.OffsetBase);
        Assert.AreEqual(ConstraintPolicy.Enforce, default(ReadLimits).Constraints);
        Assert.AreNotEqual(limits, limits with { Constraints = ConstraintPolicy.Enforce });
        StringAssert.Contains(limits.ToString(), "Constraints = Skip");

        // The setters of the two flags do not disturb each other.
        ReadLimits reset = limits with { InvalidText = InvalidTextHandling.Fail };
        Assert.AreEqual(ConstraintPolicy.Skip, reset.Constraints);
        Assert.AreEqual(InvalidTextHandling.Replace, (reset with { InvalidText = InvalidTextHandling.Replace }).InvalidText);
        Assert.AreEqual(InvalidTextHandling.Replace, (limits with { Constraints = ConstraintPolicy.Enforce }).InvalidText);
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new ReadLimits { Constraints = (ConstraintPolicy)7 });
        Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _ = new ReadOptions { Constraints = (ConstraintPolicy)7 });
    }

    /// <summary>The policy is copied between <see cref="ReadOptions"/> and <see cref="ReadLimits"/> in both directions.</summary>
    [TestMethod]
    public void Policy_TravelsBetweenOptionsAndLimits()
    {
        Assert.AreEqual(ConstraintPolicy.Skip, ReadLimits.From(Skip).Constraints);
        Assert.AreEqual(ConstraintPolicy.Enforce, ReadLimits.From(new ReadOptions()).Constraints);
        Assert.AreEqual(ConstraintPolicy.Skip, new ReadLimits { Constraints = ConstraintPolicy.Skip }.ToOptions().Constraints);
        Assert.AreEqual(ConstraintPolicy.Enforce, new ReadOptions().Constraints);
        Assert.AreEqual(new ReadOptions(), new ReadOptions { Constraints = ConstraintPolicy.Enforce });
    }

    /// <summary>A value that breaks a fixed struct's conditions is read, by every runtime read path.</summary>
    [TestMethod]
    public void Skip_ReadsAViolatingFixedStruct()
    {
        var layout = new CStruct(Fixed);
        byte[] bytes = [0, 0, 0, 0, 200, 0, 0, 0];
        Assert.Throws<CStructConstraintException>(() => layout.Parse(bytes, "fx"));

        StructValue parsed = layout.Parse(bytes, "fx", null, Skip);
        Assert.AreEqual(200u, parsed["b"]);
        Assert.AreEqual(0u, parsed["a"]);
        Assert.AreEqual(200u, layout.Parse(new MemoryStream(bytes), "fx", null, Skip)["b"]);
        Assert.AreEqual(200u, layout.ParseMany(bytes, "fx", null, Skip).Single()["b"]);
        Assert.IsTrue(layout.TryReadValue<StructValue>(bytes, "fx", out StructValue? value, null, Skip));
        Assert.AreEqual(200u, value!["b"]);
        Assert.AreEqual(200u, layout.ParseWithDebug(bytes, "fx", null, Skip).Value["b"]);
        Assert.AreEqual(200u, layout.Parse(bytes, "fx", null, new ReadOptions { Constraints = ConstraintPolicy.Skip, ExecutionPath = ExecutionPath.NoFastPaths })["b"]);
    }

    /// <summary>Skipping reaches the member-by-member reader, nested structs and the conditions after a runtime-sized array.</summary>
    [TestMethod]
    public void Skip_ReadsAViolatingNestedStruct()
    {
        var layout = new CStruct(Nested);
        byte[] bytes = [2, 1, 5, 3];
        Assert.Throws<CStructConstraintException>(() => layout.Parse(bytes, "outer"));
        StructValue parsed = layout.Parse(bytes, "outer", null, Skip);
        Assert.AreEqual((byte)3, parsed["last"]);
        Assert.AreEqual((byte)2, ((StructValue)parsed["first"]!)["v"]);
    }

    /// <summary>A skipped condition is not evaluated at all: one that names a variable the caller did not supply does not fail.</summary>
    [TestMethod]
    public void Skip_DoesNotEvaluateTheCondition()
    {
        var layout = new CStruct("struct s { uint8 a @expect(a == EXPECTED); uint8 b @expect(b / (a - a) == 0); };");
        byte[] bytes = [1, 2];
        Assert.Throws<CStructReadException>(() => layout.Parse(bytes, "s"));
        StructValue parsed = layout.Parse(bytes, "s", null, Skip);
        Assert.AreEqual((byte)2, parsed["b"]);
    }

    /// <summary>The same read with the policy and a violation collection is contradictory and fails when it starts, whatever the layout.</summary>
    [TestMethod]
    public void SkipWithViolations_IsAnArgumentException()
    {
        var contradictory = new ReadOptions { Constraints = ConstraintPolicy.Skip, ConstraintViolations = new List<CStructConstraintException>() };
        var layout = new CStruct(Fixed);
        byte[] bytes = [1, 0, 0, 0, 1, 0, 0, 0];
        Assert.ThrowsExactly<ArgumentException>(() => layout.Parse(bytes, "fx", null, contradictory));
        Assert.ThrowsExactly<ArgumentException>(() => layout.Parse(new MemoryStream(bytes), "fx", null, contradictory));
        Assert.ThrowsExactly<ArgumentException>(() => layout.ParseMany(bytes, "fx", null, contradictory).ToList());
        StringAssert.Contains(Assert.ThrowsExactly<ArgumentException>(() => layout.Parse(bytes, "fx", null, contradictory)).Message, "Skip");
    }

    /// <summary>A skipped read collects nothing, even from a collection that another option left on the options.</summary>
    [TestMethod]
    public void Skip_CollectsNothing()
    {
        var layout = new CStruct(Fixed);
        var violations = new List<CStructConstraintException>();
        byte[] bytes = [0, 0, 0, 0, 200, 0, 0, 0];
        layout.Parse(bytes, "fx", null, new ReadOptions { ConstraintViolations = violations });
        Assert.AreEqual(2, violations.Count);
        violations.Clear();
        layout.Parse(bytes, "fx", null, new ReadOptions { Constraints = ConstraintPolicy.Skip });
        Assert.AreEqual(0, violations.Count);
    }
}
