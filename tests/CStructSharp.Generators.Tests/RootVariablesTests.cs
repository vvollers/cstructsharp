namespace CStructSharp.Generators.Tests;

using System;
using System.Reflection;

/// <summary>
///     The unnamed root readers take the caller <c>variables</c> the named readers take: <c>Parse(source, [order], variables, options)</c>
///     and <c>Parse(source, [order], variables, in limits)</c>, on every input kind that has a root form.
/// </summary>
[TestClass]
public class RootVariablesTests
{
    private const string Consumer = """"
        using System;
        using System.Buffers;
        using System.Collections.Generic;
        using System.IO;
        using CStructSharp;
        using CStructSharp.Diagnostics;

        namespace Demo;

        [CStructLayout("struct packet { uint8 kind; uint8 payload[COUNT]; };", Root = "packet")]
        public static partial class Full { }

        [CStructLayout("struct packet { uint8 kind; uint8 payload[COUNT]; };", Root = "packet", Surface = GeneratedSurface.Parser)]
        public static partial class Parser { }

        [CStructLayout("struct packet { uint8 kind; uint8 payload[COUNT]; };", Root = "packet", Surface = GeneratedSurface.Reader, RuntimeByteOrder = true)]
        public static partial class Order { }

        public static class Calls
        {
            private static readonly Dictionary<string, int> Two = new() { ["COUNT"] = 2 };

            public static string Run(string form, byte[] bytes)
            {
                try
                {
                    return Read(form, bytes);
                }
                catch (Exception exception)
                {
                    return exception.GetType().Name;
                }
            }

            private static string Read(string form, byte[] bytes)
            {
                var limits = new ReadLimits { MaxArrayElements = 1 };
                var wide = new ReadLimits();
                switch (form)
                {
                case "Span": return Full.Parse(new ReadOnlySpan<byte>(bytes), Two).Payload.Length.ToString();
                case "SpanOptions": return Full.Parse(new ReadOnlySpan<byte>(bytes), Two, new ReadOptions()).Payload.Length.ToString();
                case "Array": return Full.Parse(bytes, Two).Payload.Length.ToString();
                case "Memory": return Full.Parse(new ReadOnlyMemory<byte>(bytes), Two).Payload.Length.ToString();
                case "Sequence": return Full.Parse(new ReadOnlySequence<byte>(bytes), Two).Payload.Length.ToString();
                case "Stream": return Full.Parse(new MemoryStream(bytes), Two).Payload.Length.ToString();
                case "Async": return Full.ParseAsync(new MemoryStream(bytes), Two).AsTask().GetAwaiter().GetResult().Payload.Length.ToString();
                case "Limits": return Full.Parse(new ReadOnlySpan<byte>(bytes), Two, in wide).Payload.Length.ToString();
                case "LimitsTooTight": return Full.Parse(new ReadOnlySpan<byte>(bytes), new Dictionary<string, int> { ["COUNT"] = 2 }, in limits).Payload.Length.ToString();
                case "TryLimits": return Full.TryParse(new ReadOnlySpan<byte>(bytes), out Full.Packet? tried, out CStructException? failure, Two, in wide).ToString() + (failure is null) + tried!.Payload.Length;
                case "Try": return Full.TryParse(bytes, out Full.Packet? value, out CStructException? valueFailure, Two).ToString() + (valueFailure is null) + value!.Payload.Length;
                case "TryNoFailure": return Full.TryParse(bytes, out Full.Packet? other, Two).ToString() + other!.Payload.Length;
                case "NoVariables": return Full.Parse(bytes).Payload.Length.ToString();
                case "ParserRoot": return Parser.Parse(new ReadOnlySpan<byte>(bytes), Two).Payload.Length.ToString();
                case "OrderRoot": return Order.Parse(new ReadOnlySpan<byte>(bytes), true, Two).Payload.Length.ToString();
                case "OrderLimits": return Order.Parse(new ReadOnlySpan<byte>(bytes), false, Two, in wide).Payload.Length.ToString();
                default: throw new ArgumentException(form);
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

    /// <summary>Every input kind of the root reader passes the variables to the layout.</summary>
    [TestMethod]
    public void RootParse_TakesVariables_OnEveryInputKind()
    {
        byte[] bytes = [7, 1, 2];
        foreach (string form in new[] { "Span", "SpanOptions", "Array", "Memory", "Sequence", "Stream", "Async", "Limits", "ParserRoot", "OrderRoot", "OrderLimits" })
        {
            Assert.AreEqual("2", Run(form, bytes), form);
        }

        Assert.AreEqual("TrueTrue2", Run("TryLimits", bytes));
        Assert.AreEqual("TrueTrue2", Run("Try", bytes));
        Assert.AreEqual("True2", Run("TryNoFailure", bytes));
    }

    /// <summary>The variables of the limits form still go through the limits: a tight array limit fails after the variable is applied, and a missing variable is a read failure.</summary>
    [TestMethod]
    public void RootParse_AppliesLimitsAndReportsMissingVariables()
    {
        Assert.AreEqual("CStructReadLimitException", Run("LimitsTooTight", [7, 1, 2]));
        Assert.AreEqual("CStructReadException", Run("NoVariables", [7, 1, 2]));
    }

    /// <summary>Calls the consumer's <c>Run</c> method.</summary>
    /// <param name="form">The form the method switches on.</param>
    /// <param name="bytes">The input bytes.</param>
    /// <returns>The result text, or the type name of the exception.</returns>
    private static string Run(string form, byte[] bytes)
        => (string)assembly.GetType("Demo.Calls")!.GetMethod("Run")!.Invoke(null, [form, bytes])!;
}
