namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CStructSharp;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     The generated writer against the runtime writer: the value each reader produced is written back by its own
///     writer and the bytes must agree; a destination too small for the value fails in both with the runtime's
///     capacity text (the runtime's static write plan validates a whole block before writing it and so reports
///     offset 0 where the sequential writer reports its position - the comparison strips the offset).
/// </summary>
internal static class WriteParity
{
    private delegate int SpanSerializer<in T>(T value, Span<byte> destination, IReadOnlyDictionary<string, int>? variables, WriteOptions? options);

    public static void AssertRoundTrip(string id, Type generatedClass, CStruct runtime, string root, object runtimeValue, object generatedValue, IReadOnlyDictionary<string, int>? variables)
    {
        if (runtimeValue is not StructValue and not UnionValue)
        {
            return;
        }

        MethodInfo? serialize = generatedClass.GetMethods().SingleOrDefault(method => method.Name == "Serialize" + generatedValue.GetType().Name && method.GetParameters().Length == 3);
        Assert.IsNotNull(serialize, id + ": no Serialize for " + generatedValue.GetType().Name);
        byte[] expected;
        try
        {
            expected = runtime.Serialize(root, runtimeValue, variables);
        }
        catch (CStructException exception)
        {
            // The runtime cannot write this value back (a union read from bytes it cannot re-select, ...): the
            // generated writer must fail the same way.
            CStructException generatedFailure = Assert.Throws<CStructException>(() => Invoke(serialize, generatedValue, variables, null), id + ": the runtime refused to write the value back (" + exception.Message + ") but the generated writer did not");
            Assert.AreEqual(exception.GetType(), generatedFailure.GetType(), id);
            return;
        }

        byte[] actual = (byte[])Invoke(serialize, generatedValue, variables, null);
        CollectionAssert.AreEqual(expected, actual, id + ": serialized bytes differ (runtime " + Convert.ToHexString(expected) + " vs generated " + Convert.ToHexString(actual) + ")");

        // Into a span: the exact size succeeds with the same length; every smaller destination fails with the
        // capacity text (or, in the runtime, with an earlier validation that the generated path reproduces).
        MethodInfo spanSerialize = generatedClass.GetMethods().Single(method => method.Name == serialize.Name && method.GetParameters().Length == 4);
        Assert.AreEqual(expected.Length, (int)InvokeSpan(spanSerialize, generatedValue, new byte[expected.Length], variables), id + ": span length");
        foreach (int capacity in CapacitySweep(expected.Length))
        {
            var destination = new byte[capacity];
            Exception? runtimeError = Catch(() => runtime.Serialize(destination, root, runtimeValue, variables));
            Exception? generatedError = Catch(() => InvokeSpan(spanSerialize, generatedValue, new byte[capacity], variables));
            Assert.AreEqual(runtimeError?.GetType(), generatedError?.GetType(), $"{id} capacity {capacity}: exception type ({runtimeError?.Message} vs {generatedError?.Message})");
            Assert.AreEqual(WithoutOffset(runtimeError?.Message), WithoutOffset(generatedError?.Message), $"{id} capacity {capacity}");
        }
    }

    public static string? WithoutOffset(string? message) => message is null ? null : Regex.Replace(message, @", offset \d+", string.Empty);

    private static IEnumerable<int> CapacitySweep(int length)
    {
        if (length <= 512)
        {
            for (int capacity = 0; capacity < length; capacity++)
            {
                yield return capacity;
            }

            yield break;
        }

        for (int capacity = 0; capacity < 32; capacity++)
        {
            yield return capacity;
        }

        for (int capacity = 32; capacity < length; capacity += Math.Max(1, length / 41))
        {
            yield return capacity;
        }

        yield return length - 1;
    }

    private static object Invoke(MethodInfo method, object value, IReadOnlyDictionary<string, int>? variables, WriteOptions? options)
    {
        try
        {
            return method.Invoke(null, [value, variables, options])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static object InvokeSpan(MethodInfo method, object value, byte[] destination, IReadOnlyDictionary<string, int>? variables)
    {
        // A Span<byte> parameter cannot be boxed: a generic helper closed over the value type builds a typed delegate.
        MethodInfo helper = typeof(WriteParity).GetMethod(nameof(CallSpan), BindingFlags.NonPublic | BindingFlags.Static)!.MakeGenericMethod(value.GetType());
        try
        {
            return helper.Invoke(null, [method, value, destination, variables])!;
        }
        catch (TargetInvocationException exception) when (exception.InnerException is not null)
        {
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(exception.InnerException).Throw();
            throw;
        }
    }

    private static int CallSpan<T>(MethodInfo method, T value, byte[] destination, IReadOnlyDictionary<string, int>? variables)
    {
        var caller = (SpanSerializer<T>)Delegate.CreateDelegate(typeof(SpanSerializer<T>), method);
        return caller(value, destination, variables, null);
    }

    private static Exception? Catch(Func<object?> action)
    {
        try
        {
            action();
            return null;
        }
        catch (CStructException exception)
        {
            return exception;
        }
    }
}
