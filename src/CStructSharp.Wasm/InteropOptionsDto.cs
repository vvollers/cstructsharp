namespace CStructSharpWeb.Wasm;

using System.Text.Json.Serialization;

/// <summary>
///     The one options object every browser operation accepts (contract v8, camelCase on the wire). Compile-time
///     choices (placement, byte order, pointer width, bitfield rules, compiler limits) and per-operation choices
///     (root, budgets, pointer policy, diagnostics) travel together; the adapter merges what the caller passed to
///     <c>compile</c> with what it passed to the operation.
/// </summary>
public sealed class InteropOptionsDto
{
    /// <summary>
    ///     Whether stored pointer values are absolute stream positions (<c>"Absolute"</c>) or offsets from
    ///     <see cref="Origin"/> (<c>"Relative"</c>), matched case-insensitively; <c>"Absolute"</c> when omitted.
    /// </summary>
    [JsonPropertyName("addressingMode")]
    public string? AddressingMode { get; set; }

    /// <summary>Whether the layout inserts Portable alignment padding; <see langword="false"/> when omitted.</summary>
    [JsonPropertyName("aligned")]
    public bool? Aligned { get; set; }

    /// <summary>
    ///     Which end of a storage unit the first bitfield takes: <c>"LowBitFirst"</c> or <c>"HighBitFirst"</c>
    ///     (case-insensitive); <c>"LowBitFirst"</c> when omitted.
    /// </summary>
    [JsonPropertyName("bitfieldAllocation")]
    public string? BitfieldAllocation { get; set; }

    /// <summary>
    ///     How adjacent bitfields of different declared sizes share storage: <c>"SysV"</c> (GCC/Clang) or
    ///     <c>"Msvc"</c>, which keeps one unit per size (case-insensitive); <c>"SysV"</c> when omitted.
    /// </summary>
    [JsonPropertyName("bitfieldPacking")]
    public string? BitfieldPacking { get; set; }

    /// <summary>
    ///     Whether an update clears a union's storage before writing its selected member; <see langword="true"/>
    ///     when omitted. Used only by update.
    /// </summary>
    [JsonPropertyName("clearUnionStorage")]
    public bool? ClearUnionStorage { get; set; }

    /// <summary>The width of C <c>long</c> in bits: 32 or 64; 64 when omitted. Other values are rejected.</summary>
    [JsonPropertyName("cLongWidth")]
    public int? CLongWidth { get; set; }

    /// <summary>
    ///     Whether a parse follows non-null pointers, and whether an update path may pass through a pointer's
    ///     <c>.value</c> target; <see langword="true"/> when omitted.
    /// </summary>
    [JsonPropertyName("dereferencePointers")]
    public bool? DereferencePointers { get; set; }

    /// <summary>The layout's default byte order: little-endian when true or omitted, big-endian when false.</summary>
    [JsonPropertyName("littleEndian")]
    public bool? LittleEndian { get; set; }

    /// <summary>
    ///     The greatest number of elements one array field may read or write; 1,000,000 when omitted, accepted from 1
    ///     through 2,147,483,647.
    /// </summary>
    [JsonPropertyName("maxArrayElements")]
    public int? MaxArrayElements { get; set; }

    /// <summary>
    ///     The greatest layout-definition length in characters the compiler accepts; 131,072 (128 Ki) when omitted,
    ///     which is also its maximum.
    /// </summary>
    [JsonPropertyName("maxDefinitionLength")]
    public int? MaxDefinitionLength { get; set; }

    /// <summary>
    ///     The greatest syntax-tree or identifier-dependency depth of one layout expression; 256 when omitted, which
    ///     is also its maximum.
    /// </summary>
    [JsonPropertyName("maxExpressionNestingDepth")]
    public int? MaxExpressionNestingDepth { get; set; }

    /// <summary>
    ///     The greatest number of expression nodes one evaluation session may compile or execute; 100,000 when
    ///     omitted, which is also its maximum.
    /// </summary>
    [JsonPropertyName("maxExpressionTokens")]
    public int? MaxExpressionTokens { get; set; }

    /// <summary>
    ///     The greatest brace-nesting depth accepted in a layout definition; 256 when omitted, which is also its
    ///     maximum.
    /// </summary>
    [JsonPropertyName("maxLayoutNestingDepth")]
    public int? MaxLayoutNestingDepth { get; set; }

    /// <summary>
    ///     The greatest active struct or union depth one parse, serialize, or update may enter; 256 when omitted,
    ///     which is also its maximum.
    /// </summary>
    [JsonPropertyName("maxNestingDepth")]
    public int? MaxNestingDepth { get; set; }

    /// <summary>
    ///     The greatest number of nested pointer dereferences on one parse branch; 64 when omitted, which is also its
    ///     maximum.
    /// </summary>
    [JsonPropertyName("maxPointerDepth")]
    public int? MaxPointerDepth { get; set; }

    /// <summary>
    ///     The greatest decoded size, in bytes, of one pointer target a parse may read; null or omitted leaves the
    ///     target size unrestricted. When set, variable-length string targets are rejected. Not an address limit.
    /// </summary>
    [JsonPropertyName("maxPointerTargetBytes")]
    public long? MaxPointerTargetBytes { get; set; }

    /// <summary>
    ///     The greatest encoded-byte length of one string field, including its terminator or fixed-buffer padding;
    ///     16 MiB when omitted, accepted from 1 through 2,147,483,647.
    /// </summary>
    [JsonPropertyName("maxStringBytes")]
    public long? MaxStringBytes { get; set; }

    /// <summary>
    ///     The greatest total bytes one parse or address resolution may physically read (seeking across a gap is
    ///     free); 64 MiB when omitted, at most <c>Number.MAX_SAFE_INTEGER</c>.
    /// </summary>
    [JsonPropertyName("maxTotalBytesRead")]
    public long? MaxTotalBytesRead { get; set; }

    /// <summary>
    ///     The greatest total bytes one serialize or update may write, counting rewrites of shared storage again;
    ///     64 MiB when omitted, at most <c>Number.MAX_SAFE_INTEGER</c>.
    /// </summary>
    [JsonPropertyName("maxTotalBytesWritten")]
    public long? MaxTotalBytesWritten { get; set; }

    /// <summary>
    ///     The greatest total bytes an update may physically read while locating its target path; 64 MiB when
    ///     omitted, at most <c>Number.MAX_SAFE_INTEGER</c>.
    /// </summary>
    [JsonPropertyName("maxTraversalBytesRead")]
    public long? MaxTraversalBytesRead { get; set; }

    /// <summary>
    ///     The greatest active struct depth an update may enter while locating its target path; 256 when omitted,
    ///     which is also its maximum.
    /// </summary>
    [JsonPropertyName("maxTraversalNestingDepth")]
    public int? MaxTraversalNestingDepth { get; set; }

    /// <summary>
    ///     The greatest nested pointer depth an update may follow while locating its target path; 64 when omitted,
    ///     which is also its maximum.
    /// </summary>
    [JsonPropertyName("maxTraversalPointerDepth")]
    public int? MaxTraversalPointerDepth { get; set; }

    /// <summary>
    ///     The greatest fixed-size pointer target, in bytes, an update may reach while locating its target path;
    ///     null or omitted leaves it unrestricted. When set, variable-length targets are rejected.
    /// </summary>
    [JsonPropertyName("maxTraversalPointerTargetBytes")]
    public long? MaxTraversalPointerTargetBytes { get; set; }

    /// <summary>
    ///     The greatest encoded-byte length, including the terminator, an update may scan in one terminated string
    ///     while locating its target path; 16 MiB when omitted, accepted from 1 through 2,147,483,647.
    /// </summary>
    [JsonPropertyName("maxTraversalStringBytes")]
    public long? MaxTraversalStringBytes { get; set; }

    /// <summary>
    ///     The signed base position for relative pointers, as decimal text so large values stay exact (the adapter
    ///     converts a number or bigint); 0 when omitted. A parse adds it to relative offsets, a write subtracts it.
    /// </summary>
    [JsonPropertyName("origin")]
    public string? Origin { get; set; }

    /// <summary>The stored pointer width in bytes: 1, 2, 4, or 8; 8 when omitted. Other values are rejected.</summary>
    [JsonPropertyName("pointerSize")]
    public int? PointerSize { get; set; }

    /// <summary>Whether error details keep only the curated category text (no library message, path, or member).</summary>
    [JsonPropertyName("redactDiagnostics")]
    public bool? RedactDiagnostics { get; set; }

    /// <summary>
    ///     Whether an update through a pointer's <c>.value</c> requires a non-null pointer target;
    ///     <see langword="true"/> when omitted. Used only by update.
    /// </summary>
    [JsonPropertyName("requireExistingPointerTarget")]
    public bool? RequireExistingPointerTarget { get; set; }

    /// <summary>The root name or nested path an operation selects; the first declared struct when omitted.</summary>
    [JsonPropertyName("root")]
    public string? Root { get; set; }

    /// <summary>
    ///     Whether a parse drops the trailing NUL padding of fixed text (<c>char[N]</c>, bounded <c>utf8[N]</c>);
    ///     <see langword="false"/> when omitted, so padding is kept.
    /// </summary>
    [JsonPropertyName("trimFixedText")]
    public bool? TrimFixedText { get; set; }

    /// <summary>
    ///     What a write does with a value member the layout does not declare: <c>"Ignore"</c> or <c>"Reject"</c>
    ///     (case-insensitive); <c>"Ignore"</c> when omitted.
    /// </summary>
    [JsonPropertyName("unknownMembers")]
    public string? UnknownMembers { get; set; }
}
