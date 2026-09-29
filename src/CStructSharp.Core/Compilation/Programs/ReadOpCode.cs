namespace CStructSharp.Compilation.Programs;

/// <summary>
///     The operation of one <see cref="ReadStep"/>. Each code names exactly one thing the reader does, so an executor
///     dispatches once per step and checks no field flags: the value kind and byte order of a scalar, the element kind of
///     an array, the kind of value a capture stores. The operands' meaning is listed per code; <c>Field</c> is always
///     the member the step belongs to (an index into <see cref="ReadProgram.Fields"/>), or -1 for a step outside any
///     member.
/// </summary>
/// <remarks>
///     <para>
///         <b>Member context.</b> A failure of a step whose <c>Field</c> is a member is reported with that member's name
///         and type (<c>CStructException.NoteMember</c>), as the interpreter's field loop does; selection steps and the
///         composite's finish belong to no member, like the interpreter's code outside that loop's <c>try</c>. A root
///         program notes no member (<see cref="ReadProgram.NotesMembers"/>).
///     </para>
///     <para>
///         <b>Count register.</b> <see cref="CheckFixedCount"/> and <see cref="EvaluateCount"/> set the element count
///         the next array step reads; the count is evaluated and checked before the field is placed, as the interpreter
///         orders it. A data-sized array's count (<see cref="CountToEnd"/>, <see cref="CountTerminated"/>) is taken from the
///         input after placement, at the array's start.
///     </para>
///     <para>
///         <b>Codec operand.</b> Scalar and array reads name an entry of <see cref="ReadProgram.Codecs"/>: the element's
///         <see cref="Codecs.PrimitiveCodec"/> (size, kind, byte order) and the catalog codec id whose reader the
///         stream path calls.
///     </para>
/// </remarks>
internal enum ReadOpCode : byte
{
    /// <summary>Moves the position forward by <c>A</c> bytes of padding, which is skipped rather than read.</summary>
    Seek,

    /// <summary>
    ///     Aligns the position up to a multiple of <c>A</c> bytes, measured from the composite's first byte; emitted only
    ///     where the preceding extent depends on the data, so the padding is not known when the program is built.
    /// </summary>
    Align,

    /// <summary>Checks the member's <c>@N</c> offset assertion: the position must be <c>A</c> bytes past the composite's first byte.</summary>
    CheckOffset,

    /// <summary>
    ///     Sets the count register to the fixed element count <c>A</c> - every element of every dimension of a
    ///     multidimensional array - and checks it against the array element limit.
    /// </summary>
    CheckFixedCount,

    /// <summary>
    ///     Evaluates the member's count expression <c>A</c> (an index into <see cref="ReadProgram.Expressions"/>), rejects a
    ///     negative count, checks the array element limit, and sets the count register.
    /// </summary>
    EvaluateCount,

    /// <summary>
    ///     Sets the count register to the number of whole elements of <c>A</c> bytes between the position (the member's
    ///     placed start) and the end of the input, and checks it against the array element limit; nothing is read. An
    ///     <c>[EOF]</c> array: a start past the end or a trailing partial element fails.
    /// </summary>
    CountToEnd,

    /// <summary>
    ///     Sets the count register to the number of elements of <c>A</c> bytes before the first all-zero element, scanning
    ///     from the member's placed start and returning there; the limit is checked per element. Every byte the scan reads
    ///     is charged, and the elements and their terminator are charged again when the array step reads them. A
    ///     terminated array (<c>T items[]</c>): input that ends before an all-zero element fails at the array's start.
    /// </summary>
    CountTerminated,

    /// <summary>Reads a <c>uint8</c> scalar (codec <c>A</c>).</summary>
    ReadUInt8,

    /// <summary>Reads an <c>int8</c> scalar (codec <c>A</c>).</summary>
    ReadInt8,

    /// <summary>Reads a one-byte <c>bool</c> scalar (codec <c>A</c>).</summary>
    ReadBool,

    /// <summary>Reads a little-endian <c>int16</c> scalar (codec <c>A</c>).</summary>
    ReadInt16Le,

    /// <summary>Reads a big-endian <c>int16</c> scalar (codec <c>A</c>).</summary>
    ReadInt16Be,

    /// <summary>Reads a little-endian <c>uint16</c> scalar (codec <c>A</c>).</summary>
    ReadUInt16Le,

    /// <summary>Reads a big-endian <c>uint16</c> scalar (codec <c>A</c>).</summary>
    ReadUInt16Be,

    /// <summary>Reads a little-endian <c>int24</c> scalar (codec <c>A</c>).</summary>
    ReadInt24Le,

    /// <summary>Reads a big-endian <c>int24</c> scalar (codec <c>A</c>).</summary>
    ReadInt24Be,

    /// <summary>Reads a little-endian <c>uint24</c> scalar (codec <c>A</c>).</summary>
    ReadUInt24Le,

    /// <summary>Reads a big-endian <c>uint24</c> scalar (codec <c>A</c>).</summary>
    ReadUInt24Be,

    /// <summary>Reads a little-endian <c>int32</c> scalar (codec <c>A</c>).</summary>
    ReadInt32Le,

    /// <summary>Reads a big-endian <c>int32</c> scalar (codec <c>A</c>).</summary>
    ReadInt32Be,

    /// <summary>Reads a little-endian <c>uint32</c> scalar (codec <c>A</c>).</summary>
    ReadUInt32Le,

    /// <summary>Reads a big-endian <c>uint32</c> scalar (codec <c>A</c>).</summary>
    ReadUInt32Be,

    /// <summary>Reads a little-endian <c>int64</c> scalar (codec <c>A</c>).</summary>
    ReadInt64Le,

    /// <summary>Reads a big-endian <c>int64</c> scalar (codec <c>A</c>).</summary>
    ReadInt64Be,

    /// <summary>Reads a little-endian <c>uint64</c> scalar (codec <c>A</c>).</summary>
    ReadUInt64Le,

    /// <summary>Reads a big-endian <c>uint64</c> scalar (codec <c>A</c>).</summary>
    ReadUInt64Be,

    /// <summary>Reads a little-endian <c>float32</c> scalar (codec <c>A</c>).</summary>
    ReadFloat32Le,

    /// <summary>Reads a big-endian <c>float32</c> scalar (codec <c>A</c>).</summary>
    ReadFloat32Be,

    /// <summary>Reads a little-endian <c>float64</c> scalar (codec <c>A</c>).</summary>
    ReadFloat64Le,

    /// <summary>Reads a big-endian <c>float64</c> scalar (codec <c>A</c>).</summary>
    ReadFloat64Be,

    /// <summary>Reads an <c>int48</c> scalar in the codec's byte order (codec <c>A</c>).</summary>
    ReadInt48,

    /// <summary>Reads a <c>uint48</c> scalar in the codec's byte order (codec <c>A</c>).</summary>
    ReadUInt48,

    /// <summary>Reads an <c>int128</c> scalar in the codec's byte order (codec <c>A</c>).</summary>
    ReadInt128,

    /// <summary>Reads a <c>uint128</c> scalar in the codec's byte order (codec <c>A</c>).</summary>
    ReadUInt128,

    /// <summary>Reads a <c>float16</c> scalar in the codec's byte order (codec <c>A</c>).</summary>
    ReadFloat16,

    /// <summary>Reads a fixed-point scalar (16.16, 2.30, 8.8; codec <c>A</c>).</summary>
    ReadFixedPoint,

    /// <summary>Reads a 16-byte UUID or GUID scalar (codec <c>A</c>).</summary>
    ReadIdentifier,

    /// <summary>Reads a signed or unsigned LEB128 scalar, whose size the data decides (codec <c>A</c>).</summary>
    ReadLeb128,

    /// <summary>Reads a one-byte character or text unit scalar (<c>char</c>, <c>latin1</c>, <c>cp437</c>, <c>utf8</c>, ...; codec <c>A</c>).</summary>
    ReadCharacter,

    /// <summary>Reads a two-byte <c>wchar</c> scalar in the codec's byte order (codec <c>A</c>).</summary>
    ReadWideCharacter,

    /// <summary>
    ///     Reads text up to its terminator (codec <c>A</c>): a terminated string scalar such as <c>cstring</c>, or an
    ///     unsized character array (<c>char name[]</c>) through its terminated codec.
    /// </summary>
    ReadTerminatedText,

    /// <summary>Reads an enum or flag scalar through its storage codec <c>A</c> into the enum <c>B</c> (<see cref="ReadProgram.Enums"/>).</summary>
    ReadEnum,

    /// <summary>
    ///     Reads count-register fixed-width numbers (codec <c>A</c>) of a member placed by its composite as one typed
    ///     block (<c>PrimitiveArray&lt;T&gt;</c>), with the block read's failure granularity; no elements give an empty
    ///     typed array.
    /// </summary>
    ReadNumericArray,

    /// <summary>
    ///     Reads count-register fixed-width numbers (codec <c>A</c>) of a standalone field (a root) one element at a time,
    ///     as the interpreter reads a field no composite placed, then gives them the typed array shape.
    /// </summary>
    ReadNumericElements,

    /// <summary>Reads count-register elements of another codec <c>A</c> (<c>int48</c>, <c>float16</c>, UUID, LEB128, terminated text, ...) into a list.</summary>
    ReadCodecArray,

    /// <summary>Reads count-register <c>char</c>s (codec <c>A</c>) as one Latin-1 string, trimmed as the options say.</summary>
    ReadCharArray,

    /// <summary>Reads count-register <c>wchar</c>s (codec <c>A</c>) as one string that must be valid UTF-16.</summary>
    ReadWideCharArray,

    /// <summary>Reads a byte-counted text buffer (<c>utf8 text[N]</c> and the like; codec <c>A</c>) whose capacity is the count register.</summary>
    ReadBoundedText,

    /// <summary>Reads count-register enum values (storage codec <c>A</c>, enum <c>B</c>) into a list.</summary>
    ReadEnumArray,

    /// <summary>
    ///     Reads count-register elements of an unnamed padding array one at a time through codec <c>A</c> and keeps
    ///     nothing, as the interpreter reads a field without a name.
    /// </summary>
    SkipElements,

    /// <summary>
    ///     Moves past a terminated array's all-zero terminator element of <c>A</c> bytes, which belongs to the member but
    ///     not to its value; the count step already found it, so the move cannot fail.
    /// </summary>
    SkipTerminator,

    /// <summary>
    ///     Reads one value of a caller-supplied codec (codec <c>A</c>, <c>ICustomCodec</c>) through the custom-codec
    ///     adapter the interpreter's codec delegate runs: in place from memory (the position advances, and is charged, before
    ///     a failure), through a growing window from a stream.
    /// </summary>
    ReadCustom,

    /// <summary>Reads count-register values of a caller-supplied codec <c>A</c>, each as <see cref="ReadCustom"/> reads one, into a list.</summary>
    ReadCustomArray,

    /// <summary>
    ///     Reads count-register fixed-width numbers (codec <c>A</c>) of a multidimensional member its composite placed into
    ///     a flat list, in blocks of at most 64 KiB (the interpreter's boxed bulk path); <see cref="ReshapeTable"/> nests it.
    /// </summary>
    ReadNumericList,

    /// <summary>
    ///     Reads count-register fixed-width numbers (codec <c>A</c>) of a multidimensional standalone field (a root) one at a
    ///     time into a flat list; <see cref="ReshapeTable"/> nests it.
    /// </summary>
    ReadNumericElementList,

    /// <summary>
    ///     Reads count-register structs, each with program <c>A</c> and a fresh conditional selection, into a flat list,
    ///     never taking the element struct's block path over the whole array: the elements of a multidimensional array,
    ///     which <see cref="ReshapeTable"/> nests.
    /// </summary>
    ReadStructElements,

    /// <summary>
    ///     Reads the count-register characters (codec <c>A</c>) of a multidimensional <c>char</c> or <c>wchar</c> array one
    ///     at a time, makes each innermost row a string (trimmed as the options say; a <c>wchar</c> row must be valid
    ///     UTF-16), and nests the rows by the outer dimensions.
    /// </summary>
    ReadCharTable,

    /// <summary>
    ///     Nests the member's flat element list by its dimensions, outermost first, with a <c>List&lt;object?&gt;</c> at
    ///     every level (rows are never typed arrays), as the interpreter shapes a multidimensional array after reading its
    ///     elements; a member without a value slot is left alone.
    /// </summary>
    ReshapeTable,

    /// <summary>
    ///     Reads a nested struct with program <c>A</c> (<see cref="ReadProgram.Nested"/>) into a new value; <c>B</c> is the
    ///     member's qualified prefix (<see cref="ReadProgram.Prefixes"/>), appended to the active prefix while the nested
    ///     struct is read and removed after it, or -1 for a member no expression names through a dotted path.
    /// </summary>
    ReadStruct,

    /// <summary>
    ///     Reads an anonymous struct member with program <c>A</c> into the current value: its members are promoted into
    ///     this struct and it is not a nesting level of its own.
    /// </summary>
    ReadPromotedStruct,

    /// <summary>Reads count-register structs, each with program <c>A</c> and a fresh conditional selection, into a list.</summary>
    ReadStructArray,

    /// <summary>
    ///     Stores the integer value just read as the literal of slot <c>A</c> (any integer up to 64 bits, <c>int128</c>,
    ///     <c>bool</c> as 1 or 0, a character's code).
    /// </summary>
    CaptureInteger,

    /// <summary>
    ///     Stores the <c>uint128</c> just read in slot <c>A</c>: a literal, or an unusable wide value at or above 2^127
    ///     that fails with the exact number when an expression reads it.
    /// </summary>
    CaptureUInt128,

    /// <summary>Stores the number of the enum value just read in slot <c>A</c>: a literal, or an unusable wide value outside the domain.</summary>
    CaptureEnum,

    /// <summary>
    ///     Makes slot <c>A</c> the unusable value <c>B</c> (<see cref="ReadProgram.Unusables"/>): the member is not an
    ///     integer (a floating-point or fixed-point value, a UUID, text, an array).
    /// </summary>
    CaptureNotANumber,

    /// <summary>
    ///     <see cref="CaptureNotANumber"/> for an array whose count the data decides: applied only when the count
    ///     register is above zero, because the interpreter captures per element and an empty array captures nothing.
    /// </summary>
    CaptureNotANumberIfElements,

    /// <summary>
    ///     While a qualified prefix is active, copies bare slot <c>A</c> to the slot the active prefix and the name
    ///     spell, found in the qualified targets <c>B</c> (<see cref="ReadProgram.QualifiedTargets"/>); a spelling without
    ///     a slot is not observable and is skipped.
    /// </summary>
    PublishQualified,

    /// <summary>
    ///     Starts the composite's conditional variable scope (<see cref="ReadProgram.Scope"/>): its kept names that
    ///     have slots are removed, so an outer value is not read as the composite's own. <c>Field</c> is -1.
    /// </summary>
    EnterConditionalScope,

    /// <summary>
    ///     Selects conditional branch <c>A</c> (<see cref="ReadProgram.Branches"/>): evaluates its group's selector if this
    ///     composite instance has not yet decided that group, and continues at step <c>B</c> when the branch's arm is not
    ///     the selected one. <c>Field</c> is -1: a selector failure names no member of this composite.
    /// </summary>
    SelectArm,

    /// <summary>
    ///     After an active member was read, saves its own names' slots into the scope's locals and restores the
    ///     composite's names a nested declaration replaced (<see cref="ReadProgram.Scope"/>, member <c>Field</c>).
    /// </summary>
    CompleteMember,

    /// <summary>
    ///     Ends the composite: moves to its end past the tail padding - <c>A</c> bytes when known, or, when <c>A</c> is
    ///     -1, by aligning to the composite's alignment <c>B</c> from its first byte. <c>Field</c> is -1.
    /// </summary>
    FinishComposite,

    /// <summary>Reads a root struct with program <c>A</c> into a new value stored under the root's name. <c>Field</c> is -1.</summary>
    ReadRootStruct,

    /// <summary>
    ///     A <c>#define</c> root: evaluates its expression <c>A</c> and stores the value in slot <c>B</c> (or nowhere when
    ///     -1). Nothing is read and the root value stays empty. <c>Field</c> is -1.
    /// </summary>
    EvaluateDefinition,
}
