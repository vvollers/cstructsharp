namespace CStructSharp.Tests;

using CStructSharp.Structure;
using Pidgin;

/// <summary>Verifies one checked, depth-limited, work-limited expression policy across every core operation.</summary>
[TestClass]
public class ExpressionSafetyTests
{
    /// <summary>Rejects invalid limits and applies caller-selected syntax-tree depth and work boundaries.</summary>
    [TestMethod]
    public void CompilationOptions_EnforceExpressionDepthAndTokenLimits()
    {
        const string simpleLayout = "struct root { byte value; };";
        var invalidOptions = new CStructCompilationOptions[]
        {
            new() { MaxDefinitionLength = 0, },
            new() { MaxLayoutNestingDepth = 0, },
            new() { MaxExpressionNestingDepth = 0, },
            new() { MaxExpressionTokens = 0, },
        };
        foreach (CStructCompilationOptions options in invalidOptions)
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new CStruct(simpleLayout, compilationOptions: options));
        }

        _ = new CStruct(
            simpleLayout,
            compilationOptions: new CStructCompilationOptions
            {
                MaxDefinitionLength = simpleLayout.Length,
                MaxLayoutNestingDepth = 1,
            });
        _ = new CStruct(
            "/* ((( {{{ */ // ))) }}}\nstruct root { byte values[((1))]; };",
            compilationOptions: new CStructCompilationOptions
            {
                MaxLayoutNestingDepth = 1,
                MaxExpressionNestingDepth = 2,
            });

        Assert.Throws<CStructLayoutException>(
            () => new CStruct(
                "struct root { byte values[(((1)))]; };",
                compilationOptions: new CStructCompilationOptions
                {
                    MaxExpressionNestingDepth = 2,
                }));
        Assert.Throws<CStructLayoutException>(
            () => new CStruct(
                "struct root { byte values[1 + 1]; };",
                compilationOptions: new CStructCompilationOptions
                {
                    MaxExpressionTokens = 2,
                }));
    }

    /// <summary>Applies the configured work limit to definitions, enums, bit widths, and fixed-array counts.</summary>
    /// <param name="layout">A layout whose relevant expression contains three nodes.</param>
    [TestMethod]
    [DataRow("#define COUNT 1 + 1\nstruct root { byte value; };")]
    [DataRow("enum mode { Value = 1 + 1 }; struct root { mode value; };")]
    [DataRow("struct root { uint8 value: 1 + 1; };")]
    [DataRow("struct root { byte values[1 + 1]; };")]
    public void CompilationTokenLimit_AppliesToEveryStaticExpressionSite(string layout)
    {
        Assert.Throws<CStructLayoutException>(
            () => new CStruct(
                layout,
                compilationOptions: new CStructCompilationOptions
                {
                    MaxExpressionTokens = 2,
                }));
    }

    /// <summary>Bounds named dependency chains and rejects parsed calls before any operation starts.</summary>
    [TestMethod]
    public void CompilationLimits_IncludeDefinitionDependenciesAndUnsupportedCalls()
    {
        const string dependencyChain = """
                                       #define A B
                                       #define B C
                                       #define C 1
                                       struct root { byte values[A]; };
                                       """;
        Assert.Throws<CStructLayoutException>(
            () => new CStruct(
                dependencyChain,
                compilationOptions: new CStructCompilationOptions
                {
                    MaxExpressionNestingDepth = 3,
                }));

        Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct root { byte values[method(1)]; };"));
    }

    /// <summary>Applies immutable compilation limits to caller expression graphs supplied after construction.</summary>
    [TestMethod]
    public void RuntimeVariables_UseConfiguredDepthAndWorkLimits()
    {
        const string layout = "struct root { byte values[COUNT]; };";
        var defaultLimits = new CStruct(layout);
        var acyclic = new Dictionary<string, Expr> { ["NODE0"] = new Literal(1), };
        for (int index = 1; index <= 100; index++)
        {
            acyclic["NODE" + index] = new Identifier("NODE" + (index - 1));
        }

        acyclic["COUNT"] = new Identifier("NODE100");
        dynamic valid = defaultLimits.ParseStream(new MemoryStream([0x2A,]), "root", acyclic);
        Assert.AreEqual(1, valid.values.Count);

        var withDefault = new CStruct("#define COUNT 2\n" + layout);
        dynamic overridden = withDefault.ParseStream(
            new MemoryStream([0x2A,]),
            "root",
            new Dictionary<string, Expr> { ["COUNT"] = new Literal(1), });
        Assert.AreEqual(1, overridden.values.Count);

        var undefined = new Dictionary<string, Expr> { ["COUNT"] = new Identifier("MISSING"), };
        Assert.Throws<CStructLayoutException>(
            () => defaultLimits.ParseStream(new MemoryStream([0x2A,]), "root", undefined));
        Assert.IsInstanceOfType<Identifier>(undefined["COUNT"]);

        var depthOptions = new CStructCompilationOptions
        {
            MaxExpressionNestingDepth = 3,
            MaxExpressionTokens = 100,
        };
        var depthLimited = new CStruct(
            layout,
            compilationOptions: depthOptions);
        Expr deep = new Literal(1);
        for (int i = 0; i < 4; i++)
        {
            deep = new UnaryOp(UnaryOperatorType.Complement, deep);
        }

        Assert.Throws<CStructLayoutException>(
            () => depthLimited.ParseStream(
                new MemoryStream([1,]),
                "root",
                new Dictionary<string, Expr> { ["COUNT"] = deep, }));

        var workLimited = new CStruct(
            layout,
            compilationOptions: new CStructCompilationOptions
            {
                MaxExpressionNestingDepth = 10,
                MaxExpressionTokens = 4,
            });
        Expr tooMuchWork = new BinaryOp(
            BinaryOperatorType.Or,
            new BinaryOp(BinaryOperatorType.Or, new Literal(1), new Literal(1)),
            new Literal(1));
        Assert.Throws<CStructLayoutException>(
            () => workLimited.ParseStream(
                new MemoryStream([1,]),
                "root",
                new Dictionary<string, Expr> { ["COUNT"] = tooMuchWork, }));

        var selfReferential = new Dictionary<string, Expr>
        {
            ["COUNT"] = new Identifier("COUNT"),
        };
        Assert.Throws<CStructLayoutException>(
            () => depthLimited.ParseStream(new MemoryStream([1,]), "root", selfReferential));
        Assert.IsInstanceOfType<Identifier>(selfReferential["COUNT"]);

        var cyclic = new Dictionary<string, Expr>
        {
            ["COUNT"] = new Identifier("OTHER"),
            ["OTHER"] = new Identifier("COUNT"),
        };
        Assert.Throws<CStructLayoutException>(
            () => depthLimited.ParseStream(new MemoryStream([1,]), "root", cyclic));
        Assert.IsInstanceOfType<Identifier>(cyclic["COUNT"]);
        Assert.IsInstanceOfType<Identifier>(cyclic["OTHER"]);
    }

    /// <summary>Defers only definitions with genuine external dependencies and resolves them from operation variables.</summary>
    [TestMethod]
    public void RuntimeVariables_ResolveDefinitionsThatDependOnExternalNames()
    {
        const string layout = """
                              #define COUNT BASE + 1
                              struct root { byte values[COUNT]; };
                              """;
        var cstruct = new CStruct(layout);
        var variables = new Dictionary<string, Expr> { ["BASE"] = new Literal(1), };

        dynamic parsed = cstruct.ParseStream(new MemoryStream([0x2A, 0xA5,]), "root", variables);

        Assert.AreEqual(2, parsed.values.Count);
        Assert.AreEqual(1, variables.Count);
        Assert.AreEqual(1, variables["BASE"].Value);
        Assert.Throws<CStructLayoutException>(
            () => cstruct.ParseStream(new MemoryStream([0x2A, 0xA5,]), "root"));
    }

    /// <summary>Makes statically resolved definitions available to every declaration-level expression site.</summary>
    [TestMethod]
    public void StaticDefinitions_AreAvailableToEnumBitfieldAndArrayExpressions()
    {
        const string layout = """
                              #define WIDTH 1 + 1
                              #define FIRST 4
                              enum mode { Value = FIRST + 1 };
                              struct root { uint8 flags: WIDTH; byte values[WIDTH]; mode value; };
                              """;
        var cstruct = new CStruct(layout);

        dynamic parsed = cstruct.ParseStream(new MemoryStream([0x03, 0x2A, 0xA5, 0x05,]), "root");

        Assert.AreEqual(3UL, Convert.ToUInt64(parsed.flags));
        Assert.AreEqual(2, parsed.values.Count);
        Assert.AreEqual("Value", ((EnumValueResult)parsed.value).Name);
    }

    /// <summary>Normalizes runtime array-expression overflow across parse, debug, address, write, and update entry points.</summary>
    [TestMethod]
    public void RuntimeArrayOverflow_UsesLayoutExceptionAcrossOperationsWithoutMutation()
    {
        const string layout = "struct root { byte values[COUNT + 1]; byte tail; };";
        var cstruct = new CStruct(layout, pointerSize: 1);
        var variables = new Dictionary<string, Expr> { ["COUNT"] = new Literal(int.MaxValue), };
        var data = new Dictionary<string, object>
        {
            ["values"] = Array.Empty<byte>(),
            ["tail"] = (byte)2,
        };

        Assert.Throws<CStructLayoutException>(
            () => cstruct.ParseStream(new MemoryStream([1, 2,]), "root", variables));
        Assert.Throws<CStructLayoutException>(
            () => cstruct.ParseStreamWithDebug(new MemoryStream([1, 2,]), "root", variables));
        Assert.Throws<CStructLayoutException>(
            () => cstruct.ResolveAddress(new MemoryStream([1, 2,]), "root.tail", variables));
        Assert.Throws<CStructLayoutException>(
            () => cstruct.GetDynamicArrayLength(new MemoryStream([1, 2,]), "root.values", variables));
        Assert.Throws<CStructLayoutException>(() => cstruct.Serialize("root", data, variables));

        using var writeStream = new MemoryStream([0xA5, 0xA5,]);
        Assert.Throws<CStructLayoutException>(
            () => cstruct.WriteStream(writeStream, "root", data, variables));
        CollectionAssert.AreEqual(new byte[] { 0xA5, 0xA5, }, writeStream.ToArray());
        Assert.AreEqual(0L, writeStream.Position);

        using var updateStream = new MemoryStream([0xA5, 0xA5,]) { Position = 1, };
        Assert.Throws<CStructLayoutException>(
            () => cstruct.UpdateStream(updateStream, "root.tail", (byte)3, variables));
        CollectionAssert.AreEqual(new byte[] { 0xA5, 0xA5, }, updateStream.ToArray());
        Assert.AreEqual(1L, updateStream.Position);
    }

    /// <summary>Rejects overflow and invalid shifts in enum, bitfield, and array expression sites during compilation.</summary>
    [TestMethod]
    public void StaticExpressionSites_UseCheckedSignedInt32Semantics()
    {
        _ = new CStruct(
            "enum mode : int32 { Only = 2147483647 }; struct root { mode value; };");
        _ = new CStruct(
            "enum mode : int32 { Maximum = 2147483647, Reset = 0 }; struct root { mode value; };");
        Assert.Throws<CStructLayoutException>(
            () => new CStruct("enum mode : int32 { First = 2147483647, Second }; struct root { mode value; };"));
        Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct root { uint8 value: 1 << 32; };"));
        Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct root { byte values[1 << 32]; };"));
        Assert.Throws<CStructLayoutException>(
            () => new CStruct("#define COUNT 2147483647 + 1\nstruct root { byte values[COUNT]; };"));

        Field zeroWidth = CStructDefinitionParser.Field.ParseOrThrow("uint8 value: 0;");
        Assert.Throws<InvalidOperationException>(() => _ = zeroWidth.BitSize);
    }

    /// <summary>Defines checked arithmetic, explicit shift ranges, division by zero, and two's-complement base literals.</summary>
    [TestMethod]
    public void StandaloneExpressions_HaveExplicitNumericFailureSemantics()
    {
        Assert.AreEqual(4, CStructDefinitionParser.Expr.ParseOrThrow("10 - 3 * 2").Calc());
        Assert.AreEqual(4, CStructDefinitionParser.Expr.ParseOrThrow("8 / 2").Calc());
        Assert.AreEqual(1, CStructDefinitionParser.Expr.ParseOrThrow("5 & 3").Calc());
        Assert.AreEqual(5, CStructDefinitionParser.Expr.ParseOrThrow("4 | 1").Calc());
        Assert.AreEqual(-1, CStructDefinitionParser.Expr.ParseOrThrow("~0").Calc());
        Assert.AreEqual(-1, CStructDefinitionParser.Expr.ParseOrThrow("-2 >> 1").Calc());
        Assert.AreEqual(int.MinValue, CStructDefinitionParser.Expr.ParseOrThrow("-1 << 31").Calc());
        Assert.AreEqual(int.MaxValue, CStructDefinitionParser.Expr.ParseOrThrow("2147483647 << 0").Calc());
        Assert.AreEqual(0, NoneExpr.Instance.Calc());

        Assert.Throws<OverflowException>(
            () => CStructDefinitionParser.Expr.ParseOrThrow("2147483647 + 1").Calc());
        Assert.Throws<OverflowException>(
            () => new BinaryOp(
                BinaryOperatorType.Minus,
                new Literal(int.MinValue),
                new Literal(1)).Calc());
        Assert.Throws<OverflowException>(
            () => CStructDefinitionParser.Expr.ParseOrThrow("2147483647 * 2").Calc());
        Assert.Throws<OverflowException>(
            () => CStructDefinitionParser.Expr.ParseOrThrow("1073741824 << 1").Calc());
        Assert.Throws<OverflowException>(
            () => new UnaryOp(UnaryOperatorType.Neg, new Literal(int.MinValue)).Calc());
        Assert.Throws<OverflowException>(
            () => CStructDefinitionParser.Expr.ParseOrThrow("0x80000000 / -1").Calc());
        Assert.Throws<DivideByZeroException>(
            () => CStructDefinitionParser.Expr.ParseOrThrow("1 / 0").Calc());
        Assert.Throws<InvalidOperationException>(
            () => CStructDefinitionParser.Expr.ParseOrThrow("1 << 32").Calc());
        Assert.Throws<InvalidOperationException>(
            () => new BinaryOp(BinaryOperatorType.ShiftRight, new Literal(1), new Literal(-1)).Calc());

        Assert.AreEqual(-1, CStructDefinitionParser.LiteralHex.ParseOrThrow("0xFFFFFFFF").Calc());
        Assert.AreEqual(int.MinValue, CStructDefinitionParser.LiteralHex.ParseOrThrow("0x80000000").Calc());
        Assert.AreEqual(
            -1,
            CStructDefinitionParser.LiteralBinary.ParseOrThrow(
                "0b11111111111111111111111111111111").Calc());
        Assert.AreEqual(-1, CStructDefinitionParser.LiteralOctal.ParseOrThrow("0o37777777777").Calc());
        Assert.AreEqual(1, CStructDefinitionParser.LiteralHex.ParseOrThrow("-0xFFFFFFFF").Calc());
        Assert.Throws<OverflowException>(
            () => CStructDefinitionParser.LiteralHex.ParseOrThrow("-0x80000000").Calc());
        Assert.Throws<OverflowException>(
            () => CStructDefinitionParser.LiteralHex.ParseOrThrow("0x100000000").Calc());
        Assert.Throws<CStructLayoutException>(
            () => new CStruct("struct root { byte values[1u]; };"));
        Assert.Throws<KeyNotFoundException>(() => new Identifier("MISSING").Calc());

        Expr widePostfixStack = new Literal(1);
        for (int index = 1; index < 40; index++)
        {
            widePostfixStack = new BinaryOp(
                BinaryOperatorType.Add,
                new Literal(1),
                widePostfixStack);
        }

        Assert.AreEqual(40, widePostfixStack.Calc());
    }

    /// <summary>Bounds direct public expression evaluation even when a caller builds the immutable node graph manually.</summary>
    [TestMethod]
    public void StandaloneExpression_RejectsAdversarialDepthWithoutRecursingUnboundedly()
    {
        Expr expression = new Literal(1);
        for (int i = 0; i < 300; i++)
        {
            expression = new UnaryOp(UnaryOperatorType.Complement, expression);
        }

        Assert.Throws<CStructLayoutException>(() => expression.Calc());

        string unaryLayout = "struct root { byte values[" + new string('~', 300) + "1]; };";
        Assert.Throws<CStructLayoutException>(() => new CStruct(unaryLayout));

        var variables = new Dictionary<string, Expr>(StringComparer.Ordinal);
        for (int index = 0; index < 300; index++)
        {
            variables["VALUE" + index] = index == 299
                                              ? new Literal(1)
                                              : new Identifier("VALUE" + (index + 1));
        }

        Assert.Throws<CStructLayoutException>(() => new Identifier("VALUE0").Calc(variables));

        string defineChain = string.Join(
            Environment.NewLine,
            Enumerable.Range(0, 299).Select(index => $"#define VALUE{index} VALUE{index + 1}")) +
            Environment.NewLine +
            "#define VALUE299 1" +
            Environment.NewLine +
            "struct root { byte values[VALUE0]; };";
        Assert.Throws<CStructLayoutException>(() => new CStruct(defineChain));
    }

    /// <summary>Reuses the static baseline and recomputes only definitions transitively affected by an override.</summary>
    [TestMethod]
    public void DefinitionOverrides_InvalidateOnlyTheirDependentCachedValues()
    {
        var evaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(32, 100));
        var resolver = new LayoutVariableResolver(
            [
                new CStructSharp.Structure.Defines(new Identifier("BASE"), new Literal(2)),
                new CStructSharp.Structure.Defines(
                    new Identifier("DOUBLE"),
                    new BinaryOp(
                        BinaryOperatorType.Mul,
                        new Identifier("BASE"),
                        new Literal(2))),
                new CStructSharp.Structure.Defines(
                    new Identifier("SIZE"),
                    new BinaryOp(
                        BinaryOperatorType.Add,
                        new Identifier("DOUBLE"),
                        new Literal(1))),
                new CStructSharp.Structure.Defines(new Identifier("OTHER"), new Literal(7)),
            ],
            evaluator);

        Dictionary<string, Expr> firstBaseline = resolver.Create(null);
        Dictionary<string, Expr> secondBaseline = resolver.Create(null);
        foreach (string name in firstBaseline.Keys)
        {
            Assert.AreSame(firstBaseline[name], secondBaseline[name], name);
        }

        Expr suppliedBase = new BinaryOp(
            BinaryOperatorType.Add,
            new Literal(2),
            new Literal(3));
        var supplied = new Dictionary<string, Expr> { ["BASE"] = suppliedBase, };

        Dictionary<string, Expr> overridden = resolver.Create(supplied);

        Assert.AreEqual(5, overridden["BASE"].Value);
        Assert.AreEqual(10, overridden["DOUBLE"].Value);
        Assert.AreEqual(11, overridden["SIZE"].Value);
        Assert.AreSame(firstBaseline["OTHER"], overridden["OTHER"]);
        Assert.AreNotSame(firstBaseline["BASE"], overridden["BASE"]);
        Assert.AreNotSame(firstBaseline["DOUBLE"], overridden["DOUBLE"]);
        Assert.AreNotSame(firstBaseline["SIZE"], overridden["SIZE"]);
        Assert.AreSame(suppliedBase, supplied["BASE"]);
    }

    /// <summary>Accepts exact evaluator boundaries and proves dependency/result caches share the finite session budget.</summary>
    [TestMethod]
    public void Evaluator_UsesExactLimitsDependencyCachesAndCompiledPrograms()
    {
        var exactEvaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(3, 3));
        Expr exactTree = new UnaryOp(
            UnaryOperatorType.Complement,
            new UnaryOp(UnaryOperatorType.Complement, new Literal(1)));
        exactEvaluator.Compile(exactTree);
        Assert.AreEqual(1, exactEvaluator.Evaluate(exactTree));
        Assert.Throws<ArgumentNullException>(() => exactEvaluator.Compile(null!));

        var nodeLimitedEvaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(8, 3));
        Assert.Throws<CStructLayoutException>(
            () => nodeLimitedEvaluator.Compile(
                new UnaryOp(
                    UnaryOperatorType.Complement,
                    new UnaryOp(
                        UnaryOperatorType.Complement,
                        new UnaryOp(UnaryOperatorType.Complement, new Literal(1))))));

        var depthLimitedEvaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(3, 10));
        Assert.Throws<CStructLayoutException>(
            () => depthLimitedEvaluator.Compile(
                new BinaryOp(
                    BinaryOperatorType.Add,
                    new Literal(1),
                    new UnaryOp(
                        UnaryOperatorType.Complement,
                        new UnaryOp(UnaryOperatorType.Complement, new Literal(1))))));

        Expr dependencies = new BinaryOp(
            BinaryOperatorType.Add,
            new Identifier("LEFT"),
            new Identifier("RIGHT"));
        CollectionAssert.AreEquivalent(
            new[] { "LEFT", "RIGHT", },
            exactEvaluator.GetDependencies(dependencies).ToArray());
        Assert.Throws<NotSupportedException>(
            () => exactEvaluator.Compile(
                new Call(new Identifier("method"), [new Literal(1),])));

        var exactDependencyEvaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(3, 3));
        var exactDependencyVariables = new Dictionary<string, Expr>
        {
            ["FIRST"] = new Identifier("SECOND"),
            ["SECOND"] = new Literal(7),
        };
        Assert.AreEqual(
            7,
            exactDependencyEvaluator.Evaluate(
                new Identifier("FIRST"),
                exactDependencyVariables));

        var cacheEvaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(8, 4));
        var cacheVariables = new Dictionary<string, Expr> { ["VALUE"] = new Literal(2), };
        Expr repeated = new BinaryOp(
            BinaryOperatorType.Add,
            new Identifier("VALUE"),
            new Identifier("VALUE"));
        Assert.AreEqual(4, cacheEvaluator.Evaluate(repeated, cacheVariables));

        var tripleCacheEvaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(8, 6));
        Expr repeatedThreeTimes = new BinaryOp(
            BinaryOperatorType.Add,
            repeated,
            new Identifier("VALUE"));
        Assert.AreEqual(6, tripleCacheEvaluator.Evaluate(repeatedThreeTimes, cacheVariables));

        var overBudgetVariables = new Dictionary<string, Expr>
        {
            ["LEFT"] = new Literal(1),
            ["RIGHT"] = new Literal(2),
        };
        Assert.Throws<CStructLayoutException>(
            () => cacheEvaluator.Evaluate(dependencies, overBudgetVariables));

        var sessionEvaluator = new ExpressionEvaluator(new ExpressionEvaluationLimits(8, 3));
        ExpressionEvaluator.ExpressionEvaluationSession session = sessionEvaluator.CreateSession();
        Assert.AreEqual(1, session.Evaluate(new Literal(1)));
        Assert.AreEqual(2, session.Evaluate(new Literal(2)));
        Assert.AreEqual(3, session.Evaluate(new Literal(3)));
        Assert.Throws<CStructLayoutException>(() => session.Evaluate(new Literal(4)));
    }

    /// <summary>Uses one runtime expression result across every operation, pointer traversal, and layout byte order.</summary>
    /// <param name="isLittleEndian">Whether the 16-bit array value is stored least-significant byte first.</param>
    [TestMethod]
    [DataRow(true)]
    [DataRow(false)]
    public void RuntimeCount_OperationsAgreeThroughPointersAndEndianness(bool isLittleEndian)
    {
        const string layout = """
                              struct target { uint16 values[COUNT]; uint8 tail; };
                              struct root { target *ptr; };
                              """;
        var cstruct = new CStruct(layout, pointerSize: 1, isLittleEndian: isLittleEndian);
        var variables = new Dictionary<string, Expr> { ["COUNT"] = new Literal(1), };
        byte first = isLittleEndian ? (byte)0x34 : (byte)0x12;
        byte second = isLittleEndian ? (byte)0x12 : (byte)0x34;
        byte[] bytes = [0x01, first, second, 0x7E,];

        using var parseStream = new MemoryStream((byte[])bytes.Clone());
        dynamic parsed = cstruct.ParseStream(parseStream, "root", variables);
        var pointer = (Pointer)parsed.ptr;
        dynamic target = pointer.Value!;
        Assert.AreEqual((ushort)0x1234, (ushort)target.values[0]);
        Assert.AreEqual((byte)0x7E, (byte)target.tail);

        using var selectedStream = new MemoryStream((byte[])bytes.Clone());
        dynamic selected = cstruct.ParseStream(selectedStream, "root.ptr.value", variables);
        Assert.AreEqual((ushort)0x1234, (ushort)selected.values[0]);
        Assert.AreEqual((byte)0x7E, (byte)selected.tail);

        using var debugStream = new MemoryStream((byte[])bytes.Clone());
        (List<DebugData> debug, _) = cstruct.ParseStreamWithDebug(debugStream, "root", variables);
        Assert.IsTrue(debug.Any(item => item.CurPos == 1 && item.EndPos == 3));
        Assert.IsTrue(debug.Any(item => item.CurPos == 3 && item.EndPos == 4));

        using var addressStream = new MemoryStream((byte[])bytes.Clone());
        Assert.AreEqual(1L, cstruct.ResolveAddress(addressStream, "root.ptr.value.values[0]", variables));
        Assert.AreEqual(0L, addressStream.Position);
        Assert.AreEqual(1, cstruct.GetDynamicArrayLength(addressStream, "root.ptr.value.values", variables));
        Assert.AreEqual(0L, addressStream.Position);

        var data = new Dictionary<string, object>
        {
            ["values"] = new ushort[] { 0x1234, },
            ["tail"] = (byte)0x7E,
        };
        byte[] targetBytes = [first, second, 0x7E,];
        CollectionAssert.AreEqual(targetBytes, cstruct.Serialize("target", data, variables));

        using var writeStream = new MemoryStream();
        cstruct.WriteStream(writeStream, "target", data, variables);
        CollectionAssert.AreEqual(targetBytes, writeStream.ToArray());

        using var updateStream = new MemoryStream((byte[])bytes.Clone());
        cstruct.UpdateStream(updateStream, "root.ptr.value.tail", (byte)0xA5, variables);
        CollectionAssert.AreEqual(new byte[] { 0x01, first, second, 0xA5, }, updateStream.ToArray());
        Assert.AreEqual(0L, updateStream.Position);
    }
}
