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
    /// <summary>The shape of a generated serializer that writes into a caller-supplied span.</summary>
    /// <typeparam name="T">The generated composite type.</typeparam>
    /// <param name="value">The value to write.</param>
    /// <param name="destination">The span to fill from its start.</param>
    /// <param name="variables">The caller variables.</param>
    /// <param name="options">The write options.</param>
    /// <returns>The number of bytes written.</returns>
    private delegate int SpanSerializer<in T>(T value, Span<byte> destination, IReadOnlyDictionary<string, int>? variables, WriteOptions? options);

    /// <summary>
    ///     Writes a struct or union value back with both writers and asserts equal bytes, or the same refusal, and the
    ///     same capacity failures for every smaller span; other values are skipped.
    /// </summary>
    /// <param name="id">The case name used in failure messages.</param>
    /// <param name="generatedClass">The generated layout class.</param>
    /// <param name="runtime">The runtime layout.</param>
    /// <param name="root">The root name in the layout.</param>
    /// <param name="runtimeValue">The value the runtime read.</param>
    /// <param name="generatedValue">The value the generated reader read.</param>
    /// <param name="variables">The caller variables, or <see langword="null"/> for none.</param>
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

    /// <summary>
    ///     Removes the <c>, offset N</c> part of a failure message, where the two writers legitimately differ.
    /// </summary>
    /// <param name="message">The failure message, or <see langword="null"/>.</param>
    /// <returns>The message without its offset, or <see langword="null"/> for no message.</returns>
    public static string? WithoutOffset(string? message) => message is null ? null : Regex.Replace(message, @", offset \d+", string.Empty);

    /// <summary>
    ///     Chooses the too-small destination sizes to try: every size below a length up to 512 bytes; for a longer
    ///     value, the first 32, about 41 spread sizes, and one byte short.
    /// </summary>
    /// <param name="length">The serialized length in bytes.</param>
    /// <returns>Destination sizes shorter than the value.</returns>
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

    /// <summary>Calls a generated array-returning serializer, rethrowing its exceptions unwrapped.</summary>
    /// <param name="method">The generated serializer.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="variables">The caller variables, or <see langword="null"/> for none.</param>
    /// <param name="options">The write options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The serialized bytes.</returns>
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

    /// <summary>Calls a generated span serializer, rethrowing its exceptions unwrapped.</summary>
    /// <param name="method">The generated span serializer.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="destination">The array the span covers.</param>
    /// <param name="variables">The caller variables, or <see langword="null"/> for none.</param>
    /// <returns>The boxed number of bytes written.</returns>
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

    /// <summary>Binds a span serializer to <see cref="SpanSerializer{T}"/> and calls it with default options.</summary>
    /// <typeparam name="T">The generated composite type.</typeparam>
    /// <param name="method">The generated span serializer.</param>
    /// <param name="value">The value to write.</param>
    /// <param name="destination">The array the span covers.</param>
    /// <param name="variables">The caller variables, or <see langword="null"/> for none.</param>
    /// <returns>The number of bytes written.</returns>
    private static int CallSpan<T>(MethodInfo method, T value, byte[] destination, IReadOnlyDictionary<string, int>? variables)
    {
        var caller = (SpanSerializer<T>)Delegate.CreateDelegate(typeof(SpanSerializer<T>), method);
        return caller(value, destination, variables, null);
    }

    /// <summary>Runs a write and returns the <see cref="CStructException"/> it throws.</summary>
    /// <param name="action">The write to run.</param>
    /// <returns>The exception, or <see langword="null"/> when the write succeeds; other exceptions propagate.</returns>
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
