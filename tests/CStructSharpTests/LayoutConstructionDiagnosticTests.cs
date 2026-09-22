namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;

/// <summary>Checks that invalid compiled layouts explain the rejected operation and its field.</summary>
[TestClass]
public class LayoutConstructionDiagnosticTests
{
    /// <summary>Construction errors identify the specific layout restriction instead of merely throwing.</summary>
    /// <param name="definition">The invalid layout declaration.</param>
    /// <param name="reason">The diagnostic detail needed to correct that declaration.</param>
    [TestMethod]
    [DataRow("struct root { void value; };", "void has no storage of its own; declare a pointer to it: value")]
    [DataRow("struct h { uint8 a; }; struct root { uint8 s[offsetof(h, missing)]; };", "offsetof: 'h' has no field named 'missing': s")]
    [DataRow("struct root { uint8 s[offsetof(uint8, value)]; };", "offsetof needs a struct or union type: s")]
    [DataRow("struct h { uint8 value; }; struct root { uint8 s[offsetof(h*, value)]; };", "offsetof needs a struct or union type: s")]
    [DataRow("struct root { uint8 s[sizeof(cstring)]; };", "sizeof(cstring) has no fixed size: s")]
    [DataRow("struct h { uint8 n; uint8 data[n]; uint8 tail; }; struct root { uint8 s[offsetof(h, tail)]; };", "offsetof(h, tail) is not statically placed: s")]
    [DataRow("struct root { root child; };", "By-value recursive struct declarations are not supported: root")]
    [DataRow("struct root { uint8 n; uint8 _[n]; };", "A padding field (`_`) needs a fixed-size primitive type in: root")]
    [DataRow("struct child { uint8 value; }; struct root { child _; };", "A padding field (`_`) needs a fixed-size primitive type in: root")]
    [DataRow("struct root { uint8 value @ (-1); };", "Explicit offset assertion must be non-negative: value = -1")]
    [DataRow("struct root { uint8 value : 3 @ (0); };", "An explicit offset assertion is not supported on a bitfield declarator: value")]
    [DataRow("struct root { utf8 text[2][3]; };", "Encoded text buffers support one byte-length dimension; use an array of structs for multiple strings: text")]
    [DataRow("struct root { uint8 n; uint8 items[n][2]; };", "runtime-sized dimension is not yet supported for two or more dimensions: items")]
    [DataRow("struct root { float value : 3; };", "Invalid bitfield declaration: value")]
    public void InvalidDeclaration_ReportsItsSpecificRestriction(string definition, string reason)
    {
        // Compilation, rather than a later binary operation, must reject each declaration.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct(definition));
        StringAssert.Contains(failure.Message, reason);
    }

    /// <summary>A recursive member error retains the source position of the field causing the recursion.</summary>
    [TestMethod]
    public void RecursiveMember_IdentifiesItsSourcePosition()
    {
        const string definition = "struct root { root child; };";

        // The reported source offset points at the by-value member, not the type declaration.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct(definition));
        Assert.AreEqual(definition.IndexOf("child", StringComparison.Ordinal), failure.SourceOffset);
    }

    /// <summary>A mismatched C type keyword points at the named type, including its source line and column.</summary>
    [TestMethod]
    public void TypeKeywordMismatch_IdentifiesTheTypeSpelling()
    {
        const string definition = "enum kind : uint8 { A = 1 };\nstruct root { struct kind value; };";

        // A declaration's enum/struct/union keyword must agree with the resolved named type.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => new CStruct(definition));
        StringAssert.Contains(failure.Message, "Field 'value' declared as 'struct' but 'kind' is a enum.");
        Assert.AreEqual(definition.LastIndexOf("kind", StringComparison.Ordinal), failure.SourceOffset);
        Assert.AreEqual(2, failure.Line);
        Assert.AreEqual(22, failure.Column);
    }

    /// <summary>Overflow while constructing a fixed storage extent is wrapped with its arithmetic cause.</summary>
    [TestMethod]
    public void FixedStorageOverflow_PreservesTheCompilationCause()
    {
        // The array count is valid by itself, but eight bytes per element cannot fit the compiled Int32 extent.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() =>
            new CStruct("struct root { uint64 values[2147483647]; };"));
        StringAssert.Contains(failure.Message, "Layout could not be converted to the compiled intermediate representation.");
        Assert.IsInstanceOfType<OverflowException>(failure.InnerException);
    }
}
