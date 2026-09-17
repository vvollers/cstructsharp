namespace CStructSharp.Tests;

using System.Collections.Generic;
using System.IO;
using System.Linq;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Syntax;
using Microsoft.VisualStudio.TestTools.UnitTesting;

/// <summary>
///     Pins E2.6: a field's value is published as a layout variable exactly when an expression can name it, so
///     skipping the capture for every other field can never change an observable result.
/// </summary>
[TestClass]
public class LayoutVariableCaptureTests
{
    /// <summary>Every runtime expression site - array count, condition, switch selector, offset assertion - still sees the field it names; compile-time sites (case labels, bit widths, alignment, enum values, defines) cannot name fields at all.</summary>
    [TestMethod]
    public void EveryExpressionSite_ReadsItsReferencedField()
    {
        var cstruct = new CStruct("""
            enum kind : uint8 { small = 1 };
            struct root {
                uint8 base;
                uint8 lo;
                uint8 n;
                uint8 items[n];
                uint8 flag;
                if (flag == 1) { uint8 yes; } else { uint8 no; }
                uint8 sel;
                switch (sel) { case 1: { uint8 a; } default: { uint8 b; } }
                uint8 mark @ (base + 5 + n);
                uint8 bits : 3;
                uint8 tail;
            };
            """);
        byte[] bytes = [2, 1, 3, 0xA, 0xB, 0xC, 1, 0x11, 1, 0x22, 0x33, 0x05, 0x44];
        dynamic parsed = cstruct.Parse(bytes, "root");
        Assert.AreEqual(3, ((IList<object?>)parsed.items).Count);
        Assert.AreEqual((byte)0x11, parsed.yes);
        Assert.AreEqual((byte)0x22, parsed.a);
        Assert.AreEqual((byte)0x33, parsed.mark);
        Assert.AreEqual((byte)5, parsed.bits);
        Assert.AreEqual((byte)0x44, parsed.tail);
        Assert.AreEqual(3, cstruct.GetDynamicArrayLength(new MemoryStream(bytes), "root.items"));
        Assert.AreEqual(10L, cstruct.ResolveAddress(new MemoryStream(bytes), "root.mark"));
        foreach (string referenced in new[] { "base", "n", "flag", "sel" })
        {
            Assert.IsTrue(CapturesVariable(cstruct, referenced), referenced);
        }

        foreach (string unreferenced in new[] { "lo", "items", "yes", "no", "a", "b", "mark", "bits", "tail" })
        {
            Assert.IsFalse(CapturesVariable(cstruct, unreferenced), unreferenced);
        }
    }

    /// <summary>Only referenced fields are captured; a text field that an expression names disables the optimization for the whole layout.</summary>
    [TestMethod]
    public void CompiledFields_CaptureOnlyWhenReferenced()
    {
        var plain = new CStruct("struct root { uint8 n; uint8 items[n]; uint16 unused; char name[4]; };");
        Assert.IsTrue(CapturesVariable(plain, "n"));
        Assert.IsFalse(CapturesVariable(plain, "unused"));
        Assert.IsFalse(CapturesVariable(plain, "items"));
        Assert.IsFalse(CapturesVariable(plain, "name"));

        var textual = new CStruct("#define x 1\nstruct root { char kind[4]; uint8 unused; if (kind == x) { uint8 v; } };");
        Assert.IsTrue(CapturesVariable(textual, "kind"));
        Assert.IsTrue(CapturesVariable(textual, "unused"), "a referenced text field can name any field at evaluation time");
    }

    /// <summary>
    ///     Supplied variables cannot name a field: integers are literals, and an expression naming a field is rejected
    ///     when the operation starts, so the capture-all fallback is a safety net rather than a reachable path. The
    ///     flag still survives the copies operations make of their variables.
    /// </summary>
    [TestMethod]
    public void SuppliedVariables_CannotReferenceFields_AndTheFallbackFlagPropagates()
    {
        var cstruct = new CStruct("struct root { uint8 unused; uint8 n; uint8 items[n]; };");
        LayoutVariableResolver resolver = cstruct.CompiledLayoutVariables;
        Assert.IsFalse(LayoutVariableInput.FromIntegers(new Dictionary<string, int> { ["n"] = 1 }).Resolve(resolver) is LayoutVariables { CaptureAll: true });
        Assert.ThrowsExactly<CStructLayoutException>(() => LayoutVariableInput.FromExpressions(
            new Dictionary<string, Expr> { ["n"] = new BinaryOp(BinaryOperatorType.Add, new Identifier("unused"), new Literal(1)) }).Resolve(resolver));

        var flagged = new LayoutVariables { CaptureAll = true };
        Assert.IsTrue(new LayoutVariables(flagged).CaptureAll);
        Assert.IsFalse(new LayoutVariables(new Dictionary<string, Expr>()).CaptureAll);
    }

    private static bool CapturesVariable(CStruct cstruct, string fieldName)
    {
        CompiledField field = cstruct.CompiledModel.Fields.Values.Single(candidate => candidate.Declaration.Name.Name == fieldName);
        return field.CapturesLayoutVariable;
    }
}
