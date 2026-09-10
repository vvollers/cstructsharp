namespace CStructSharp;

/// <summary>
/// Identifies the stable semantic category of a <see cref="CStructException"/> without requiring message parsing.
/// </summary>
public enum CStructErrorCode
{
    /// <summary>The layout source cannot be compiled into a valid binary layout.</summary>
    InvalidLayout = 1,

    /// <summary>A requested declaration, member, index, or accessor cannot be resolved.</summary>
    InvalidPath = 2,

    /// <summary>Binary input could not be read or decoded.</summary>
    ReadFailed = 3,

    /// <summary>A configured read or traversal safety limit was exceeded.</summary>
    ReadLimitExceeded = 4,

    /// <summary>Caller data could not be encoded or written.</summary>
    WriteFailed = 5,

    /// <summary>A configured output safety limit was exceeded.</summary>
    WriteLimitExceeded = 6,
}
