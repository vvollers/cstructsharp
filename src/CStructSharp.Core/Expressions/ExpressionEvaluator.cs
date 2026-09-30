namespace CStructSharp.Expressions;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using CStructSharp.Diagnostics;
using CStructSharp.Syntax;

/// <summary>
///     Compiles immutable expression trees once and executes them with bounded, checked signed 128-bit semantics
///     (<see cref="ExpressionArithmetic"/>). Every integer a layout field can hold up to 64 bits, and every 128-bit
///     value inside that range, is an exact operand; a literal or variable outside it fails when an expression uses it.
/// </summary>
[SuppressMessage(
    "StyleCop.CSharp.OrderingRules",
    "SA1201:ElementsMustAppearInTheCorrectOrder",
    Justification = "The evaluator keeps executable helpers before their closely related private VM types.")]
[SuppressMessage(
    "StyleCop.CSharp.OrderingRules",
    "SA1204:StaticElementsMustAppearBeforeInstanceElements",
    Justification = "Public session entry points precede private compiler and arithmetic implementation details.")]
internal sealed class ExpressionEvaluator
{
    private const int DefaultMaximumDepth = 256;
    private const int DefaultMaximumNodes = 100_000;
    private static readonly IReadOnlyDictionary<string, Expr> EmptyVariables =
        ImmutableDictionary<string, Expr>.Empty.WithComparers(StringComparer.Ordinal);

    private readonly ExpressionEvaluationLimits limits;
    private readonly ConditionalWeakTable<Expr, CompiledExpression> programs = new();

    /// <summary>Gets the finite evaluator used by the public expression model outside a compiled layout.</summary>
    public static ExpressionEvaluator Default { get; } =
        new(new ExpressionEvaluationLimits(DefaultMaximumDepth, DefaultMaximumNodes));

    /// <summary>Creates an evaluator whose limits are an immutable snapshot of the compilation settings.</summary>
    /// <param name="limits">The maximum tree depth and node work every compilation and evaluation may use.</param>
    public ExpressionEvaluator(ExpressionEvaluationLimits limits)
    {
        this.limits = limits;
    }

    /// <summary>Compiles and evaluates one expression against the supplied immutable name view.</summary>
    /// <param name="expression">The expression tree to compile (or fetch from the cache) and run.</param>
    /// <param name="variables">The names identifiers resolve to, or <see langword="null"/> for no names.</param>
    /// <returns>The signed 128-bit value of the expression.</returns>
    public Int128 Evaluate(Expr expression, IReadOnlyDictionary<string, Expr>? variables = null)
    {
        CompiledExpression program = this.GetProgram(expression);
        if (this.TryEvaluateSimple(program, variables ?? EmptyVariables, out Int128 result))
        {
            return result;
        }

        return this.CreateSession(variables).Evaluate(expression);
    }

    /// <summary>
    ///     Executes a scalar or one operator without session allocation when every dependency is a literal inside the
    ///     domain. Anything else (a nested operator, a dependency that is itself an expression or out of range) takes
    ///     the session path, which reports failures with the dependency's name.
    /// </summary>
    private bool TryEvaluateSimple(CompiledExpression program, IReadOnlyDictionary<string, Expr> variables, out Int128 result)
    {
        result = Int128.Zero;
        SessionInstruction[] code = program.Code;
        int operands = code.Length == 3 ? 2 : 1;
        if (code.Length is < 1 or > 3 ||
            (code.Length == 2 && code[1].Opcode is not (ExpressionOpcode.Negate or ExpressionOpcode.Complement or ExpressionOpcode.LogicalNot)) ||
            (code.Length == 3 && code[2].Opcode is ExpressionOpcode.Identifier or ExpressionOpcode.Literal or
                ExpressionOpcode.JumpIfFalse or ExpressionOpcode.JumpIfTrue or ExpressionOpcode.Negate or ExpressionOpcode.Complement or ExpressionOpcode.LogicalNot))
        {
            return false;
        }

        Int128 first = default;
        Int128 second = default;
        int firstDependency = -1;
        int extraNodes = 0;
        for (int index = 0; index < operands; index++)
        {
            SessionInstruction instruction = code[index];
            Int128 value;
            if (instruction.Opcode == ExpressionOpcode.Literal)
            {
                value = instruction.Value;
            }
            else if (instruction.Opcode == ExpressionOpcode.Identifier &&
                     variables.TryGetValue(program.Dependencies[instruction.Key], out Expr? expression) && expression is Literal { IsInDomain: true, } literal)
            {
                if (instruction.Depth + 1 > this.limits.MaximumDepth)
                {
                    throw new CStructLayoutException("Maximum expression evaluation depth exceeded.");
                }

                if (instruction.Key != firstDependency)
                {
                    extraNodes++;
                }

                firstDependency = instruction.Key;
                value = literal.Value;
            }
            else
            {
                return false;
            }

            if (index == 0)
            {
                first = value;
            }
            else
            {
                second = value;
            }
        }

        if (code.Length + extraNodes > this.limits.MaximumNodes)
        {
            throw new CStructLayoutException("Maximum expression evaluation work exceeded.");
        }

        result = code.Length switch
        {
            1 => first,
            2 => code[1].Opcode switch
            {
                ExpressionOpcode.Negate => ExpressionArithmetic.Negate(first),
                ExpressionOpcode.Complement => ExpressionArithmetic.Complement(first),
                _ => ExpressionArithmetic.LogicalNot(first),
            },
            _ => ExpressionArithmetic.Binary(code[2].Opcode, first, second),
        };
        return true;
    }

    /// <summary>
    ///     Evaluates one enum expression as an exact mathematical integer while retaining the configured depth/work limits.
    /// </summary>
    /// <param name="expression">The enum value expression to evaluate.</param>
    /// <param name="variables">The names identifiers resolve to, or <see langword="null"/> for no names.</param>
    /// <param name="shiftWidth">
    ///     The exclusive upper bound, in bits, for shift counts (usually the enum's underlying bit width).
    /// </param>
    /// <returns>The exact integer value, which may lie outside the signed 128-bit range.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="shiftWidth"/> is not positive.</exception>
    public BigInteger EvaluateExact(
        Expr expression,
        IReadOnlyDictionary<string, Expr>? variables,
        int shiftWidth)
    {
        if (shiftWidth <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(shiftWidth));
        }

        _ = this.GetProgram(expression);
        return new ExactEvaluationContext(
            this,
            variables ?? EmptyVariables,
            this.limits,
            shiftWidth).Evaluate(expression, 1);
    }

    /// <summary>Creates a session that shares one work counter, identifier cache, and cycle detector.</summary>
    /// <param name="variables">The names identifiers resolve to, or <see langword="null"/> for no names.</param>
    /// <returns>A new session bound to this evaluator's limits.</returns>
    public ExpressionEvaluationSession CreateSession(IReadOnlyDictionary<string, Expr>? variables = null)
    {
        return new ExpressionEvaluationSession(
            this,
            variables ?? EmptyVariables,
            this.limits);
    }

    /// <summary>Whether the tree contains a call node (<c>sizeof(T)</c>, <c>offsetof(T, f)</c>), which the compiler folds before compilation.</summary>
    /// <param name="expression">The root of the tree to search.</param>
    /// <returns>
    ///     <see langword="true"/> when any node in the tree is a call; otherwise <see langword="false"/>.
    /// </returns>
    public static bool ContainsCall(Expr expression)
    {
        var pending = new Stack<Expr>();
        pending.Push(expression);
        while (pending.Count > 0)
        {
            switch (pending.Pop())
            {
            case Call:
                return true;
            case UnaryOp unary:
                pending.Push(unary.Expr);
                break;
            case BinaryOp binary:
                pending.Push(binary.Left);
                pending.Push(binary.Right);
                break;
            case ConditionalExpr conditional:
                pending.Push(conditional.Condition);
                pending.Push(conditional.WhenTrue);
                pending.Push(conditional.WhenFalse);
                break;
            }
        }

        return false;
    }

    /// <summary>Compiles one expression now so unsupported or over-budget trees fail during layout construction.</summary>
    /// <param name="expression">The expression tree to compile and cache.</param>
    public void Compile(Expr expression)
    {
        _ = this.GetProgram(expression);
    }

    /// <summary>Returns the direct identifier dependencies recorded in the compiled immutable program.</summary>
    /// <param name="expression">The expression whose identifiers are listed; it is compiled if not cached.</param>
    /// <returns>The distinct identifier names the expression references directly, without transitive ones.</returns>
    public IReadOnlyCollection<string> GetDependencies(Expr expression)
    {
        return this.GetProgram(expression).Dependencies;
    }

    /// <summary>Gets the depth and work limits every compilation and evaluation of this evaluator applies.</summary>
    internal ExpressionEvaluationLimits Limits => this.limits;

    /// <summary>
    ///     Gets or creates the postfix program for an immutable expression node. Slot-indexed programs
    ///     (<c>ProgramExpression</c>) are translated from these programs, so both run the same instructions.
    /// </summary>
    /// <param name="expression">The expression tree to compile (or fetch from the cache).</param>
    /// <returns>The cached program.</returns>
    /// <exception cref="CStructLayoutException">The tree exceeds the depth or work limit.</exception>
    /// <exception cref="NotSupportedException">The tree contains a call or an unsupported node.</exception>
    internal CompiledExpression GetProgram(Expr expression)
    {
        if (expression is null)
        {
            throw new ArgumentNullException(nameof(expression));
        }

        return this.programs.GetValue(expression, value => CompileExpression(value, this.limits));
    }

    /// <summary>Builds a postfix program iteratively so compiling an adversarial tree cannot overflow the call stack.</summary>
    private static CompiledExpression CompileExpression(Expr root, ExpressionEvaluationLimits limits)
    {
        var instructions = new List<ExpressionInstruction>();
        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Stack<CompilationFrame>();
        pending.Push(new CompilationFrame(root, 1, false));
        int nodes = 0;

        while (pending.Count > 0)
        {
            CompilationFrame frame = pending.Pop();
            if (frame.BranchStage == 1)
            {
                instructions.Add(new ExpressionInstruction(
                    ((BinaryOp)frame.Expression).Type == BinaryOperatorType.LogicalAnd ? ExpressionOpcode.JumpIfFalse : ExpressionOpcode.JumpIfTrue,
                    Int128.Zero,
                    null,
                    frame.Depth,
                    frame.Patch));
                continue;
            }

            if (frame.BranchStage == 2)
            {
                instructions.Add(CreateOperatorInstruction(frame.Expression, frame.Depth));
                frame.Patch!.Target = instructions.Count;
                continue;
            }

            if (frame.BranchStage == 3)
            {
                // Conditional: the test is on the stack; skip to the else arm when it is zero.
                instructions.Add(new ExpressionInstruction(ExpressionOpcode.BranchIfFalse, Int128.Zero, null, frame.Depth, frame.Patch));
                continue;
            }

            if (frame.BranchStage == 4)
            {
                // End of the then arm: jump over the else arm, and the else arm starts here.
                instructions.Add(new ExpressionInstruction(ExpressionOpcode.Jump, Int128.Zero, null, frame.Depth, frame.ElsePatch));
                frame.Patch!.Target = instructions.Count;
                continue;
            }

            if (frame.BranchStage == 5)
            {
                // Both arms land here; the join is a no-op at run time and keeps the static stack count honest.
                instructions.Add(new ExpressionInstruction(ExpressionOpcode.Join, Int128.Zero, null, frame.Depth));
                frame.Patch!.Target = instructions.Count - 1;
                continue;
            }

            if (frame.EmitOperator)
            {
                instructions.Add(CreateOperatorInstruction(frame.Expression, frame.Depth));
                continue;
            }

            if (frame.Depth > limits.MaximumDepth)
            {
                throw new CStructLayoutException("Maximum expression evaluation depth exceeded.");
            }

            nodes++;
            if (nodes > limits.MaximumNodes)
            {
                throw new CStructLayoutException("Maximum expression evaluation work exceeded.");
            }

            switch (frame.Expression)
            {
            case Literal { IsInDomain: true, } literal:
                instructions.Add(
                    new ExpressionInstruction(
                        ExpressionOpcode.Literal,
                        literal.Value,
                        null,
                        frame.Depth));
                break;
            case Literal literal:
                // A literal beyond the domain compiles, so an enum or a definition may still spell it; the program
                // fails only when this instruction runs, with the message that names the literal.
                instructions.Add(
                    new ExpressionInstruction(
                        ExpressionOpcode.OutOfDomainLiteral,
                        Int128.Zero,
                        literal.DescribeOutsideDomain(),
                        frame.Depth));
                break;
            case NoneExpr:
                instructions.Add(
                    new ExpressionInstruction(
                        ExpressionOpcode.Literal,
                        Int128.Zero,
                        null,
                        frame.Depth));
                break;
            case Identifier identifier:
                dependencies.Add(identifier.Name);
                instructions.Add(
                    new ExpressionInstruction(
                        ExpressionOpcode.Identifier,
                        Int128.Zero,
                        identifier.Name,
                        frame.Depth,
                        Conditional: frame.Conditional));
                break;
            case UnaryOp unary:
                pending.Push(new CompilationFrame(unary, frame.Depth, true));
                pending.Push(new CompilationFrame(unary.Expr, frame.Depth + 1, false, Conditional: frame.Conditional));
                break;
            case BinaryOp binary when binary.Type is BinaryOperatorType.LogicalAnd or BinaryOperatorType.LogicalOr:
                var patch = new BranchPatch();
                pending.Push(new CompilationFrame(binary, frame.Depth, false, 2, patch));
                pending.Push(new CompilationFrame(binary.Right, frame.Depth + 1, false, Conditional: true));
                pending.Push(new CompilationFrame(binary, frame.Depth, false, 1, patch));
                pending.Push(new CompilationFrame(binary.Left, frame.Depth + 1, false, Conditional: frame.Conditional));
                break;
            case BinaryOp binary:
                pending.Push(new CompilationFrame(binary, frame.Depth, true));
                pending.Push(new CompilationFrame(binary.Right, frame.Depth + 1, false, Conditional: frame.Conditional));
                pending.Push(new CompilationFrame(binary.Left, frame.Depth + 1, false, Conditional: frame.Conditional));
                break;
            case ConditionalExpr conditional:
                var elsePatch = new BranchPatch();
                var endPatch = new BranchPatch();
                pending.Push(new CompilationFrame(conditional, frame.Depth, false, 5, endPatch));
                pending.Push(new CompilationFrame(conditional.WhenFalse, frame.Depth + 1, false, Conditional: true));
                pending.Push(new CompilationFrame(conditional, frame.Depth, false, 4, elsePatch, ElsePatch: endPatch));
                pending.Push(new CompilationFrame(conditional.WhenTrue, frame.Depth + 1, false, Conditional: true));
                pending.Push(new CompilationFrame(conditional, frame.Depth, false, 3, elsePatch));
                pending.Push(new CompilationFrame(conditional.Condition, frame.Depth + 1, false, Conditional: frame.Conditional));
                break;
            case Call:
                throw new NotSupportedException("Expression calls are parsed but are not supported.");
            default:
                throw new NotSupportedException(
                    "Unsupported expression node type: " + frame.Expression.GetType().Name);
            }
        }

        return new CompiledExpression(
            instructions,
            dependencies.Count == 0 ? Array.Empty<string>() : [.. dependencies,]);
    }

    /// <summary>Maps one parsed operator node to its stack-machine instruction.</summary>
    private static ExpressionInstruction CreateOperatorInstruction(Expr expression, int depth)
    {
        ExpressionOpcode opcode = expression switch
        {
            UnaryOp { Type: UnaryOperatorType.LogicalNot, } => ExpressionOpcode.LogicalNot,
            UnaryOp { Type: UnaryOperatorType.Complement, } => ExpressionOpcode.Complement,
            UnaryOp { Type: UnaryOperatorType.Neg, } => ExpressionOpcode.Negate,
            UnaryOp unary => throw new InvalidOperationException("Unknown unary operator: " + unary.Type),
            BinaryOp { Type: BinaryOperatorType.LogicalAnd, } => ExpressionOpcode.LogicalAnd,
            BinaryOp { Type: BinaryOperatorType.LogicalOr, } => ExpressionOpcode.LogicalOr,
            BinaryOp { Type: BinaryOperatorType.Equal, } => ExpressionOpcode.Equal,
            BinaryOp { Type: BinaryOperatorType.NotEqual, } => ExpressionOpcode.NotEqual,
            BinaryOp { Type: BinaryOperatorType.Less, } => ExpressionOpcode.Less,
            BinaryOp { Type: BinaryOperatorType.LessOrEqual, } => ExpressionOpcode.LessOrEqual,
            BinaryOp { Type: BinaryOperatorType.Greater, } => ExpressionOpcode.Greater,
            BinaryOp { Type: BinaryOperatorType.GreaterOrEqual, } => ExpressionOpcode.GreaterOrEqual,
            BinaryOp { Type: BinaryOperatorType.Add, } => ExpressionOpcode.Add,
            BinaryOp { Type: BinaryOperatorType.Minus, } => ExpressionOpcode.Subtract,
            BinaryOp { Type: BinaryOperatorType.And, } => ExpressionOpcode.And,
            BinaryOp { Type: BinaryOperatorType.Div, } => ExpressionOpcode.Divide,
            BinaryOp { Type: BinaryOperatorType.Mul, } => ExpressionOpcode.Multiply,
            BinaryOp { Type: BinaryOperatorType.Or, } => ExpressionOpcode.Or,
            BinaryOp { Type: BinaryOperatorType.ShiftLeft, } => ExpressionOpcode.ShiftLeft,
            BinaryOp { Type: BinaryOperatorType.ShiftRight, } => ExpressionOpcode.ShiftRight,
            BinaryOp { Type: BinaryOperatorType.Mod, } => ExpressionOpcode.Modulo,
            BinaryOp { Type: BinaryOperatorType.Xor, } => ExpressionOpcode.Xor,
            BinaryOp binary => throw new InvalidOperationException("Unknown binary operator: " + binary.Type),
            _ => throw new InvalidOperationException("Expression node has no executable operator."),
        };
        return new ExpressionInstruction(opcode, Int128.Zero, null, depth);
    }

    /// <summary>
    ///     Evaluates expressions over a variable dictionary while sharing a finite work budget across named dependencies:
    ///     the session rules (<see cref="ExpressionSession{TKey, TProgram, TResolver}"/>) with names resolved through the
    ///     variables and compiled through the evaluator's program cache.
    /// </summary>
    internal sealed class ExpressionEvaluationSession : ExpressionSession<string, CompiledExpression, DictionaryResolver>
    {
        private readonly ExpressionEvaluator evaluator;

        /// <summary>Creates one bounded evaluation session.</summary>
        /// <param name="evaluator">The evaluator whose compiled-program cache the session uses.</param>
        /// <param name="variables">
        ///     The names identifiers resolve to; each is evaluated at most once per session.
        /// </param>
        /// <param name="limits">The depth and total work limits shared by every evaluation in the session.</param>
        public ExpressionEvaluationSession(
            ExpressionEvaluator evaluator,
            IReadOnlyDictionary<string, Expr> variables,
            ExpressionEvaluationLimits limits)
            : base(new DictionaryResolver(evaluator, variables, limits))
        {
            this.evaluator = evaluator;
        }

        /// <summary>Evaluates one root while retaining the session's dependency values and total work counter.</summary>
        /// <param name="expression">The expression tree to compile (or fetch from the cache) and run.</param>
        /// <returns>The signed 128-bit value of the expression.</returns>
        public Int128 Evaluate(Expr expression) => this.EvaluateRoot(this.evaluator.GetProgram(expression));
    }

    /// <summary>Resolves a dictionary session's names: a name's program is its variable's expression, compiled through the evaluator's cache.</summary>
    internal readonly struct DictionaryResolver : IExpressionSessionResolver<string, CompiledExpression>
    {
        private readonly ExpressionEvaluator evaluator;
        private readonly IReadOnlyDictionary<string, Expr> variables;

        /// <summary>Creates the resolver of one session.</summary>
        /// <param name="evaluator">The evaluator whose program cache compiles the variables.</param>
        /// <param name="variables">The names identifiers resolve to.</param>
        /// <param name="limits">The session's limits.</param>
        public DictionaryResolver(ExpressionEvaluator evaluator, IReadOnlyDictionary<string, Expr> variables, ExpressionEvaluationLimits limits)
        {
            this.evaluator = evaluator;
            this.variables = variables;
            this.Limits = limits;
        }

        /// <inheritdoc/>
        public ExpressionEvaluationLimits Limits { get; }

        /// <inheritdoc/>
        public SessionInstruction[] Code(CompiledExpression program) => program.Code;

        /// <inheritdoc/>
        public int MaximumDepth(CompiledExpression program) => program.MaximumDepth;

        /// <inheritdoc/>
        public int MaximumStackSize(CompiledExpression program) => Math.Max(1, program.MaximumStackSize);

        /// <inheritdoc/>
        public SessionReference[] Prelude(CompiledExpression program) => program.Prelude;

        /// <inheritdoc/>
        public string Key(CompiledExpression program, int reference) => program.Dependencies[reference];

        /// <inheritdoc/>
        public string Name(string key) => key;

        /// <inheritdoc/>
        public bool IsDefined(string key) => this.variables.TryGetValue(key, out Expr? value) && value is not UnusableVariable;

        /// <inheritdoc/>
        public CompiledExpression? Dependency(string key) => this.evaluator.GetProgram(this.Variable(key));

        /// <inheritdoc/>
        public CompiledExpression? Evaluation(string key, out Int128 leaf)
        {
            Expr expression = this.Variable(key);
            if (expression is Literal { IsInDomain: false, } wide)
            {
                // A constant beyond the domain (an unsigned 128-bit enum member, a define such as 1 << 127) is
                // reported under the name the expression used rather than as an anonymous literal.
                throw new InvalidOperationException(WideValueVariable.DescribeOutOfRange(key, wide.ExactValue));
            }

            leaf = Int128.Zero;
            return this.evaluator.GetProgram(expression);
        }

        /// <summary>Returns a name's variable, failing for an undefined name and for an unusable value.</summary>
        /// <param name="key">The name.</param>
        /// <returns>The variable's expression.</returns>
        private Expr Variable(string key)
        {
            if (!this.variables.TryGetValue(key, out Expr? expression))
            {
                throw new KeyNotFoundException("Undefined expression identifier: " + key);
            }

            if (expression is UnusableVariable unusable)
            {
                throw unusable.CreateFailure(key);
            }

            return expression;
        }
    }

    /// <summary>Evaluates exact enum expressions without adding arbitrary-precision cost to ordinary 128-bit expressions.</summary>
    private sealed class ExactEvaluationContext
    {
        private readonly HashSet<string> activeIdentifiers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, BigInteger> identifierValues = new(StringComparer.Ordinal);
        private readonly ExpressionEvaluator evaluator;
        private readonly ExpressionEvaluationLimits limits;
        private readonly int shiftWidth;
        private readonly IReadOnlyDictionary<string, Expr> variables;
        private int executedNodes;

        public ExactEvaluationContext(
            ExpressionEvaluator evaluator,
            IReadOnlyDictionary<string, Expr> variables,
            ExpressionEvaluationLimits limits,
            int shiftWidth)
        {
            this.evaluator = evaluator;
            this.variables = variables;
            this.limits = limits;
            this.shiftWidth = shiftWidth;
        }

        public BigInteger Evaluate(Expr expression, int depth)
        {
            this.Charge(depth);
            return expression switch
            {
                Literal literal => literal.ExactValue,
                NoneExpr => BigInteger.Zero,
                Identifier identifier => this.EvaluateIdentifier(identifier.Name, depth + 1),
                UnaryOp unary => this.EvaluateUnary(unary, depth),
                BinaryOp binary => this.EvaluateBinary(binary, depth),
                ConditionalExpr conditional => this.Evaluate(conditional.Condition, depth + 1).IsZero
                                                   ? this.Evaluate(conditional.WhenFalse, depth + 1)
                                                   : this.Evaluate(conditional.WhenTrue, depth + 1),
                Call => throw new NotSupportedException("Expression calls are parsed but are not supported."),
                _ => throw new NotSupportedException(
                    "Unsupported expression node type: " + expression.GetType().Name),
            };
        }

        private void Charge(int depth)
        {
            if (depth > this.limits.MaximumDepth)
            {
                throw new CStructLayoutException("Maximum expression evaluation depth exceeded.");
            }

            this.executedNodes++;
            if (this.executedNodes > this.limits.MaximumNodes)
            {
                throw new CStructLayoutException("Maximum expression evaluation work exceeded.");
            }
        }

        private BigInteger EvaluateIdentifier(string name, int depth)
        {
            if (this.identifierValues.TryGetValue(name, out BigInteger known))
            {
                return known;
            }

            if (!this.variables.TryGetValue(name, out Expr? expression))
            {
                throw new KeyNotFoundException("Undefined expression identifier: " + name);
            }

            if (!this.activeIdentifiers.Add(name))
            {
                throw new CStructLayoutException(
                    "Circular expression dependency detected at: " + name);
            }

            try
            {
                _ = this.evaluator.GetProgram(expression);
                BigInteger result = this.Evaluate(expression, depth);
                this.identifierValues.Add(name, result);
                return result;
            }
            finally
            {
                this.activeIdentifiers.Remove(name);
            }
        }

        private BigInteger EvaluateUnary(UnaryOp unary, int depth)
        {
            BigInteger value = this.Evaluate(unary.Expr, depth + 1);
            return unary.Type switch
            {
                UnaryOperatorType.LogicalNot => value.IsZero ? BigInteger.One : BigInteger.Zero,
                UnaryOperatorType.Complement => ~value,
                UnaryOperatorType.Neg => -value,
                _ => throw new InvalidOperationException("Unknown unary operator: " + unary.Type),
            };
        }

        private BigInteger EvaluateBinary(BinaryOp binary, int depth)
        {
            BigInteger left = this.Evaluate(binary.Left, depth + 1);
            if (binary.Type == BinaryOperatorType.LogicalAnd && left.IsZero)
            {
                return BigInteger.Zero;
            }

            if (binary.Type == BinaryOperatorType.LogicalOr && !left.IsZero)
            {
                return BigInteger.One;
            }

            BigInteger right = this.Evaluate(binary.Right, depth + 1);
            return binary.Type switch
            {
                BinaryOperatorType.LogicalAnd => right.IsZero ? BigInteger.Zero : BigInteger.One,
                BinaryOperatorType.LogicalOr => right.IsZero ? BigInteger.Zero : BigInteger.One,
                BinaryOperatorType.Equal => left == right ? BigInteger.One : BigInteger.Zero,
                BinaryOperatorType.NotEqual => left != right ? BigInteger.One : BigInteger.Zero,
                BinaryOperatorType.Less => left < right ? BigInteger.One : BigInteger.Zero,
                BinaryOperatorType.LessOrEqual => left <= right ? BigInteger.One : BigInteger.Zero,
                BinaryOperatorType.Greater => left > right ? BigInteger.One : BigInteger.Zero,
                BinaryOperatorType.GreaterOrEqual => left >= right ? BigInteger.One : BigInteger.Zero,
                BinaryOperatorType.Add => left + right,
                BinaryOperatorType.Minus => left - right,
                BinaryOperatorType.And => left & right,
                BinaryOperatorType.Div => left / right,
                BinaryOperatorType.Mul => left * right,
                BinaryOperatorType.Or => left | right,
                BinaryOperatorType.ShiftLeft => left << this.ValidateShiftCount(right),
                BinaryOperatorType.ShiftRight => left >> this.ValidateShiftCount(right),
                BinaryOperatorType.Mod => right.IsZero ? throw new DivideByZeroException() : BigInteger.Remainder(left, right),
                BinaryOperatorType.Xor => left ^ right,
                _ => throw new InvalidOperationException("Unknown binary operator: " + binary.Type),
            };
        }

        private int ValidateShiftCount(BigInteger count)
        {
            if (count < BigInteger.Zero || count >= this.shiftWidth)
            {
                throw new InvalidOperationException(
                    $"Enum expression shift count must be between 0 and {this.shiftWidth - 1}.");
            }

            return (int)count;
        }
    }

    /// <summary>Stores one immutable postfix program and its direct dependencies.</summary>
    internal sealed class CompiledExpression
    {
        /// <summary>
        ///     Stores a program and measures it once: its deepest syntax level, the largest value stack it needs, and
        ///     the identifier references the dependency validation walks.
        /// </summary>
        /// <param name="instructions">The postfix instructions, in execution order.</param>
        /// <param name="dependencies">The distinct identifier names the program reads directly.</param>
        /// <exception cref="InvalidOperationException">The instructions do not form a valid stack program.</exception>
        public CompiledExpression(
            List<ExpressionInstruction> instructions,
            string[] dependencies)
        {
            this.Dependencies = dependencies;
            int maximumDepth = 0;
            int maximumStackSize = 0;
            int stackSize = 0;
            var identifierReferences = new List<SessionReference>();
            foreach (ExpressionInstruction instruction in instructions)
            {
                maximumDepth = Math.Max(maximumDepth, instruction.Depth);
                switch (instruction.Opcode)
                {
                case ExpressionOpcode.Literal:
                case ExpressionOpcode.OutOfDomainLiteral:
                    stackSize++;
                    break;
                case ExpressionOpcode.Identifier:
                    stackSize++;
                    if (!instruction.Conditional)
                    {
                        identifierReferences.Add(new SessionReference(
                            DependencyIndex(
                                dependencies,
                                instruction.Name ?? throw new InvalidOperationException("Identifier instruction has no name.")),
                            instruction.Depth));
                    }

                    break;
                case ExpressionOpcode.JumpIfFalse:
                case ExpressionOpcode.JumpIfTrue:
                case ExpressionOpcode.LogicalNot:
                case ExpressionOpcode.Complement:
                case ExpressionOpcode.Negate:
                    break;
                case ExpressionOpcode.BranchIfFalse:
                    // The test is consumed at run time, but counting it as still present keeps the count positive
                    // through the then arm; Jump and Join take the two surplus entries back off, so a conditional
                    // nets one pushed value and the maximum only over-estimates by one slot.
                    break;
                case ExpressionOpcode.Jump:
                case ExpressionOpcode.Join:
                    stackSize--;
                    break;
                default:
                    stackSize--;
                    break;
                }

                if (stackSize <= 0)
                {
                    throw new InvalidOperationException("Compiled expression has an invalid stack transition.");
                }

                maximumStackSize = Math.Max(maximumStackSize, stackSize);
            }

            if (stackSize != 1)
            {
                throw new InvalidOperationException("Compiled expression does not leave exactly one stack value.");
            }

            this.MaximumDepth = maximumDepth;
            this.MaximumStackSize = maximumStackSize;

            // The session runs the instructions with each identifier as an index into Dependencies and each jump target
            // resolved; the prelude lists the identifiers outside the arms in the same form.
            this.Code = new SessionInstruction[instructions.Count];
            for (int position = 0; position < instructions.Count; position++)
            {
                ExpressionInstruction instruction = instructions[position];
                this.Code[position] = new SessionInstruction(
                    instruction.Opcode,
                    instruction.Value,
                    instruction.Opcode == ExpressionOpcode.Identifier ? DependencyIndex(dependencies, instruction.Name!) : instruction.Patch?.Target ?? 0,
                    instruction.Depth,
                    instruction.Conditional,
                    instruction.Opcode == ExpressionOpcode.OutOfDomainLiteral ? instruction.Name : null);
            }

            this.Prelude = identifierReferences.ToArray();
        }

        /// <summary>
        ///     Returns a name's index in the program's dependencies. A program reads few distinct names, so a linear search
        ///     costs less than building a lookup table for every compiled expression.
        /// </summary>
        /// <param name="dependencies">The program's distinct identifier names.</param>
        /// <param name="name">A name the program reads.</param>
        /// <returns>The index.</returns>
        private static int DependencyIndex(string[] dependencies, string name)
        {
            for (int index = 0; index < dependencies.Length; index++)
            {
                if (string.Equals(dependencies[index], name, StringComparison.Ordinal))
                {
                    return index;
                }
            }

            throw new InvalidOperationException("Identifier instruction names no dependency: " + name);
        }

        /// <summary>Gets the distinct identifier names the program reads directly.</summary>
        public string[] Dependencies { get; }

        /// <summary>Gets the instructions a session runs: each identifier an index into <see cref="Dependencies"/>, each jump target resolved.</summary>
        public SessionInstruction[] Code { get; }

        /// <summary>
        ///     Gets the identifier occurrences outside short-circuit and conditional arms, in instruction order, each an index
        ///     into <see cref="Dependencies"/> and its level: the names a session validates before it evaluates the program.
        /// </summary>
        public SessionReference[] Prelude { get; }

        /// <summary>Gets the deepest syntax level of any instruction (the root is level 1).</summary>
        public int MaximumDepth { get; }

        /// <summary>Gets the largest number of values the program's stack holds at once (over-estimated by one for <c>?:</c>).</summary>
        public int MaximumStackSize { get; }
    }

    /// <summary>Represents one iterative compilation frame.</summary>
    private readonly record struct CompilationFrame(Expr Expression, int Depth, bool EmitOperator, int BranchStage = 0, BranchPatch? Patch = null, bool Conditional = false, BranchPatch? ElsePatch = null);

    /// <summary>The target of a jump, filled in once the compiler has emitted the instruction the jump lands on.</summary>
    internal sealed class BranchPatch
    {
        /// <summary>Gets or sets the index of the instruction the jump continues at.</summary>
        public int Target { get; set; }
    }

    /// <summary>Represents one postfix stack-machine instruction.</summary>
    /// <param name="Opcode">The operation.</param>
    /// <param name="Value">The constant a <see cref="ExpressionOpcode.Literal"/> pushes.</param>
    /// <param name="Name">
    ///     The name an <see cref="ExpressionOpcode.Identifier"/> reads, or the message an
    ///     <see cref="ExpressionOpcode.OutOfDomainLiteral"/> fails with.
    /// </param>
    /// <param name="Depth">The syntax level of the node the instruction came from (the root is level 1).</param>
    /// <param name="Patch">The jump target of a jump or branch instruction.</param>
    /// <param name="Conditional">Whether an identifier sits inside a short-circuit or <c>?:</c> arm, so it is validated only when selected.</param>
    internal readonly record struct ExpressionInstruction(
        ExpressionOpcode Opcode,
        Int128 Value,
        string? Name,
        int Depth,
        BranchPatch? Patch = null,
        bool Conditional = false);

    /// <summary>Lists the executable operations supported by the CStructSharp expression subset.</summary>
    internal enum ExpressionOpcode
    {
        /// <summary>
        ///     Short-circuits <c>&amp;&amp;</c>: when the top value is zero, replaces it with 0 and jumps past the
        ///     right operand; otherwise leaves the stack unchanged and continues.
        /// </summary>
        JumpIfFalse,

        /// <summary>
        ///     Short-circuits <c>||</c>: when the top value is non-zero, replaces it with 1 and jumps past the right
        ///     operand; otherwise leaves the stack unchanged and continues.
        /// </summary>
        JumpIfTrue,

        /// <summary>Pops two values and pushes 1 when both are non-zero, otherwise 0.</summary>
        LogicalAnd,

        /// <summary>Pops two values and pushes 1 when either is non-zero, otherwise 0.</summary>
        LogicalOr,

        /// <summary>Replaces the top value with 1 when it is zero, otherwise 0.</summary>
        LogicalNot,

        /// <summary>Pops two values and pushes 1 when they are equal, otherwise 0.</summary>
        Equal,

        /// <summary>Pops two values and pushes 1 when they differ, otherwise 0.</summary>
        NotEqual,

        /// <summary>Pops two values and pushes 1 when the left is less than the right, otherwise 0.</summary>
        Less,

        /// <summary>Pops two values and pushes 1 when the left is at most the right, otherwise 0.</summary>
        LessOrEqual,

        /// <summary>Pops two values and pushes 1 when the left is greater than the right, otherwise 0.</summary>
        Greater,

        /// <summary>Pops two values and pushes 1 when the left is at least the right, otherwise 0.</summary>
        GreaterOrEqual,

        /// <summary>Pushes the instruction's constant value.</summary>
        Literal,

        /// <summary>
        ///     Stands for a literal outside the signed 128-bit domain: it fails, with the instruction's name text as
        ///     the message, when it is reached.
        /// </summary>
        OutOfDomainLiteral,

        /// <summary>Evaluates the named dependency (once per session) and pushes its value.</summary>
        Identifier,

        /// <summary>Replaces the top value with its bitwise complement (<c>~</c>).</summary>
        Complement,

        /// <summary>Replaces the top value with its checked arithmetic negation (unary <c>-</c>).</summary>
        Negate,

        /// <summary>Pops two values and pushes their checked sum.</summary>
        Add,

        /// <summary>Pops two values and pushes the checked difference of left minus right.</summary>
        Subtract,

        /// <summary>Pops two values and pushes their bitwise AND (<c>&amp;</c>).</summary>
        And,

        /// <summary>Pops two values and pushes the truncating quotient; a zero divisor fails.</summary>
        Divide,

        /// <summary>Pops two values and pushes their checked product.</summary>
        Multiply,

        /// <summary>Pops two values and pushes their bitwise OR (<c>|</c>).</summary>
        Or,

        /// <summary>
        ///     Pops two values and pushes the left shifted left by a bit count from 0 to 127, rejecting overflow.
        /// </summary>
        ShiftLeft,

        /// <summary>
        ///     Pops two values and pushes the left arithmetically shifted right by a bit count from 0 to 127.
        /// </summary>
        ShiftRight,

        /// <summary>
        ///     Pops two values and pushes the C# remainder of left divided by right; a zero divisor fails.
        /// </summary>
        Modulo,

        /// <summary>Pops two values and pushes their bitwise exclusive OR (<c>^</c>).</summary>
        Xor,

        /// <summary>Pops the condition of <c>?:</c> and jumps to the else arm when it is zero.</summary>
        BranchIfFalse,

        /// <summary>Jumps unconditionally, used to skip the else arm after the then arm of <c>?:</c>.</summary>
        Jump,

        /// <summary>Marks the point where both arms of <c>?:</c> meet; it does nothing at run time.</summary>
        Join,
    }
}
