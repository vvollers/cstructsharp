namespace CStructSharp.Engine;

/// <summary>
///     The kinds of public operation the engine selector decides for. Each names the general-path work one public
///     call starts, so eligibility can differ per kind (a debug parse or an update may be declined while a plain root
///     read is taken).
/// </summary>
internal enum EngineOperation
{
    /// <summary>A whole root read: <c>Parse</c>, <c>ParseAsync</c>, one record of <c>ParseMany</c>, or <c>ReadValue</c> of a bare root.</summary>
    RootRead,

    /// <summary>A read of a nested path: <c>Parse</c> or <c>ReadValue</c> of a member, element, or pointer target.</summary>
    PathRead,

    /// <summary>A read that records debug byte ranges: <c>ParseWithDebug</c> or <c>ReadValueWithDebug</c>, of a root or a path.</summary>
    DebugRead,

    /// <summary><c>ResolveAddress</c>: locating a path without reading its value.</summary>
    AddressResolution,

    /// <summary><c>GetArrayLength</c>: reading only what determines an array's element count or a string's length.</summary>
    LengthQuery,

    /// <summary>A write of a root or a path: <c>Serialize</c> to an array, a span, or a buffer writer, <c>Write</c>, and <c>WriteAsync</c>.</summary>
    Write,

    /// <summary>An in-place replacement: <c>Update</c> of a span or a stream, and <c>UpdateAsync</c>.</summary>
    Update,
}
