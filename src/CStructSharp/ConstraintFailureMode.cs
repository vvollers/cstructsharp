namespace CStructSharp;

/// <summary>
///     Chooses the exception type a generated reader throws for a field that breaks an <c>@expect</c> or <c>@supports</c>
///     constraint. It is the <see cref="CStructLayoutAttribute.ConstraintFailures"/> option (project default: the build property
///     <c>CStructSharpConstraintFailures</c>).
/// </summary>
/// <remarks>
///     The mode applies to the generated <c>Parse</c> methods of every input kind, the sequence and stream readers, the record
///     enumerators and a view's <c>Validate</c>. The non-throwing forms (<c>TryParse</c>, <c>TryValidate</c>, <c>Matches</c>) and
///     the violations a read collects are failure values and do not change. A layout read through the runtime engine
///     (<c>CStruct.Parse</c>) or the browser engine always throws <see cref="Diagnostics.CStructConstraintException"/>.
/// </remarks>
public enum ConstraintFailureMode
{
    /// <summary>A failed constraint throws <see cref="Diagnostics.CStructConstraintException"/>, as the runtime does.</summary>
    Constraint = 0,

    /// <summary>
    ///     A failed <c>@expect</c> throws <see cref="System.IO.InvalidDataException"/> and a failed <c>@supports</c> throws
    ///     <see cref="System.NotSupportedException"/>. The message is the constraint failure's, and the
    ///     <see cref="Diagnostics.CStructConstraintException"/> is the inner exception, so its label, kind, member, path and offset stay available.
    /// </summary>
    FormatExceptions = 1,
}
