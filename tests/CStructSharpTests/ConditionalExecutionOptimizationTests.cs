namespace CStructSharpTests;

using CStructSharp;
using CStructSharp.Structure;
using Pidgin;

/// <summary>Protects optimized decision reuse, scoped variables and the checked scalar expression path.</summary>
[TestClass]
public class ConditionalExecutionOptimizationTests
{
    /// <summary>A nested field can change an external selector without changing an arm already entered.</summary>
    [TestMethod]
    public void GroupDecision_RemainsFrozenWhenNestedFieldsChangeAnExternalSelector()
    {
        var layout = new CStruct("#define TAG 1\nstruct child { uint8 TAG; }; struct root { if (TAG) { child nested; uint8 payload; } else { uint16 fallback; } };", aligned: false);
        byte[] bytes = [0, 42];
        using var stream = new MemoryStream(bytes);
        dynamic value = layout.ParseStream(stream, "root");
        Assert.AreEqual((byte)42, value.payload);
        Assert.AreEqual(2L, stream.Position);
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", (object)value));
        Assert.AreEqual(1L, layout.ResolveAddress(new MemoryStream(bytes), "root.payload"));
    }

    /// <summary>A composite needs conditional state even when it has no unconditional tag field.</summary>
    [TestMethod]
    public void EntirelyConditionalComposite_UsesOnlyTheSelectedArm()
    {
        var layout = new CStruct("struct root { if (1) { uint8 value; } else { uint16 other; } };", aligned: false);
        byte[] bytes = [42];
        using var stream = new MemoryStream(bytes);
        dynamic value = layout.ParseStream(stream, "root");
        Assert.AreEqual((byte)42, value.value);
        Assert.AreEqual(1L, stream.Position);
        CollectionAssert.AreEqual(bytes, layout.Serialize("root", (object)value));
        Assert.AreEqual(0L, layout.ResolveAddress(new MemoryStream(bytes), "root.value"));
        Assert.Throws<CStructException>(() => layout.Serialize("root", new { value = 42, other = 7 }));
    }

    /// <summary>A nested name cannot revive an inactive local that has never had a value.</summary>
    [TestMethod]
    public void NestedField_CannotReviveAnInactiveLocal()
    {
        var layout = new CStruct("struct child { uint8 count; }; struct root { if (0) { uint8 count; } child nested; if (count) { uint8 payload; } };", aligned: false);
        byte[] bytes = [1, 42];
        Assert.Throws<CStructLayoutException>(() => layout.ParseStream(new MemoryStream(bytes), "root"));
        Assert.Throws<CStructLayoutException>(() => layout.ResolveAddress(new MemoryStream(bytes), "root.payload"));
        Assert.Throws<CStructException>(() => layout.Serialize("root", new { nested = new { count = 1 }, payload = 42 }));
    }

    /// <summary>Selector errors retain useful expression context at the public boundary.</summary>
    [TestMethod]
    public void MissingSelector_IdentifiesConditionalEvaluation()
    {
        var layout = new CStruct("struct root { if (missing) { uint8 value; } };", aligned: false);
        CStructLayoutException error = Assert.Throws<CStructLayoutException>(() => layout.ParseStream(new MemoryStream([42]), "root"));
        StringAssert.Contains(error.Message, "conditional selector");
    }

    /// <summary>Common scalar selectors must not silently regress to allocating a full evaluation session.</summary>
    [TestMethod]
    public void SimpleSelectors_AllocateLessThanFullSessions()
    {
        foreach (string source in new[] { "42", "a", "-a", "~a", "!a", "a+b", "a==b" })
        {
            Expr expression = CStructDefinitionParser.Expr.ParseOrThrow(source);
            var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(256, 100_000));
            var variables = new Dictionary<string, Expr> { ["a"] = new Literal(1), ["b"] = new Literal(2) };
            for (int index = 0; index < 100; index++)
            {
                _ = evaluator.Evaluate(expression, variables);
                _ = evaluator.CreateSession(variables).Evaluate(expression);
            }

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 100; index++)
            {
                _ = evaluator.Evaluate(expression, variables);
            }

            long fast = GC.GetAllocatedBytesForCurrentThread() - before;
            before = GC.GetAllocatedBytesForCurrentThread();
            for (int index = 0; index < 100; index++)
            {
                _ = evaluator.CreateSession(variables).Evaluate(expression);
            }

            long full = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.IsTrue(fast < full / 2, $"{source}: simple={fast}, full={full} allocated bytes.");
        }
    }

    /// <summary>The fast scalar evaluator must preserve the full evaluator's values and resource failures.</summary>
    [TestMethod]
    public void SimpleExpressions_MatchBoundedSessionEvaluation()
    {
        string[] expressions = ["a", "42", "-a", "~a", "!a", "a+a", "a+b", "a-b", "a*b", "a/b", "a<<b", "a>>b", "a==b", "a!=b", "a<b", "a>=b", "a&b", "a|b"];
        foreach (string source in expressions)
        {
            Expr expression = CStructDefinitionParser.Expr.ParseOrThrow(source);
            foreach (int a in new[] { int.MinValue, -1, 0, int.MaxValue })
            {
                foreach (int b in new[] { -1, 0, 1, 32 })
                {
                    for (int depth = 1; depth <= 4; depth++)
                    {
                        for (int nodes = 1; nodes <= 5; nodes++)
                        {
                            var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(depth, nodes));
                            var variables = new Dictionary<string, Expr> { ["a"] = new Literal(a), ["b"] = new Literal(b) };
                            Assert.AreEqual(
                                Capture(() => evaluator.CreateSession(variables).Evaluate(expression)),
                                Capture(() => evaluator.Evaluate(expression, variables)),
                                $"{source}, a={a}, b={b}, depth={depth}, nodes={nodes}");
                        }
                    }
                }
            }
        }
    }

    /// <summary>Group slots and restored local slots belong to each operation and each array element.</summary>
    [TestMethod]
    public void ConcurrentMixedArms_PreserveScopedValuesAndRoundtrip()
    {
        const string definition = "struct inner { uint8 tag; uint8 count; }; struct entry { uint8 tag; uint8 count; switch (tag) { case 1: { inner child; uint8 bytes[count]; } case 2: { uint16 other; } default: { uint8 fallback; } } if (tag == 1) { uint8 trailer; } }; struct root { entry entries[3]; };";
        var layout = new CStruct(definition);
        byte[] bytes = [1, 2, 99, 99, 42, 43, 77, 2, 0, 0x34, 0x12, 3, 0, 88];
        Parallel.For(0, 64, _ =>
        {
            using var stream = new MemoryStream(bytes);
            dynamic value = layout.ParseStream(stream, "root");
            Assert.AreEqual(bytes.Length, stream.Position);
            CollectionAssert.AreEqual(bytes, layout.Serialize("root", (object)value));
            Assert.AreEqual(6L, layout.ResolveAddress(new MemoryStream(bytes), "root.entries[0].trailer"));
            Assert.AreEqual((ushort)0x1234, layout.ReadValue<ushort>(new MemoryStream(bytes), "root.entries[1].other"));
            Assert.AreEqual((byte)88, layout.ReadValue<byte>(new MemoryStream(bytes), "root.entries[2].fallback"));
        });
    }

    private static (Type? Error, int Value) Capture(Func<int> operation)
    {
        try
        {
            return (null, operation());
        }
        catch (Exception error)
        {
            return (error.GetType(), 0);
        }
    }
}
