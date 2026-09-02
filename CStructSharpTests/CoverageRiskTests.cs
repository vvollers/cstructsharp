namespace CStructSharpTests;

using System.Collections.Immutable;
using CStructSharp;
using CStructSharp.Structure;

/// <summary>Covers small public contracts that otherwise appear as misleading high-risk gaps in per-file reports.</summary>
[TestClass]
public class CoverageRiskTests
{
    /// <summary>Exercises expression wrapper values, equality short-circuits, hashes, and diagnostic text.</summary>
    [TestMethod]
    public void ExpressionValueObjects_HonorTheirEqualityAndValueContracts()
    {
        var binary = new BinaryOp(BinaryOperatorType.Add, new Literal(1), new Literal(2));
        var equalBinary = new BinaryOp(BinaryOperatorType.Add, new Literal(1), new Literal(2));
        Assert.AreEqual(3, binary.Value);
        Assert.AreEqual(3, binary.Calc());
        Assert.IsTrue(binary.Equals(equalBinary));
        Assert.AreEqual(binary.GetHashCode(), equalBinary.GetHashCode());
        Assert.IsFalse(binary.Equals(new BinaryOp(BinaryOperatorType.Minus, new Literal(1), new Literal(2))));
        Assert.IsFalse(binary.Equals(new BinaryOp(BinaryOperatorType.Add, new Literal(0), new Literal(2))));
        Assert.IsFalse(binary.Equals(new BinaryOp(BinaryOperatorType.Add, new Literal(1), new Literal(0))));
        Assert.IsFalse(binary.Equals(new Literal(3)));
        Assert.AreEqual("BinaryOp: 3", binary.ToString());

        var unary = new UnaryOp(UnaryOperatorType.Neg, new Literal(3));
        var equalUnary = new UnaryOp(UnaryOperatorType.Neg, new Literal(3));
        Assert.AreEqual(-3, unary.Value);
        Assert.AreEqual(-3, unary.Calc());
        Assert.IsTrue(unary.Equals(equalUnary));
        Assert.AreEqual(unary.GetHashCode(), equalUnary.GetHashCode());
        Assert.IsFalse(unary.Equals(new UnaryOp(UnaryOperatorType.Complement, new Literal(3))));
        Assert.IsFalse(unary.Equals(new UnaryOp(UnaryOperatorType.Neg, new Literal(4))));
        Assert.IsFalse(unary.Equals(new Literal(-3)));
        Assert.AreEqual("Unary: -3", unary.ToString());

        var define = new Defines(new Identifier("COUNT"), binary);
        var equalDefine = new Defines(new Identifier("COUNT"), equalBinary);
        Assert.IsTrue(define.Equals(equalDefine));
        Assert.AreEqual(define.GetHashCode(), equalDefine.GetHashCode());
        Assert.IsFalse(define.Equals(new Defines(new Identifier("OTHER"), equalBinary)));
        Assert.IsFalse(define.Equals(new Defines(new Identifier("COUNT"), new Literal(3))));
        Assert.IsFalse(define.Equals(new Struct(new Identifier("COUNT"), [], false)));
        Assert.AreEqual("Define: [COUNT] = BinaryOp: 3", define.ToString());

        Assert.AreEqual(0, NoneExpr.Instance.Value);
        Assert.AreEqual(0, NoneExpr.Instance.Calc());
        Assert.IsTrue(NoneExpr.Instance.Equals(new NoneExpr()));
        Assert.AreEqual(new NoneExpr().GetHashCode(), NoneExpr.Instance.GetHashCode());
        Assert.AreEqual("NoneExpr(0)", NoneExpr.Instance.ToString());
    }

    /// <summary>Preserves call-expression structural equality while consistently rejecting evaluation.</summary>
    [TestMethod]
    public void CallExpression_UsesStructuralArgumentsForEqualityAndHashing()
    {
        var call = new Call(
            new Identifier("method"),
            ImmutableArray.Create<Expr>(new Literal(1), new Literal(2)));
        var equalCall = new Call(
            new Identifier("method"),
            ImmutableArray.Create<Expr>(new Literal(1), new Literal(2)));

        Assert.AreEqual(2, call.Arguments.Length);
        Assert.AreEqual(new Identifier("method"), call.Expr);
        Assert.IsTrue(call.Equals(equalCall));
        Assert.AreEqual(call.GetHashCode(), equalCall.GetHashCode());
        Assert.IsFalse(
            call.Equals(
                new Call(
                    new Identifier("other"),
                    ImmutableArray.Create<Expr>(new Literal(1), new Literal(2)))));
        Assert.IsFalse(
            call.Equals(
                new Call(
                    new Identifier("method"),
                    ImmutableArray.Create<Expr>(new Literal(1), new Literal(3)))));
        Assert.IsFalse(call.Equals(new Literal(0)));
        Assert.Throws<NotSupportedException>(() => call.Calc());
        Assert.Throws<NotSupportedException>(() => _ = call.Value);
        Assert.Throws<NotSupportedException>(() => call.ToString());
    }

    /// <summary>Exercises the complete read-budget stream facade without taking ownership of the caller's stream.</summary>
    [TestMethod]
    public void ReadBudgetStream_ImplementsItsReadOnlyNonOwningContract()
    {
        var options = new ReadOptions { MaxStringBytes = 7, MaxTotalBytesRead = 100, };
        Assert.Throws<ArgumentNullException>(() => new ReadBudgetStream(null!, options));

        var inner = new MemoryStream([0x11, 0x22, 0x33,]);
        var budget = new ReadBudgetStream(inner, options);
        Assert.IsTrue(budget.CanRead);
        Assert.IsTrue(budget.CanSeek);
        Assert.IsFalse(budget.CanWrite);
        Assert.AreEqual(3, budget.Length);
        Assert.AreEqual(0, budget.Position);
        Assert.AreEqual(7, budget.MaxStringBytes);

        budget.Flush();
        byte[] first = new byte[1];
        Assert.AreEqual(1, budget.Read(first, 0, first.Length));
        Assert.AreEqual((byte)0x11, first[0]);
        Span<byte> second = stackalloc byte[1];
        Assert.AreEqual(1, budget.Read(second));
        Assert.AreEqual((byte)0x22, second[0]);
        Assert.AreEqual(0x33, budget.ReadByte());
        Assert.AreEqual(-1, budget.ReadByte());
        Assert.AreEqual(0, budget.Read(first, 0, first.Length));

        Assert.AreEqual(0, budget.Seek(0, SeekOrigin.Begin));
        budget.Position = 1;
        Assert.AreEqual(1, budget.Position);
        Assert.Throws<NotSupportedException>(() => budget.SetLength(1));
        Assert.Throws<NotSupportedException>(() => budget.Write(first, 0, first.Length));

        budget.Dispose();
        Assert.IsTrue(inner.CanRead);
        Assert.AreEqual(0x22, inner.ReadByte());
        inner.Dispose();
    }

    /// <summary>Exercises public exception constructors and both valid and invalid string-pointer values.</summary>
    [TestMethod]
    public void SmallPublicWrappers_PreserveMessagesCausesAndValues()
    {
        var cause = new FormatException("cause");

        Assert.IsNotNull(new CStructLayoutException().Message);
        Assert.AreEqual("layout", new CStructLayoutException("layout").Message);
        Assert.AreSame(cause, new CStructLayoutException("layout", cause).InnerException);
        Assert.IsNotNull(new CStructPathException().Message);
        Assert.AreEqual("path", new CStructPathException("path").Message);
        Assert.AreSame(cause, new CStructPathException("path", cause).InnerException);
        Assert.IsNotNull(new CStructReadException().Message);
        Assert.AreEqual("read", new CStructReadException("read").Message);
        Assert.AreSame(cause, new CStructReadException("read", cause).InnerException);
        Assert.AreEqual("limit", new CStructReadLimitException("limit").Message);
        Assert.AreSame(cause, new CStructReadLimitException("limit", cause).InnerException);
        Assert.IsNotNull(new CStructWriteException().Message);
        Assert.AreEqual("write", new CStructWriteException("write").Message);
        Assert.AreSame(cause, new CStructWriteException("write", cause).InnerException);
        Assert.AreEqual(CStructErrorCode.InvalidLayout, new CStructLayoutException("layout").Code);
        Assert.AreEqual(CStructErrorCode.InvalidPath, new CStructPathException("path").Code);
        Assert.AreEqual(CStructErrorCode.ReadFailed, new CStructReadException("read").Code);
        Assert.AreEqual(CStructErrorCode.ReadLimitExceeded, new CStructReadLimitException("limit").Code);
        Assert.AreEqual(CStructErrorCode.WriteFailed, new CStructWriteException("write").Code);
        Assert.AreEqual(CStructErrorCode.WriteLimitExceeded, new CStructWriteLimitException("limit").Code);
        Assert.AreSame(cause, new CStructWriteLimitException("limit", cause).InnerException);
    }
}
