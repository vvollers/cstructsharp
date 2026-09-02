namespace CStructSharp;

using System;
using System.Buffers;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using System.Runtime.CompilerServices;
using CStructSharp.Structure;

/// <summary>Compiles immutable expression trees once and executes them with bounded checked signed-Int32 semantics.</summary>
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
    public ExpressionEvaluator(ExpressionEvaluationLimits limits)
    {
        this.limits = limits;
    }

    /// <summary>Compiles and evaluates one expression against the supplied immutable name view.</summary>
    public int Evaluate(Expr expression, IReadOnlyDictionary<string, Expr>? variables = null)
    {
        return this.CreateSession(variables).Evaluate(expression);
    }

    /// <summary>
    ///     Evaluates one enum expression as an exact mathematical integer while retaining the configured depth/work limits.
    /// </summary>
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
    public ExpressionEvaluationSession CreateSession(IReadOnlyDictionary<string, Expr>? variables = null)
    {
        return new ExpressionEvaluationSession(
            this,
            variables ?? EmptyVariables,
            this.limits);
    }

    /// <summary>Compiles one expression now so unsupported or over-budget trees fail during layout construction.</summary>
    public void Compile(Expr expression)
    {
        _ = this.GetProgram(expression);
    }

    /// <summary>Returns the direct identifier dependencies recorded in the compiled immutable program.</summary>
    public IReadOnlyCollection<string> GetDependencies(Expr expression)
    {
        return this.GetProgram(expression).Dependencies;
    }

    /// <summary>Gets or creates the postfix program for an immutable expression node.</summary>
    private CompiledExpression GetProgram(Expr expression)
    {
        ArgumentNullException.ThrowIfNull(expression);
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
            case Literal literal:
                instructions.Add(
                    new ExpressionInstruction(
                        ExpressionOpcode.Literal,
                        literal.Int32Projection,
                        null,
                        frame.Depth));
                break;
            case NoneExpr:
                instructions.Add(
                    new ExpressionInstruction(
                        ExpressionOpcode.Literal,
                        BigInteger.Zero,
                        null,
                        frame.Depth));
                break;
            case Identifier identifier:
                dependencies.Add(identifier.Name);
                instructions.Add(
                    new ExpressionInstruction(
                        ExpressionOpcode.Identifier,
                        BigInteger.Zero,
                        identifier.Name,
                        frame.Depth));
                break;
            case UnaryOp unary:
                pending.Push(new CompilationFrame(unary, frame.Depth, true));
                pending.Push(new CompilationFrame(unary.Expr, frame.Depth + 1, false));
                break;
            case BinaryOp binary:
                pending.Push(new CompilationFrame(binary, frame.Depth, true));
                pending.Push(new CompilationFrame(binary.Right, frame.Depth + 1, false));
                pending.Push(new CompilationFrame(binary.Left, frame.Depth + 1, false));
                break;
            case Call:
                throw new NotSupportedException("Expression calls are parsed but are not supported.");
            default:
                throw new NotSupportedException(
                    "Unsupported expression node type: " + frame.Expression.GetType().Name);
            }
        }

        return new CompiledExpression(
            instructions.ToArray(),
            dependencies.Count == 0 ? Array.Empty<string>() : [.. dependencies,]);
    }

    /// <summary>Maps one parsed operator node to its stack-machine instruction.</summary>
    private static ExpressionInstruction CreateOperatorInstruction(Expr expression, int depth)
    {
        ExpressionOpcode opcode = expression switch
        {
            UnaryOp { Type: UnaryOperatorType.Complement, } => ExpressionOpcode.Complement,
            UnaryOp { Type: UnaryOperatorType.Neg, } => ExpressionOpcode.Negate,
            UnaryOp unary => throw new InvalidOperationException("Unknown unary operator: " + unary.Type),
            BinaryOp { Type: BinaryOperatorType.Add, } => ExpressionOpcode.Add,
            BinaryOp { Type: BinaryOperatorType.Minus, } => ExpressionOpcode.Subtract,
            BinaryOp { Type: BinaryOperatorType.And, } => ExpressionOpcode.And,
            BinaryOp { Type: BinaryOperatorType.Div, } => ExpressionOpcode.Divide,
            BinaryOp { Type: BinaryOperatorType.Mul, } => ExpressionOpcode.Multiply,
            BinaryOp { Type: BinaryOperatorType.Or, } => ExpressionOpcode.Or,
            BinaryOp { Type: BinaryOperatorType.ShiftLeft, } => ExpressionOpcode.ShiftLeft,
            BinaryOp { Type: BinaryOperatorType.ShiftRight, } => ExpressionOpcode.ShiftRight,
            BinaryOp binary => throw new InvalidOperationException("Unknown binary operator: " + binary.Type),
            _ => throw new InvalidOperationException("Expression node has no executable operator."),
        };
        return new ExpressionInstruction(opcode, BigInteger.Zero, null, depth);
    }

    /// <summary>Executes compiled expressions while sharing a finite work budget across named dependencies.</summary>
    internal sealed class ExpressionEvaluationSession
    {
        private readonly HashSet<string> activeIdentifiers = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> identifierValues = new(StringComparer.Ordinal);
        private readonly ExpressionEvaluator evaluator;
        private readonly ExpressionEvaluationLimits limits;
        private readonly Dictionary<string, int> validatedIdentifierDepths = new(StringComparer.Ordinal);
        private readonly IReadOnlyDictionary<string, Expr> variables;
        private int executedNodes;
        private int validatedNodes;

        /// <summary>Creates one bounded evaluation session.</summary>
        public ExpressionEvaluationSession(
            ExpressionEvaluator evaluator,
            IReadOnlyDictionary<string, Expr> variables,
            ExpressionEvaluationLimits limits)
        {
            this.evaluator = evaluator;
            this.variables = variables;
            this.limits = limits;
        }

        /// <summary>Evaluates one root while retaining the session's dependency values and total work counter.</summary>
        public int Evaluate(Expr expression)
        {
            CompiledExpression program = this.evaluator.GetProgram(expression);
            this.ValidateDependencyDepth(program);
            return this.EvaluateProgram(program, 0);
        }

        /// <summary>Checks complete dependency paths independently of result-cache order and without recursive calls.</summary>
        private void ValidateDependencyDepth(CompiledExpression root)
        {
            var activeIdentifiers = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<DependencyValidationFrame>();
            this.ChargeValidatedNodes(root);
            pending.Push(new DependencyValidationFrame(root, 0, 0, null));

            while (pending.Count > 0)
            {
                DependencyValidationFrame frame = pending.Pop();
                if (frame.NextReference >= frame.Program.IdentifierReferences.Length)
                {
                    if (frame.EnteredIdentifier is not null)
                    {
                        activeIdentifiers.Remove(frame.EnteredIdentifier);
                    }

                    continue;
                }

                ExpressionIdentifierReference reference =
                    frame.Program.IdentifierReferences[frame.NextReference];
                pending.Push(frame with { NextReference = frame.NextReference + 1, });
                int dependencyDepth = frame.BaseDepth + reference.Depth;
                if (!activeIdentifiers.Add(reference.Name))
                {
                    throw new CStructLayoutException(
                        "Circular expression dependency detected at: " + reference.Name);
                }

                if (!this.variables.TryGetValue(reference.Name, out Expr? expression))
                {
                    throw new KeyNotFoundException("Undefined expression identifier: " + reference.Name);
                }

                CompiledExpression dependency = this.evaluator.GetProgram(expression);
                if (dependencyDepth + dependency.MaximumDepth > this.limits.MaximumDepth)
                {
                    throw new CStructLayoutException("Maximum expression evaluation depth exceeded.");
                }

                if (this.validatedIdentifierDepths.TryGetValue(reference.Name, out int validatedDepth) &&
                    validatedDepth >= dependencyDepth)
                {
                    activeIdentifiers.Remove(reference.Name);
                    continue;
                }

                this.validatedIdentifierDepths[reference.Name] = dependencyDepth;
                this.ChargeValidatedNodes(dependency);
                pending.Push(new DependencyValidationFrame(
                    dependency,
                    dependencyDepth,
                    0,
                    reference.Name));
            }
        }

        /// <summary>Bounds the iterative dependency-validation walk to the same session work setting.</summary>
        private void ChargeValidatedNodes(CompiledExpression program)
        {
            this.validatedNodes = checked(this.validatedNodes + program.Instructions.Length);
            if (this.validatedNodes > this.limits.MaximumNodes)
            {
                throw new CStructLayoutException("Maximum expression evaluation work exceeded.");
            }
        }

        /// <summary>Runs one postfix program and recursively resolves only bounded identifier dependencies.</summary>
        private int EvaluateProgram(CompiledExpression program, int dependencyDepth)
        {
            if (dependencyDepth + program.MaximumDepth > this.limits.MaximumDepth)
            {
                throw new CStructLayoutException("Maximum expression evaluation depth exceeded.");
            }

            int[] values = ArrayPool<int>.Shared.Rent(Math.Max(1, program.MaximumStackSize));
            int valueCount = 0;
            try
            {
                foreach (ExpressionInstruction instruction in program.Instructions)
                {
                    this.executedNodes++;
                    if (this.executedNodes > this.limits.MaximumNodes)
                    {
                        throw new CStructLayoutException("Maximum expression evaluation work exceeded.");
                    }

                    switch (instruction.Opcode)
                    {
                    case ExpressionOpcode.Literal:
                        values[valueCount++] = checked((int)instruction.Value);
                        break;
                    case ExpressionOpcode.Identifier:
                        values[valueCount++] = this.EvaluateIdentifier(
                            instruction.Name ??
                            throw new InvalidOperationException("Identifier instruction has no name."),
                            dependencyDepth + instruction.Depth);
                        break;
                    case ExpressionOpcode.Complement:
                        values[valueCount - 1] = ~values[valueCount - 1];
                        break;
                    case ExpressionOpcode.Negate:
                        values[valueCount - 1] = checked(-values[valueCount - 1]);
                        break;
                    default:
                        int right = values[--valueCount];
                        int leftIndex = valueCount - 1;
                        values[leftIndex] = EvaluateBinary(instruction.Opcode, values[leftIndex], right);
                        break;
                    }
                }

                if (valueCount != 1)
                {
                    throw new InvalidOperationException("Compiled expression did not produce exactly one value.");
                }

                return values[0];
            }
            finally
            {
                ArrayPool<int>.Shared.Return(values);
            }
        }

        /// <summary>Evaluates one named expression once and rejects dependency cycles at their first repeated name.</summary>
        private int EvaluateIdentifier(string name, int dependencyDepth)
        {
            if (this.identifierValues.TryGetValue(name, out int known))
            {
                return known;
            }

            if (!this.variables.TryGetValue(name, out Expr? expression))
            {
                throw new KeyNotFoundException("Undefined expression identifier: " + name);
            }

            if (!this.activeIdentifiers.Add(name))
            {
                throw new CStructLayoutException("Circular expression dependency detected at: " + name);
            }

            try
            {
                int value = this.EvaluateProgram(this.evaluator.GetProgram(expression), dependencyDepth);
                this.identifierValues.Add(name, value);
                return value;
            }
            finally
            {
                this.activeIdentifiers.Remove(name);
            }
        }

        /// <summary>Applies the documented signed-Int32 operator semantics.</summary>
        private static int EvaluateBinary(ExpressionOpcode opcode, int left, int right)
        {
            return opcode switch
            {
                ExpressionOpcode.Add => checked(left + right),
                ExpressionOpcode.Subtract => checked(left - right),
                ExpressionOpcode.And => left & right,
                ExpressionOpcode.Divide => left / right,
                ExpressionOpcode.Multiply => checked(left * right),
                ExpressionOpcode.Or => left | right,
                ExpressionOpcode.ShiftLeft => CheckedShiftLeft(left, right),
                ExpressionOpcode.ShiftRight => CheckedShiftRight(left, right),
                _ => throw new InvalidOperationException("Unknown compiled binary expression opcode: " + opcode),
            };
        }

        /// <summary>Rejects C#'s masked shift counts and any signed-Int32 left-shift overflow.</summary>
        private static int CheckedShiftLeft(int value, int count)
        {
            ValidateShiftCount(count);
            long result = (long)value << count;
            if (result is < int.MinValue or > int.MaxValue)
            {
                throw new OverflowException("Expression left shift exceeded the signed 32-bit range.");
            }

            return (int)result;
        }

        /// <summary>Performs an arithmetic signed right shift after validating the unmasked count.</summary>
        private static int CheckedShiftRight(int value, int count)
        {
            ValidateShiftCount(count);
            return value >> count;
        }

        /// <summary>Defines valid shift counts as the complete signed-Int32 bit-index domain.</summary>
        private static void ValidateShiftCount(int count)
        {
            if (count is < 0 or >= sizeof(int) * 8)
            {
                throw new InvalidOperationException("Expression shift count must be between 0 and 31.");
            }
        }
    }

    /// <summary>Evaluates exact enum expressions without adding arbitrary-precision cost to normal Int32 expressions.</summary>
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
                UnaryOperatorType.Complement => ~value,
                UnaryOperatorType.Neg => -value,
                _ => throw new InvalidOperationException("Unknown unary operator: " + unary.Type),
            };
        }

        private BigInteger EvaluateBinary(BinaryOp binary, int depth)
        {
            BigInteger left = this.Evaluate(binary.Left, depth + 1);
            BigInteger right = this.Evaluate(binary.Right, depth + 1);
            return binary.Type switch
            {
                BinaryOperatorType.Add => left + right,
                BinaryOperatorType.Minus => left - right,
                BinaryOperatorType.And => left & right,
                BinaryOperatorType.Div => left / right,
                BinaryOperatorType.Mul => left * right,
                BinaryOperatorType.Or => left | right,
                BinaryOperatorType.ShiftLeft => left << this.ValidateShiftCount(right),
                BinaryOperatorType.ShiftRight => left >> this.ValidateShiftCount(right),
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
    private sealed class CompiledExpression
    {
        public CompiledExpression(
            ExpressionInstruction[] instructions,
            string[] dependencies)
        {
            this.Instructions = instructions;
            this.Dependencies = dependencies;
            int maximumDepth = 0;
            int maximumStackSize = 0;
            int stackSize = 0;
            var identifierReferences = new List<ExpressionIdentifierReference>();
            foreach (ExpressionInstruction instruction in instructions)
            {
                maximumDepth = Math.Max(maximumDepth, instruction.Depth);
                switch (instruction.Opcode)
                {
                case ExpressionOpcode.Literal:
                    stackSize++;
                    break;
                case ExpressionOpcode.Identifier:
                    stackSize++;
                    identifierReferences.Add(
                        new ExpressionIdentifierReference(
                            instruction.Name ??
                            throw new InvalidOperationException("Identifier instruction has no name."),
                            instruction.Depth));
                    break;
                case ExpressionOpcode.Complement:
                case ExpressionOpcode.Negate:
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

            this.IdentifierReferences = identifierReferences.ToArray();
            this.MaximumDepth = maximumDepth;
            this.MaximumStackSize = maximumStackSize;
        }

        public string[] Dependencies { get; }

        public ExpressionIdentifierReference[] IdentifierReferences { get; }

        public ExpressionInstruction[] Instructions { get; }

        public int MaximumDepth { get; }

        public int MaximumStackSize { get; }
    }

    /// <summary>Represents one iterative compilation frame.</summary>
    private readonly record struct CompilationFrame(Expr Expression, int Depth, bool EmitOperator);

    /// <summary>Tracks one iterative dependency walk and the identifier removed when that frame exits.</summary>
    private readonly record struct DependencyValidationFrame(
        CompiledExpression Program,
        int BaseDepth,
        int NextReference,
        string? EnteredIdentifier);

    /// <summary>Records one identifier occurrence and its syntax-tree depth inside a compiled program.</summary>
    private readonly record struct ExpressionIdentifierReference(string Name, int Depth);

    /// <summary>Represents one postfix stack-machine instruction.</summary>
    private readonly record struct ExpressionInstruction(
        ExpressionOpcode Opcode,
        BigInteger Value,
        string? Name,
        int Depth);

    /// <summary>Lists the executable operations supported by the CStructSharp expression subset.</summary>
    private enum ExpressionOpcode
    {
        Literal,
        Identifier,
        Complement,
        Negate,
        Add,
        Subtract,
        And,
        Divide,
        Multiply,
        Or,
        ShiftLeft,
        ShiftRight,
    }
}
