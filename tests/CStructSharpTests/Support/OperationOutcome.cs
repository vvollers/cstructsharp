namespace CStructSharp.Tests;

using System.Collections;
using System.Text.Json;
using System.Text.RegularExpressions;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     The outcome of one operation - its result, or the failure it reported - so two ways of doing the same thing (a
///     fast path and the general path, two input forms) can be asserted to behave identically.
/// </summary>
/// <param name="Result">The result, or <see langword="null"/> after a failure.</param>
/// <param name="Failure">The failure, or <see langword="null"/> after success.</param>
internal readonly record struct OperationOutcome(object? Result, Exception? Failure)
{
    /// <summary>Runs an operation and records its result or a library failure; any other exception propagates.</summary>
    /// <param name="operation">The operation.</param>
    /// <param name="alsoCaptures">Further exception types to record as failures, beyond <see cref="CStructException"/>.</param>
    /// <returns>The outcome.</returns>
    public static OperationOutcome Of(Func<object?> operation, params Type[] alsoCaptures)
    {
        try
        {
            return new OperationOutcome(operation(), null);
        }
        catch (Exception exception) when (exception is CStructException || alsoCaptures.Any(type => type.IsInstanceOfType(exception)))
        {
            return new OperationOutcome(null, exception);
        }
    }

    /// <summary>
    ///     Asserts two outcomes are the same: the failure's type, message, path, offset and inner exception type, or the
    ///     rendered result.
    /// </summary>
    /// <param name="expected">The reference outcome, usually the general path's.</param>
    /// <param name="actual">The outcome to check.</param>
    /// <param name="label">The case, for the failure message.</param>
    /// <param name="compareOffsets">
    ///     Whether failure offsets must match; when two paths legitimately fail at different stream positions, the offset
    ///     is also removed from the messages before they are compared.
    /// </param>
    public static void AssertSame(OperationOutcome expected, OperationOutcome actual, string label, bool compareOffsets = true)
    {
        Exception? left = expected.Failure;
        Exception? right = actual.Failure;
        Assert.AreEqual(left?.GetType(), right?.GetType(), label + ": " + (right ?? left)?.Message);
        Assert.AreEqual(Message(left, compareOffsets), Message(right, compareOffsets), label + ": message");
        Assert.AreEqual((left as CStructException)?.Path, (right as CStructException)?.Path, label + ": path");
        if (compareOffsets)
        {
            Assert.AreEqual((left as CStructException)?.Offset, (right as CStructException)?.Offset, label + ": offset");
        }

        Assert.AreEqual(left?.InnerException?.GetType(), right?.InnerException?.GetType(), label + ": inner failure");
        Assert.AreEqual(Render(expected.Result), Render(actual.Result), label);
    }

    /// <summary>
    ///     Renders a result as comparable text, member order included: struct and union values by member, pointers by
    ///     address and target, enums by name and value, text quoted, sequences by item, other objects with public members
    ///     as JSON, and scalars with their type.
    /// </summary>
    /// <param name="value">The result.</param>
    /// <returns>The text.</returns>
    public static string Render(object? value)
    {
        return value switch
        {
            null => "null",
            StructValue s => "{" + string.Join(",", s.Select(pair => pair.Key + ":" + Render(pair.Value))) + "}",
            EnumValueResult e => e.Enum + "." + (e.Name ?? "?") + "=" + e.Value,
            UnionValue u => u.UnionName + "{" + string.Join(",", u.Members.Select(pair => pair.Key + ":" + Render(pair.Value))) + "}",
            Pointer p => "ptr(" + p.Address + (p.IsDereferenced ? "->" + Render(p.Value) : string.Empty) + ")",
            string text => "\"" + text + "\"",
            IEnumerable items => "[" + string.Join(",", items.Cast<object?>().Select(Render)) + "]",
            IConvertible => value.GetType().Name + ":" + value,
            _ => value.GetType().Name + JsonSerializer.Serialize(value, value.GetType(), new JsonSerializerOptions { IncludeFields = true, }),
        };
    }

    /// <summary>A failure's message, with any <c>offset N</c> removed when offsets are not compared.</summary>
    /// <param name="failure">The failure, or <see langword="null"/>.</param>
    /// <param name="keepOffset">Whether to keep the offset.</param>
    /// <returns>The message, or <see langword="null"/>.</returns>
    private static string? Message(Exception? failure, bool keepOffset)
        => failure is null ? null : keepOffset ? failure.Message : Regex.Replace(failure.Message, @",? ?offset \d+", string.Empty);
}
