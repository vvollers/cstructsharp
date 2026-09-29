namespace CStructSharp.Compilation.Programs;

/// <summary>
///     The operation of one <see cref="WriteStep"/>. Each code names exactly one thing the writer does, so an executor
///     dispatches once per step and checks no field flags. <c>Field</c> is always the member the step belongs to (an
///     index into <see cref="WriteProgram.Fields"/>), or -1 for a step outside any member.
/// </summary>
/// <remarks>
///     <para>
///         <b>Registers.</b> A frame keeps the member's supplied value (set by <see cref="LoadMember"/>,
///         <see cref="LoadPadding"/> or <see cref="LoadRoot"/>), the element count (set by <see cref="CheckFixedCount"/>,
///         <see cref="EvaluateCount"/> and <see cref="SetCount"/>; -1 means the value decides it), the exact number an enum
///         write produced (which a capture stores instead of the supplied value), and the open bitfield unit (the bits
///         already used and the unit's size in bytes).
///     </para>
///     <para>
///         <b>Member context.</b> A failure is reported with a member's name and type only while the interpreter's field
///         loop would report it: from a named member's value lookup to its capture (<see cref="WriteProgram.NotedMembers"/>).
///         Padding, anonymous promoted members, separators, selection, scope, union members and the tail belong to no member.
///     </para>
///     <para>
///         <b>Codec operand.</b> Scalar and array writes name an entry of <see cref="WriteProgram.Codecs"/>: the codec
///         identity and the catalog codec id whose stream writer encodes values the executor does not encode itself.
///     </para>
/// </remarks>
internal enum WriteOpCode : byte
{
    /// <summary>Moves the position forward by <c>A</c> bytes of padding; nothing is written, so nothing is charged.</summary>
    Seek,

    /// <summary>
    ///     Aligns the position up to a multiple of <c>A</c> bytes, measured from the composite's first byte; emitted only
    ///     where the preceding extent depends on the data.
    /// </summary>
    Align,

    /// <summary>Checks the member's <c>@N</c> offset assertion: the member must start <c>A</c> bytes past the composite's first byte.</summary>
    CheckOffset,

    /// <summary>Sets the count register to the fixed element count <c>A</c> (every element of every dimension) and checks it against the array element limit.</summary>
    CheckFixedCount,

    /// <summary>
    ///     Evaluates the member's count expression <c>A</c> (an index into <see cref="WriteProgram.Expressions"/>) in the
    ///     write domain, rejects a negative count, checks the array element limit, and sets the count register.
    /// </summary>
    EvaluateCount,

    /// <summary>
    ///     Sets the count register to <c>A</c> without a check, for an array whose value gives its element count: -1 for
    ///     its elements, and 1 for text, which the interpreter writes as a buffer of one character there.
    /// </summary>
    SetCount,

    /// <summary>
    ///     Looks the member's value up in the frame's data - by slot <c>A</c> of a struct value of the program's shape,
    ///     otherwise by name - and rejects a missing value, and a null one unless the member is a scalar pointer.
    /// </summary>
    LoadMember,

    /// <summary>Sets the value register to the all-zero value unnamed padding is written with.</summary>
    LoadPadding,

    /// <summary>
    ///     Sets the value register to the frame's data - the root value of a root field program, or the selected value of a
    ///     union member - and rejects a null one unless the member is a scalar pointer.
    /// </summary>
    LoadRoot,

    /// <summary>Encodes the value register as one fixed-width number of codec <c>A</c> and writes it.</summary>
    WriteNumeric,

    /// <summary>
    ///     Encodes the value register through the stream writer of codec <c>A</c> (characters, wide integers, fixed point,
    ///     identifiers, LEB128, terminated text, a caller's codec); a conversion failure names the member's value field.
    /// </summary>
    WriteCodecValue,

    /// <summary>Resolves the value register as a member of enum <c>B</c>, writes its storage value through codec <c>A</c>, and keeps its exact number.</summary>
    WriteEnum,

    /// <summary>Writes the value register as fixed-capacity text of the counted characters or bytes, padded with zeroes.</summary>
    WriteText,

    /// <summary>
    ///     Writes the counted fixed-width numbers of codec <c>A</c>: as one block from typed storage where the interpreter
    ///     takes its block path (<c>B</c> is 0: a member a struct places, with a declared count), otherwise element by element.
    /// </summary>
    WriteNumericArray,

    /// <summary>
    ///     Writes the counted elements (or, with a count of -1, every element the value holds) one by one as elements of kind
    ///     <c>B</c> (<see cref="WriteElementKind"/>) with operand <c>A</c>, observing cancellation before each element whose
    ///     type is a struct or union.
    /// </summary>
    WriteElements,

    /// <summary>Writes a multidimensional array's flattened row-major leaves one by one as elements of kind <c>B</c> with operand <c>A</c>.</summary>
    WriteLeaves,

    /// <summary>Writes a multidimensional character array row by row, each row fixed-capacity text of the innermost dimension (codec <c>A</c>).</summary>
    WriteTextTable,

    /// <summary>Writes the all-zero element of <c>A</c> bytes that ends a terminated array.</summary>
    WriteTerminator,

    /// <summary>Writes <c>A</c> zero bytes: unnamed padding of a caller's codec, whatever the codec would encode.</summary>
    WriteZeroes,

    /// <summary>Writes the value register as a pointer's stored address, encoded by the operation's addressing mode and origin.</summary>
    WritePointer,

    /// <summary>
    ///     Merges the value register into the open bitfield unit: the unit's existing bytes are read back (zero past the
    ///     output's end), the member's bits replaced, and the whole unit written again.
    /// </summary>
    WriteBitfield,

    /// <summary>
    ///     Writes the value register as a nested struct or union through program <c>A</c> (an index into
    ///     <see cref="WriteProgram.Nested"/>), with qualified prefix <c>B</c> active when it is not -1.
    /// </summary>
    WriteStruct,

    /// <summary>
    ///     Writes an anonymous promoted struct (or, inside a union member, a promoted union) through program <c>A</c> from the
    ///     frame's own data; it claims no nesting level, with qualified prefix <c>B</c> active when it is not -1.
    /// </summary>
    WritePromotedStruct,

    /// <summary>
    ///     Writes an anonymous promoted union of program <c>A</c> from the frame's own data: the widest member the data
    ///     supplies is staged into the union's storage and the storage written once.
    /// </summary>
    WritePromotedUnion,

    /// <summary>Writes the frame's data as the root struct or union of program <c>A</c>.</summary>
    WriteRootStruct,

    /// <summary>Captures the supplied value (or the enum register's exact number) into slot <c>A</c> and publishes it under qualified targets <c>B</c> (-1 for none).</summary>
    CaptureValue,

    /// <summary>Stores the member's not-a-number value in slot <c>A</c> and publishes it under qualified targets <c>B</c> (-1 for none).</summary>
    CaptureNotANumber,

    /// <summary>
    ///     Tests conditional branch <c>A</c>; when the frame's group selected another arm, rejects a value supplied for any
    ///     name the member makes visible and continues at step <c>B</c>, after the member.
    /// </summary>
    SelectArm,

    /// <summary>Makes every kept name of the composite's conditional scope undefined, as the scope does on entry.</summary>
    EnterConditionalScope,

    /// <summary>After an active member of a conditional composite, saves the member's names and restores the replaced ones.</summary>
    CompleteMember,

    /// <summary>
    ///     Writes the composite's tail padding as zeroes: <c>A</c> bytes when known when the program was built, otherwise up
    ///     to the next multiple of the composite's alignment <c>B</c> from its first byte.
    /// </summary>
    FinishComposite,

    /// <summary>Places a member of a struct with bitfields through the frame's runtime placement cursor, closing any open unit.</summary>
    PlaceMember,

    /// <summary>Places a bitfield through the frame's runtime placement cursor, which opens or shares its storage unit.</summary>
    PlaceBitfield,

    /// <summary>Applies a <c>: 0</c> separator through the frame's runtime placement cursor and moves past it.</summary>
    PlaceSeparator,

    /// <summary>Tells the frame's runtime placement cursor where a member that is not a bitfield ended.</summary>
    CompletePlacement,

    /// <summary>
    ///     Ends a struct with bitfields: moves to where the runtime placement cursor ended (past a reserved unit) and writes
    ///     the tail padding up to the composite's alignment <c>B</c> as zeroes.
    /// </summary>
    FinishPlaced,

    /// <summary>Opens a standalone bitfield's own storage unit of its declared size, at bit 0.</summary>
    OpenBitfieldUnit,

    /// <summary>Moves a union member's write to the union's first byte (of its staged storage) and closes any bitfield unit.</summary>
    RewindToUnionStart,

    /// <summary>Ends a union member's segment: the frame returns.</summary>
    Return,

    /// <summary>Evaluates a <c>#define</c> root's value <c>A</c> in the write domain and stores it in slot <c>B</c> (-1 for none).</summary>
    EvaluateDefinition,
}
