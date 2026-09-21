namespace CStructSharp.Values;

using CStructSharp.Diagnostics;

/// <summary>
///     The outcome of an awaitable <c>TryReadValueAsync&lt;T&gt;</c> (an <c>out</c> parameter cannot cross an
///     <c>await</c>): either <see cref="Value"/> with <see cref="Succeeded"/> set, or the categorized
///     <see cref="Failure"/> the throwing form would have raised. Cancellation and argument errors are not outcomes;
///     they throw as they do everywhere else.
/// </summary>
/// <typeparam name="T">The requested value type.</typeparam>
/// <param name="Succeeded">Whether the read produced a value.</param>
/// <param name="Value">The value when <paramref name="Succeeded"/> is <see langword="true"/>; otherwise the default.</param>
/// <param name="Failure">The read, path, or limit failure when <paramref name="Succeeded"/> is <see langword="false"/>; otherwise <see langword="null"/>.</param>
public readonly record struct ReadAttempt<T>(bool Succeeded, T? Value, CStructException? Failure);
