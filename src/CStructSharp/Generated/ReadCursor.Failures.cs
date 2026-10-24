namespace CStructSharp.Generated;

using System;
using CStructSharp.Diagnostics;
using CStructSharp.Expressions;

/// <summary>Failures: the runtime's exceptions and texts, with the field, path and position context every read failure carries.</summary>
public ref partial struct ReadCursor
{
    /// <summary>The runtime's short-read text, for a generated view that finds its source shorter than the value.</summary>
    /// <param name="needed">The bytes the value needs.</param>
    /// <param name="available">The bytes the source has.</param>
    /// <returns>The message.</returns>
    public static string ShortReadText(long needed, long available) => ReadFailures.ShortRead(needed, available);

    /// <summary>
    ///     The most elements an array may have under <paramref name="options"/>, for a generated view that checks a
    ///     computed count against the limit itself and builds a cursor only to report a failure.
    /// </summary>
    /// <param name="options">The read options; <see langword="null"/> uses the documented defaults.</param>
    /// <returns>The <see cref="ReadOptions.MaxArrayElements"/> limit.</returns>
    public static int ArrayLimit(ReadOptions? options) => (options ?? ReadOptions.Default).MaxArrayElements;

    /// <summary>Gets whether an exception is the failure of a layout expression (a division by zero, an overflow) rather than another error, so generated code can treat an unevaluable condition as not satisfied.</summary>
    /// <param name="exception">The exception an expression raised.</param>
    /// <returns>Whether it is an expression failure.</returns>
    public static bool IsExpressionFailure(Exception exception) => LayoutExpressionEvaluator.IsExpressionFailure(exception);

    /// <summary>A layout failure (an offset assertion that does not hold) at the current position, with the runtime's context.</summary>
    /// <param name="message">The diagnostic.</param>
    /// <param name="member">The layout field.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <returns>The exception to throw.</returns>
    public readonly CStructLayoutException FailLayout(string message, string? member, string? memberType)
    {
        var exception = new CStructLayoutException(message);
        this.Attach(exception, member, memberType);
        return exception;
    }

    /// <summary>
    ///     A read failure at the current position with the runtime's context: the innermost field and its type, the
    ///     operation path, and the offset.
    /// </summary>
    /// <param name="message">The diagnostic.</param>
    /// <param name="member">The layout field, or <see langword="null"/> when none applies.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <returns>The exception to throw.</returns>
    public readonly CStructReadException Fail(string message, string? member, string? memberType)
        => this.Fail(message, member, memberType, null);

    /// <summary>The failure of a field whose value does not satisfy an <c>@expect</c> or <c>@supports</c> condition, with the runtime's context.</summary>
    /// <param name="kind">Whether the data is malformed or unsupported.</param>
    /// <param name="constraint">The condition as written in the layout.</param>
    /// <param name="field">The field's layout name.</param>
    /// <param name="member">The layout field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <param name="label">The constraint's label, or <see langword="null"/> when the layout gave it none.</param>
    /// <returns>The exception to throw.</returns>
    public readonly CStructConstraintException FailConstraint(ConstraintKind kind, string constraint, string field, string? member, string? memberType, string? label = null)
    {
        var exception = new CStructConstraintException(kind, constraint, ReadFailures.ConstraintNotSatisfied(kind, field, constraint, label), label);
        this.Attach(exception, member, memberType);
        return exception;
    }

    /// <summary>
    ///     Reports a field whose value does not satisfy an <c>@expect</c> or <c>@supports</c> condition: throws the failure, or, when
    ///     the read collects violations (<see cref="ReadOptions.ConstraintViolations"/>), adds it to the collection and returns so
    ///     the read continues.
    /// </summary>
    /// <param name="kind">Whether the data is malformed or unsupported.</param>
    /// <param name="constraint">The condition as written in the layout.</param>
    /// <param name="field">The field's layout name.</param>
    /// <param name="member">The layout field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <param name="label">The constraint's label, or <see langword="null"/> when the layout gave it none.</param>
    /// <param name="reportAt">
    ///     The source offset to report when it is not the cursor's own (a constraint pass that runs after the whole struct was
    ///     read reports the end of the checked field), or -1 for the cursor's position.
    /// </param>
    /// <exception cref="CStructConstraintException">The read does not collect violations.</exception>
    public void ConstraintViolated(ConstraintKind kind, string constraint, string field, string? member, string? memberType, string? label = null, int reportAt = -1)
    {
        if (this.RejectConstraint(kind, constraint, field, member, memberType, label, reportAt, out CStructConstraintException? failure))
        {
            throw failure;
        }
    }

    /// <summary>
    ///     Reports a field whose value does not satisfy an <c>@expect</c> or <c>@supports</c> condition without throwing: the form
    ///     for a check that returns its failure (a non-throwing <c>TryParse</c> or <c>TryValidate</c>). When the read collects
    ///     violations (<see cref="ReadOptions.ConstraintViolations"/>) the failure is added to the collection and the check goes on.
    /// </summary>
    /// <param name="kind">Whether the data is malformed or unsupported.</param>
    /// <param name="constraint">The condition as written in the layout.</param>
    /// <param name="field">The field's layout name.</param>
    /// <param name="member">The layout field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <param name="label">The constraint's label, or <see langword="null"/> when the layout gave it none.</param>
    /// <param name="reportAt">The source offset to report when it is not the cursor's own, or -1 for the cursor's position.</param>
    /// <param name="failure">
    ///     The failure when the method returns <see langword="true"/>: its position is not attached yet (pass it to
    ///     <see cref="Complete"/> before handing it out, as a catch block does for a thrown one).
    /// </param>
    /// <returns><see langword="true"/> when the check must stop with <paramref name="failure"/>; <see langword="false"/> when the failure was collected and the check goes on.</returns>
    public bool RejectConstraint(ConstraintKind kind, string constraint, string field, string? member, string? memberType, string? label, int reportAt, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out CStructConstraintException? failure)
    {
        CStructConstraintException exception = this.FailConstraint(kind, constraint, field, member, memberType, label);
        if (this.settings.ConstraintViolations is not { } violations)
        {
            // Stopping: the cursor stands where the failure is reported, and its position is attached at the operation boundary (Complete),
            // where the runtime attaches it (a pointer target's failure reports the position after the pointer).
            if (reportAt >= 0)
            {
                this.position = reportAt;
            }

            failure = exception;
            return true;
        }

        exception.AttachContext(null, (reportAt >= 0 ? reportAt : this.position) + this.settings.OffsetBase);
        violations.Add(exception);
        failure = null;
        return false;
    }

    /// <summary>A read failure at the current position with the runtime's context and a lower-level cause.</summary>
    /// <param name="message">The diagnostic.</param>
    /// <param name="member">The layout field, or <see langword="null"/> when none applies.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <param name="cause">The exception that caused the failure, or <see langword="null"/>.</param>
    /// <returns>The exception to throw.</returns>
    public readonly CStructReadException Fail(string message, string? member, string? memberType, Exception? cause)
    {
        var exception = cause is null ? new CStructReadException(message) : new CStructReadException(message, cause);
        this.Attach(exception, member, memberType);
        return exception;
    }

    /// <summary>A limit failure at the current position, with the runtime's context.</summary>
    /// <param name="message">The diagnostic.</param>
    /// <param name="member">The layout field, or <see langword="null"/> when none applies.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <returns>The exception to throw.</returns>
    internal readonly CStructReadLimitException FailLimit(string message, string? member, string? memberType)
    {
        var exception = new CStructReadLimitException(message);
        this.Attach(exception, member, memberType);
        return exception;
    }

    /// <summary>
    ///     The failure of a layout expression (an array length, a condition) evaluated by generated code: the
    ///     runtime's <c>Cannot evaluate {context}: {reason}</c> text around an exception one of the
    ///     <see cref="Expressions"/> operators raised.
    /// </summary>
    /// <param name="exception">The exception the expression raised.</param>
    /// <param name="context">What was being evaluated, as the runtime names it (<c>array length for items</c>).</param>
    /// <param name="member">The layout field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling.</param>
    /// <returns>The exception to throw, or <paramref name="exception"/> itself when it is not an expression failure.</returns>
    public readonly Exception FailExpression(Exception exception, string context, string? member, string? memberType)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (!LayoutExpressionEvaluator.IsExpressionFailure(exception))
        {
            return exception;
        }

        var failure = new CStructReadException(LayoutExpressionEvaluator.DescribeFailure(context, exception), exception);
        this.Attach(failure, member, memberType);
        return failure;
    }

    /// <summary>
    ///     Records the position an operation had reached when <paramref name="exception"/> left it, as the runtime
    ///     does at its operation boundary: a pointer target's failure reports the position after the pointer, not
    ///     the position inside the target. Generated <c>Parse</c> methods call this in their catch block.
    /// </summary>
    /// <param name="exception">The failure leaving the operation.</param>
    public readonly void Complete(CStructException exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        exception.AttachContext(this.path, this.position + this.settings.OffsetBase);
    }

    /// <summary>Attaches field and path context while leaving the final offset for the operation boundary.</summary>
    /// <param name="exception">The failure to enrich without replacing its original cause.</param>
    /// <param name="member">The innermost field name, when known.</param>
    /// <param name="memberType">The field's layout type spelling.</param>
    private readonly void Attach(CStructException exception, string? member, string? memberType)
    {
        if (member is not null)
        {
            exception.AttachMember(member, memberType);
        }

        // The offset is attached when the exception leaves the operation (Complete), where the runtime attaches it.
        exception.AttachContext(this.path, null);
    }

    /// <summary>Moves to the source end and creates the short-read failure a complete attempted read would produce.</summary>
    /// <param name="count">The requested byte count, not an element count.</param>
    /// <param name="member">The field being read.</param>
    /// <param name="memberType">The field's layout type spelling.</param>
    /// <returns>The contextual failure; the caller throws it.</returns>
    /// <exception cref="Streams.BufferedInputShortfallException">The source is the first part of an input that holds more of the bytes.</exception>
    private CStructReadException ShortRead(int count, string member, string? memberType)
    {
        this.RequireBuffered(this.position, (long)this.position + count);
        int available = this.Remaining;
        this.position = this.source.Length;
        return this.Fail(ReadFailures.ShortRead(count, available), member, memberType);
    }
}
