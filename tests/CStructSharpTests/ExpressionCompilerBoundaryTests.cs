namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Expressions;
using CStructSharp.Parsing;
using CStructSharp.Syntax;

/// <summary>Checks focused expression-compiler diagnostics and exact syntax-work limits.</summary>
[TestClass]
public class ExpressionCompilerBoundaryTests
{
    /// <summary>Independent constant programs share the canonical empty dependency storage.</summary>
    [TestMethod]
    public void EmptyDependencies_UseSharedEmptyStorage()
    {
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(10, 100));
        IReadOnlyCollection<string> literal = evaluator.GetDependencies(new Literal(1));
        IReadOnlyCollection<string> addition = evaluator.GetDependencies(CStructDefinitionParser.ParseExpression("2 + 3"));

        Assert.AreEqual(0, literal.Count);
        Assert.AreSame(literal, addition, "A program without names does not need its own empty dependency array.");
    }

    /// <summary>Exact evaluation compiles a named dependency completely before selecting one of its branches.</summary>
    /// <param name="dependency">A dependency with an unsupported call in the unselected branch.</param>
    [TestMethod]
    [DataRow("1 ? 7 : sizeof(root)")]
    [DataRow("0 ? sizeof(root) : 7")]
    public void ExactDependency_ValidatesUnselectedSyntax(string dependency)
    {
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(10, 100));
        var variables = new Dictionary<string, Expr> { ["value"] = CStructDefinitionParser.ParseExpression(dependency), };

        // Layout calls must already have been folded; exact arithmetic does not bypass compiler validation.
        NotSupportedException failure = Assert.Throws<NotSupportedException>(() => evaluator.EvaluateExact(new Identifier("value"), variables, 64));
        Assert.AreEqual("Expression calls are parsed but are not supported.", failure.Message);
    }

    /// <summary>A runtime-only wide-value marker is not syntax and reports its node type if passed to the compiler.</summary>
    [TestMethod]
    public void UnsupportedSyntaxNode_IdentifiesItsType()
    {
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(10, 100));

        // The marker describes captured data, not syntax the compiler can execute.
        NotSupportedException failure = Assert.Throws<NotSupportedException>(() => evaluator.Compile(new WideValueVariable(ulong.MaxValue)));
        Assert.AreEqual("Unsupported expression node type: WideValueVariable", failure.Message);
    }

    /// <summary>The binary instruction helper identifies an opcode outside its own operator domain.</summary>
    /// <param name="value">A leaf opcode or an unknown enum value, neither of which is a binary operator.</param>
    [TestMethod]
    [DataRow((int)ExpressionEvaluator.ExpressionOpcode.Identifier)]
    [DataRow(999)]
    public void InvalidBinaryOpcode_IdentifiesTheInstruction(int value)
    {
        var opcode = (ExpressionEvaluator.ExpressionOpcode)value;

        // A leaf or unknown opcode must not silently select an unrelated binary operation.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => ExpressionEvaluator.ExpressionEvaluationSession.EvaluateBinary(opcode, 1, 2));
        Assert.AreEqual("Unknown compiled binary expression opcode: " + opcode, failure.Message);
    }

    /// <summary>A missing expression is identified at the evaluator boundary, not as an internal cache-key failure.</summary>
    [TestMethod]
    public void NullExpression_IdentifiesTheArgument()
    {
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(10, 10));

        // The evaluator's parameter name should survive the compiler's internal cache implementation.
        ArgumentNullException failure = Assert.Throws<ArgumentNullException>(() => evaluator.Compile(null!));
        Assert.AreEqual("expression", failure.ParamName);
    }

    /// <summary>Compilation accepts exactly three syntax nodes and rejects the same tree when only two are permitted.</summary>
    [TestMethod]
    public void SyntaxWork_ReportsTheConfiguredLimit()
    {
        Expr expression = CStructDefinitionParser.ParseExpression("1 + 2");
        new ExpressionEvaluator(new ExpressionEvaluationLimits(10, 3)).Compile(expression);
        var limited = new ExpressionEvaluator(new ExpressionEvaluationLimits(10, 2));

        // The two literal operands and their addition each count as one syntax node.
        CStructLayoutException failure = Assert.Throws<CStructLayoutException>(() => limited.Compile(expression));
        Assert.AreEqual("Maximum expression evaluation work exceeded.", failure.Message);
    }

    /// <summary>An invalid internal syntax operator reports its operator family and value before execution.</summary>
    /// <param name="unary">Whether to construct a unary rather than binary node.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void UnknownSyntaxOperator_IdentifiesItsFamily(bool unary)
    {
        Expr expression = unary
                              ? new UnaryOp((UnaryOperatorType)999, new Literal(1))
                              : new BinaryOp((BinaryOperatorType)999, new Literal(1), new Literal(2));
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(10, 10));

        // Invalid internal enum values must not silently select an unrelated arithmetic operation.
        InvalidOperationException failure = Assert.Throws<InvalidOperationException>(() => evaluator.Compile(expression));
        Assert.AreEqual(unary ? "Unknown unary operator: 999" : "Unknown binary operator: 999", failure.Message);
    }
}
