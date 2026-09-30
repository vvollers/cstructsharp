namespace CStructSharp.Generated.Parity;

using CStructSharp.Diagnostics;

/// <summary>Checks the generated <c>Serialize</c> into a new array for a struct with a fixed writer.</summary>
[TestClass]
public class FixedArraySerializeTests
{
    /// <summary>A value the fixed writer takes gives the runtime's bytes, in an array of the struct's size.</summary>
    [TestMethod]
    public void FixedValue_GivesTheRuntimeBytes()
    {
        NestedValueLayout.Outer value = CreateValue();

        byte[] generated = NestedValueLayout.Serialize(value);

        CollectionAssert.AreEqual(new byte[] { 9, 1, 0, 2, 0, 3, 0, 4, 0, }, generated);
        CollectionAssert.AreEqual(NestedValueLayout.Layout.Serialize("outer", NestedValueLayout.Layout.Parse(generated, "outer")), generated);
    }

    /// <summary>
    ///     A budget one byte short of the struct, a null nested value and a cancelled token fail as the span overload
    ///     fails: the same exception type, text, member and offset.
    /// </summary>
    [TestMethod]
    public void UnwritableValue_FailsAsTheSpanOverloadFails()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        NestedValueLayout.Outer withoutNested = CreateValue();
        withoutNested.Second = null!;
        (NestedValueLayout.Outer Value, WriteOptions? Options)[] cases =
        [
            (CreateValue(), new WriteOptions { MaxTotalBytesWritten = 8, }),
            (withoutNested, null),
            (CreateValue(), new WriteOptions { CancellationToken = cancelled.Token, }),
        ];
        foreach ((NestedValueLayout.Outer value, WriteOptions? options) in cases)
        {
            Exception expected = Assert.Throws<Exception>(() => NestedValueLayout.Serialize(value, new byte[64], options));
            Exception actual = Assert.Throws<Exception>(() => NestedValueLayout.Serialize(value, options));

            Assert.AreEqual(expected.GetType(), actual.GetType());
            Assert.AreEqual(expected.Message, actual.Message);
            if (expected is CStructException expectedFailure)
            {
                var actualFailure = (CStructException)actual;
                Assert.AreEqual(expectedFailure.Member, actualFailure.Member);
                Assert.AreEqual(expectedFailure.Offset, actualFailure.Offset);
            }
        }
    }

    /// <summary>Creates the value whose bytes are 9, then (1, 2) and (3, 4) as little-endian 16-bit pairs.</summary>
    /// <returns>A value the fixed writer takes.</returns>
    private static NestedValueLayout.Outer CreateValue()
    {
        var value = new NestedValueLayout.Outer { Tag = 9, };
        value.First.A = 1;
        value.First.B = 2;
        value.Second.A = 3;
        value.Second.B = 4;
        return value;
    }
}
