namespace CStructSharp.Tests;

using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>Checks failure classification while a selected union member is staged through a caller-supplied mapper.</summary>
[TestClass]
public class UnionMapperFailureTests
{
    /// <summary>Expected conversion failures become write errors; domain errors and unexpected failures retain their identity.</summary>
    /// <param name="kind">The mapper's deliberate failure category.</param>
    [TestMethod]
    [DataRow("invalid-operation")]
    [DataRow("argument")]
    [DataRow("arithmetic")]
    [DataRow("format")]
    [DataRow("invalid-cast")]
    [DataRow("not-supported")]
    [DataRow("domain")]
    [DataRow("unexpected")]
    public void SelectedMember_ClassifiesMapperFailuresWithoutWriting(string kind)
    {
        Exception original = kind switch
        {
            "invalid-operation" => new InvalidOperationException("mapper failure"),
            "argument" => new ArgumentException("mapper failure"),
            "arithmetic" => new ArithmeticException("mapper failure"),
            "format" => new FormatException("mapper failure"),
            "invalid-cast" => new InvalidCastException("mapper failure"),
            "not-supported" => new NotSupportedException("mapper failure"),
            "domain" => new CStructWriteException("mapper failure"),
            _ => new IOException("unexpected mapper failure"),
        };
        MappedTypes.Register<FailingMapper>();
        var layout = new CStruct("union choice { struct { uint8 value; } record; uint8 raw; };");
        UnionValue value = UnionValue.FromMember("choice", "record", new FailingMapper(original));
        byte[] bytes = [0xAA, 0xBB, 0xCC,];
        using var destination = new MemoryStream(bytes.ToArray()) { Position = 1, };
        if (kind == "unexpected")
        {
            // A mapper's unexpected I/O failure is not a caller-value conversion failure.
            Assert.AreSame(original, Assert.Throws<IOException>(() => layout.Write(destination, "choice", value)));
        }
        else
        {
            // Conversion errors gain the selected member's name, while an existing domain error is preserved.
            CStructWriteException failure = Assert.Throws<CStructWriteException>(() => layout.Write(destination, "choice", value));
            if (kind == "domain")
            {
                Assert.AreSame(original, failure);
            }
            else
            {
                Assert.AreSame(original, failure.InnerException);
                StringAssert.StartsWith(failure.Message, "Cannot write selected union member 'choice.record'");
            }
        }

        Assert.AreEqual(1L, destination.Position);
        CollectionAssert.AreEqual(bytes, destination.ToArray());
    }

    /// <summary>Raises one caller-selected failure while materializing a selected struct member.</summary>
    /// <param name="failure">The exact exception that WriteTo must propagate.</param>
    private sealed class FailingMapper(Exception failure) : ICStructMapped<FailingMapper>
    {
        private Exception Failure { get; } = failure;

        /// <summary>Rejects reads because this fixture exercises only writer-side mapper failures.</summary>
        /// <param name="source">Unused parsed input.</param>
        /// <returns>No mapped value is produced.</returns>
        public static FailingMapper ReadFrom(StructValue source) => throw new NotSupportedException();

        /// <summary>Throws the fixture's exact exception before assigning any target member.</summary>
        /// <param name="value">The caller's failing mapper.</param>
        /// <param name="target">The untouched staging value.</param>
        public static void WriteTo(FailingMapper value, StructValue target) => throw value.Failure;
    }
}
