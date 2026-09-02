namespace CStructSharp.Tests;

using System.Collections.Generic;
using System.IO;

/// <summary>Verifies that every user-visible declaration namespace is unambiguous before an operation reaches a stream.</summary>
[TestClass]
public class DuplicateNameValidationTests
{
    /// <summary>Reproduces the review's conflicting field layout and proves compilation fails before parsing any bytes.</summary>
    [TestMethod]
    public void Constructor_RejectsExactDuplicateFieldReproductionBeforeStreamAccess()
    {
        const string layout = "struct root { uint8 value; uint16 value; };";
        using var stream = new MemoryStream([0xEE, 0x11, 0x22, 0x33, 0xDD,]);
        stream.Position = 1;
        byte[] original = stream.ToArray();

        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () =>
            {
                var cstruct = new CStruct(layout);
                _ = cstruct.ParseStream(stream, "root");
            });

        StringAssert.Contains(exception.Message, "Duplicate field name 'value' in struct 'root'.");
        RegressionTestSupport.AssertStreamUntouched(stream, original, 1);
    }

    /// <summary>Rejects duplicates recursively in unions, inline structures, pointer targets, and enum member scopes.</summary>
    [TestMethod]
    public void Constructor_RejectsDuplicateNamesInEveryMemberScope()
    {
        (string Layout, string ExpectedMessage)[] cases =
        [
            (
                "union choice { byte value; uint16 value; };",
                "Duplicate member name 'value' in union 'choice'."),
            (
                "struct root { struct { byte item; uint16 item; } child; };",
                "Duplicate field name 'item' in struct 'root.child'."),
            (
                "struct target { byte value; uint16 value; }; struct root { target *pointer; };",
                "Duplicate field name 'value' in struct 'target'."),
            (
                "enum state { Ready = 1, Ready = 2 };",
                "Duplicate enum member name 'Ready' in enum 'state'."),
            (
                "typedef struct backing { byte item; uint16 item; } payload;",
                "Duplicate field name 'item' in struct 'backing'."),
        ];

        foreach ((string layout, string expectedMessage) in cases)
        {
            CStructLayoutException exception = Assert.Throws<CStructLayoutException>(() => new CStruct(layout), layout);
            StringAssert.Contains(exception.Message, expectedMessage, layout);
        }
    }

    /// <summary>Keeps ordinary field names lexical and case-sensitive while allowing the same spelling in another scope.</summary>
    [TestMethod]
    public void Constructor_AllowsCaseDistinctAndSeparatelyScopedFieldNames()
    {
        const string layout = """
                              struct first {
                                  byte value;
                                  byte Value;
                                  byte byte;
                                  struct { byte value; } child;
                              };
                              struct second { byte value; };
                              union choice { byte value; uint16 Value; };
                              """;

        var cstruct = new CStruct(layout);

        Assert.AreEqual(4, cstruct.GetStructSizeInBytes("first"));
        Assert.AreEqual(1, cstruct.GetStructSizeInBytes("second"));
        Assert.AreEqual(2, cstruct.GetStructSizeInBytes("choice"));
    }

    /// <summary>Keeps enum constants inside their enum and permits case-distinct constants and collisions with field/type names.</summary>
    [TestMethod]
    public void Constructor_AllowsEnumMembersInSeparateCaseSensitiveScopes()
    {
        const string layout = """
                              enum first_state { Ready = 1, ready = 2 };
                              enum second_state { Ready = 3 };
                              struct Ready { byte Ready; first_state state; };
                              """;

        var cstruct = new CStruct(layout);
        using var stream = new MemoryStream([0xA5, 0x01,]);
        dynamic result = cstruct.ParseStream(stream, "Ready");

        Assert.AreEqual((byte)0xA5, (byte)result.Ready);
        Assert.AreEqual("Ready", result.state.Name);
    }

    /// <summary>Defines one case-sensitive global declaration namespace and reserves every built-in codec name.</summary>
    [TestMethod]
    public void Constructor_RejectsCrossKindAndBuiltInGlobalNameCollisions()
    {
        (string Layout, string ExpectedMessage)[] cases =
        [
            (
                "struct item { byte value; }; typedef uint16 item;",
                "Duplicate global declaration name 'item': struct and typedef."),
            (
                "union item { byte value; }; enum item { Value };",
                "Duplicate global declaration name 'item': union and enum."),
            (
                "struct item { byte value; }; #define item 1",
                "Duplicate global declaration name 'item': struct and #define."),
            (
                "typedef uint16 item; #define item 1",
                "Duplicate global declaration name 'item': typedef and #define."),
            (
                "struct byte { uint8 value; };",
                "Global struct name 'byte' conflicts with a built-in codec."),
            (
                "typedef uint16 uint32;",
                "Global typedef name 'uint32' conflicts with a built-in codec."),
        ];

        foreach ((string layout, string expectedMessage) in cases)
        {
            CStructLayoutException exception = Assert.Throws<CStructLayoutException>(() => new CStruct(layout), layout);
            StringAssert.Contains(exception.Message, expectedMessage, layout);
        }

        var caseDistinct = new CStruct(
            "struct item { byte value; }; typedef uint16 Item; struct root { item lower; Item upper; };");
        Assert.AreEqual(3, caseDistinct.GetStructSizeInBytes("root"));

        var matchingAlias = new CStruct(
            "typedef struct payload { byte value; } payload; struct root { payload item; };");
        Assert.AreEqual(1, matchingAlias.GetStructSizeInBytes("root"));
    }

    /// <summary>Ensures every public operation is unreachable for an ambiguous layout and therefore cannot mutate stream state.</summary>
    /// <param name="operation">The public operation that must remain unreachable after compilation rejects the layout.</param>
    [TestMethod]
    [DynamicData(nameof(RegressionTestSupport.PublicOperationMatrix), typeof(RegressionTestSupport))]
    public void DuplicateLayout_BlocksEveryOperationBeforeStreamAccess(string operation)
    {
        const string duplicateRoot = "struct root { uint8 value; uint16 value; };";
        const string duplicatePointerTarget =
            "struct target { uint8 value; uint16 value; }; struct root { target *pointer; };";
        string layout = operation == "pointer" ? duplicatePointerTarget : duplicateRoot;
        using var stream = new MemoryStream([0x01, 0x11, 0x22, 0x33, 0x44,]);
        byte[] original = stream.ToArray();

        Assert.Throws<CStructLayoutException>(
            () =>
            {
                var cstruct = new CStruct(layout, pointerSize: 1);
                var data = new Dictionary<string, object> { ["value"] = (byte)0x55, };
                switch (operation)
                {
                case "parse":
                    _ = cstruct.ParseStream(stream, "root");
                    break;
                case "debug":
                    _ = cstruct.ParseStreamWithDebug(stream, "root");
                    break;
                case "address":
                    _ = cstruct.ResolveAddress(stream, "root.value");
                    break;
                case "serialize":
                    _ = cstruct.Serialize("root", data);
                    break;
                case "write":
                    cstruct.WriteStream(stream, "root", data);
                    break;
                case "update":
                    cstruct.UpdateStream(stream, "root.value", (byte)0x55);
                    break;
                case "pointer":
                    _ = cstruct.ResolveAddress(stream, "root.pointer.value.value");
                    break;
                default:
                    Assert.Fail("Unknown operation: " + operation);
                    break;
                }
            });

        RegressionTestSupport.AssertStreamUntouched(stream, original, 0, operation);
    }
}
