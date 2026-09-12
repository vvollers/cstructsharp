namespace CStructSharpTests;

using CStructSharp;
using CStructSharp.Structure;
using Pidgin;

/// <summary>Protects optimized decision reuse, scoped variables and the checked scalar expression path.</summary>
[TestClass]
public class ConditionalExecutionOptimizationTests
{
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
