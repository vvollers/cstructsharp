namespace CStructSharp.Tests;

using System.Globalization;
using System.Numerics;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>Checks exact integer ranges and diagnostic context at the natural-value conversion boundary.</summary>
[TestClass]
public class TypedConversionBoundaryTests
{
    /// <summary>Every integral target accepts both endpoints and rejects the adjacent integers without wrapping.</summary>
    /// <param name="target">The requested CLR integer type.</param>
    /// <param name="minimum">Its inclusive minimum in invariant decimal notation.</param>
    /// <param name="maximum">Its inclusive maximum in invariant decimal notation.</param>
    [TestMethod]
    [DataRow(typeof(byte), "0", "255")]
    [DataRow(typeof(sbyte), "-128", "127")]
    [DataRow(typeof(short), "-32768", "32767")]
    [DataRow(typeof(ushort), "0", "65535")]
    [DataRow(typeof(int), "-2147483648", "2147483647")]
    [DataRow(typeof(uint), "0", "4294967295")]
    [DataRow(typeof(long), "-9223372036854775808", "9223372036854775807")]
    [DataRow(typeof(ulong), "0", "18446744073709551615")]
    public void IntegralTargets_EnforceBothRangeBoundaries(Type target, string minimum, string maximum)
    {
        BigInteger lower = BigInteger.Parse(minimum, CultureInfo.InvariantCulture);
        BigInteger upper = BigInteger.Parse(maximum, CultureInfo.InvariantCulture);
        foreach (BigInteger endpoint in new[] { lower, upper, })
        {
            object converted = TypedValueConverter.Convert(endpoint, target, "root.number")!;
            Assert.AreEqual(target, converted.GetType());
            string actual = ((IFormattable)converted).ToString(null, CultureInfo.InvariantCulture);
            Assert.AreEqual(endpoint, BigInteger.Parse(actual, CultureInfo.InvariantCulture));
        }

        foreach (BigInteger outside in new[] { lower - 1, upper + 1, })
        {
            // Out-of-range values must retain both the requested path and the numeric overflow cause.
            CStructReadException error = Assert.Throws<CStructReadException>(() => TypedValueConverter.Convert(outside, target, "root.number"));
            Assert.AreEqual("root.number", error.Path);
            Assert.IsInstanceOfType<OverflowException>(error.InnerException);
            StringAssert.StartsWith(error.Message, $"Cannot map 'root.number' from 'System.Numerics.BigInteger' to '{target.FullName}' without an unsupported or lossy conversion");
        }
    }

    /// <summary>Missing caller paths retain the root marker and identify null separately from an incompatible string.</summary>
    /// <param name="value">The incompatible natural value.</param>
    /// <param name="sourceName">The expected source name in the diagnostic.</param>
    [TestMethod]
    [DataRow(null, "null")]
    [DataRow("not a number", "System.String")]
    public void FailedRootConversion_IdentifiesTheSource(object? value, string sourceName)
    {
        CStructReadException error = Assert.Throws<CStructReadException>(() => TypedValueConverter.Convert(value, typeof(int), null));
        Assert.AreEqual("<root>", error.Path);
        Assert.IsNull(error.InnerException);
        StringAssert.StartsWith(error.Message, $"Cannot map '<root>' from '{sourceName}' to 'System.Int32' without an unsupported or lossy conversion");
    }

    /// <summary>A fractional numeric value retains the exact-integer failure as its conversion cause.</summary>
    [TestMethod]
    public void FractionalIntegerConversion_RetainsItsCauseAndRoot()
    {
        CStructReadException error = Assert.Throws<CStructReadException>(() => TypedValueConverter.Convert(0.5D, typeof(int), null));
        Assert.AreEqual("<root>", error.Path);
        Assert.IsInstanceOfType<InvalidCastException>(error.InnerException);
        Assert.AreEqual("The numeric value is not an exact integer.", error.InnerException.Message);
        StringAssert.StartsWith(error.Message, "Cannot map '<root>' from 'System.Double' to 'System.Int32'");
    }

    /// <summary>A registered struct mapper must reject a scalar value with a precise type and path diagnostic.</summary>
    [TestMethod]
    public void RegisteredMapper_RejectsScalarInput()
    {
        Type target = typeof(TypedValueConversionTests.ExactModel);
        CStructReadException error = Assert.Throws<CStructReadException>(() => TypedValueConverter.Convert((byte)7, target, "root.number"));
        Assert.AreEqual("root.number", error.Path);
        StringAssert.StartsWith(error.Message, $"Cannot map 'root.number' to '{target.FullName}': the value is a Byte, not a struct");
    }
}
