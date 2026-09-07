namespace CStructSharp.Tests;

using System.Collections.Immutable;
using CStructSharp.Structure;
using CstructEnum = CStructSharp.Structure.Enum;

/// <summary>
///     Exercises <see cref="SymbolValidation"/> directly, independent of a full layout compilation. Only reachable
///     indirectly through the public API before this type was extracted from the God-Object <c>CStruct</c> partial
///     class.
/// </summary>
[TestClass]
public class SymbolValidationTests
{
    /// <summary>Every declaration kind must report its own user-facing label, and a union must not read as a struct.</summary>
    [TestMethod]
    public void GetDeclarationKind_ReportsTheUserFacingLabelForEveryDeclarationKind()
    {
        Assert.AreEqual("struct", SymbolValidation.GetDeclarationKind(EmptyStruct("s", isUnion: false)));
        Assert.AreEqual("union", SymbolValidation.GetDeclarationKind(EmptyStruct("u", isUnion: true)));
        Assert.AreEqual("enum", SymbolValidation.GetDeclarationKind(EmptyEnum("e")));
        Assert.AreEqual("typedef", SymbolValidation.GetDeclarationKind(new Typedef(new Identifier("t"), new Identifier("uint8"))));
        Assert.AreEqual(
            "#define",
            SymbolValidation.GetDeclarationKind(new Structure.Defines(new Identifier("d"), new Literal(1))));
    }

    /// <summary>A struct with two distinct field names is a valid scope and must not raise anything.</summary>
    [TestMethod]
    public void ValidateScopedMemberNames_StructWithUniqueFieldNames_DoesNotThrow()
    {
        Struct root = MakeStruct(
            "root",
            isUnion: false,
            ScalarField("a"),
            ScalarField("b"));

        SymbolValidation.ValidateScopedMemberNames([root,]);
    }

    /// <summary>Two fields sharing one name in the same struct scope must be rejected.</summary>
    [TestMethod]
    public void ValidateScopedMemberNames_StructWithDuplicateFieldNames_Throws()
    {
        Struct root = MakeStruct(
            "root",
            isUnion: false,
            ScalarField("a"),
            ScalarField("a"));

        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => SymbolValidation.ValidateScopedMemberNames([root,]));
        StringAssert.Contains(exception.Message, "field");
        StringAssert.Contains(exception.Message, "'a'");
    }

    /// <summary>A union's duplicate-name error must describe it as a union with a member, not a struct with a field.</summary>
    [TestMethod]
    public void ValidateScopedMemberNames_UnionWithDuplicateMemberNames_ThrowsWithUnionWording()
    {
        Struct root = MakeStruct(
            "root",
            isUnion: true,
            ScalarField("a"),
            ScalarField("a"));

        CStructLayoutException exception = Assert.Throws<CStructLayoutException>(
            () => SymbolValidation.ValidateScopedMemberNames([root,]));
        StringAssert.Contains(exception.Message, "member");
        StringAssert.Contains(exception.Message, "union");
    }

    /// <summary>Two identically-named fields inside a nested inline struct must be caught, not just the outer scope.</summary>
    [TestMethod]
    public void ValidateScopedMemberNames_DuplicateNamesInsideANestedStruct_Throws()
    {
        Struct inner = MakeStruct(
            "inner",
            isUnion: false,
            ScalarField("x"),
            ScalarField("x"));
        Struct outer = MakeStruct("outer", isUnion: false, inner);

        Assert.Throws<CStructLayoutException>(() => SymbolValidation.ValidateScopedMemberNames([outer,]));
    }

    /// <summary>An inline struct reached through a typedef is validated exactly like a standalone struct declaration.</summary>
    [TestMethod]
    public void ValidateScopedMemberNames_TypedefWrappingAnInlineStruct_ValidatesTheInlineStruct()
    {
        Struct inline = MakeStruct("inline", isUnion: false, ScalarField("a"), ScalarField("a"));
        var typedef = new Typedef(new Identifier("alias"), inline);

        Assert.Throws<CStructLayoutException>(() => SymbolValidation.ValidateScopedMemberNames([typedef,]));
    }

    /// <summary>An enum with two members sharing one name must be rejected.</summary>
    [TestMethod]
    public void ValidateScopedMemberNames_EnumWithDuplicateMemberNames_Throws()
    {
        CstructEnum duplicateEnum = MakeEnum("state", "Ready", "Ready");

        Assert.Throws<CStructLayoutException>(() => SymbolValidation.ValidateScopedMemberNames([duplicateEnum,]));
    }

    /// <summary>An enum whose members all have distinct names is a valid scope and must not raise anything.</summary>
    [TestMethod]
    public void ValidateScopedMemberNames_EnumWithUniqueMemberNames_DoesNotThrow()
    {
        CstructEnum uniqueEnum = MakeEnum("state", "None", "Ready");

        SymbolValidation.ValidateScopedMemberNames([uniqueEnum,]);
    }

    /// <summary>A declaration name that shadows a registered built-in codec name must be rejected.</summary>
    [TestMethod]
    public void ValidateBuiltInNameCollision_NameMatchesABuiltInCodec_Throws()
    {
        var fieldHandlers = new Dictionary<string, Func<Stream, object>> { ["uint32"] = _ => 0, };
        Struct collidingStruct = EmptyStruct("uint32", isUnion: false);

        Assert.Throws<CStructLayoutException>(
            () => SymbolValidation.ValidateBuiltInNameCollision(collidingStruct, fieldHandlers));
    }

    /// <summary>A declaration name that does not match any built-in codec name is accepted.</summary>
    [TestMethod]
    public void ValidateBuiltInNameCollision_NameDoesNotMatchABuiltInCodec_DoesNotThrow()
    {
        var fieldHandlers = new Dictionary<string, Func<Stream, object>> { ["uint32"] = _ => 0, };
        Struct distinctStruct = EmptyStruct("packet", isUnion: false);

        SymbolValidation.ValidateBuiltInNameCollision(distinctStruct, fieldHandlers);
    }

    private static Field ScalarField(string name)
    {
        return new Field(new Identifier("uint8"), new Identifier(name), Field.NoArray, 0);
    }

    private static Struct MakeStruct(string name, bool isUnion, params Field[] fields)
    {
        return new Struct(new Identifier(name), [.. fields,], isUnion);
    }

    private static Struct EmptyStruct(string name, bool isUnion)
    {
        return MakeStruct(name, isUnion);
    }

    private static CstructEnum EmptyEnum(string name)
    {
        return new CstructEnum(new Identifier(name), ImmutableArray<EnumValue>.Empty);
    }

    private static CstructEnum MakeEnum(string name, params string[] memberNames)
    {
        ImmutableArray<EnumValue> values =
            [.. memberNames.Select(memberName => new EnumValue(new Identifier(memberName))),];
        return new CstructEnum(new Identifier(name), values);
    }
}
