namespace CStructSharp.Generators.Tests;

using System;
using System.Reflection;
using CStructSharp;
using CStructSharp.Diagnostics;

/// <summary>
///     A view of a value-type layout converts to the generated struct without allocating: <c>ToObject()</c> of a
///     <c>Shape = Struct</c> layout returns the struct itself, so a lazy view and an allocation-free value combine.
/// </summary>
[TestClass]
public class ViewValueTests
{
    private const string Consumer = """"
        using System;
        using CStructSharp;
        using CStructSharp.Diagnostics;

        namespace Demo;

        [CStructLayout("struct rec { uint32 id; uint16 delta; uint8 sig[4]; };", Root = "rec", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Parser)]
        public static partial class ValueRec { }

        [CStructLayout("struct sealed { uint8 magic @expect(magic == 7, \"sealed.magic\"); uint16 tail; };", Root = "sealed", Shape = GeneratedShape.Struct, Surface = GeneratedSurface.Parser)]
        public static partial class ValueSealed { }

        public static class Probe
        {
            public static Type ValueType() => typeof(ValueRec.Rec);

            public static uint Convert(byte[] bytes) => new ValueRec.RecView(bytes).ToObject().Sig[3];

            public static long Allocated(byte[] bytes, int rounds)
            {
                for (int i = 0; i < 200; i++) { _ = new ValueRec.RecView(bytes).ToObject(); }
                long before = GC.GetAllocatedBytesForCurrentThread();
                uint sum = 0;
                for (int i = 0; i < rounds; i++)
                {
                    ValueRec.Rec value = new ValueRec.RecView(bytes).ToObject();
                    sum += value.Id + value.Delta + value.Sig[2];
                }

                return GC.GetAllocatedBytesForCurrentThread() - before + (sum == 0 ? 1 : 0);
            }

            public static bool Equal(byte[] bytes)
            {
                ValueRec.Rec fromView = new ValueRec.RecView(bytes).ToObject();
                ValueRec.Rec fromParse = ValueRec.Parse(bytes);
                return fromView.Id == fromParse.Id && fromView.Delta == fromParse.Delta && fromView.Sig.AsSpan().SequenceEqual(fromParse.Sig.AsSpan());
            }

            public static CStructException? Sealed(byte[] bytes)
            {
                try
                {
                    _ = new ValueSealed.SealedView(bytes).ToObject();
                    return null;
                }
                catch (CStructException exception)
                {
                    return exception;
                }
            }
        }
        """";

    private static Assembly assembly = null!;

    /// <summary>Generates the layouts once.</summary>
    /// <param name="context">The test context.</param>
    [ClassInitialize]
    public static void Generate(TestContext context)
    {
        _ = context;
        assembly = GeneratorRunner.Run(Consumer).AssertClean().Load();
    }

    /// <summary>Calls a probe method.</summary>
    /// <param name="method">The method.</param>
    /// <param name="arguments">The arguments.</param>
    /// <returns>The result.</returns>
    private static object? Call(string method, params object[] arguments) => assembly.GetType("Demo.Probe")!.GetMethod(method)!.Invoke(null, arguments);

    /// <summary>The generated type is a struct, and the view converts to it with the same members the reader gives.</summary>
    [TestMethod]
    public void ToObject_OfAValueTypeLayout_ReturnsTheStruct()
    {
        Assert.IsTrue(((Type)Call("ValueType")!).IsValueType);
        byte[] bytes = [1, 0, 0, 0, 2, 0, 9, 8, 7, 6];
        Assert.AreEqual(6u, Convert.ToUInt32(Call("Convert", bytes)));
        Assert.IsTrue((bool)Call("Equal", bytes)!);
    }

    /// <summary>The conversion allocates nothing.</summary>
    [TestMethod]
    public void ToObject_OfAValueTypeLayout_AllocatesNothing()
    {
        Assert.AreEqual(0L, (long)Call("Allocated", new byte[] { 1, 0, 0, 0, 2, 0, 9, 8, 7, 6 }, 5000)!);
    }

    /// <summary>A conversion that fails reports the reader's failure, label included.</summary>
    [TestMethod]
    public void ToObject_OfAValueTypeLayout_ReportsTheReadersFailure()
    {
        var failure = (CStructConstraintException)Call("Sealed", new byte[] { 8, 0, 0 })!;
        Assert.AreEqual("sealed.magic", failure.Label);
        Assert.IsNull(Call("Sealed", new byte[] { 7, 0, 0 }));
        Assert.IsInstanceOfType<CStructReadException>(Call("Sealed", new byte[] { 7 }));
    }
}
