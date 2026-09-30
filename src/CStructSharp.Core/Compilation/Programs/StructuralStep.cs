namespace CStructSharp.Compilation.Programs;

/// <summary>
///     The steps of a struct walk that read and write programs share (<see cref="StructProgramCompiler{TBuilder}"/>): a
///     builder emits each as its own opcode of the same name (<see cref="ReadOpCode"/>, <see cref="WriteOpCode"/>), with the
///     operands that opcode documents.
/// </summary>
internal enum StructuralStep
{
    /// <summary>Removes a composite's kept names at its entry (<c>EnterConditionalScope</c>).</summary>
    EnterConditionalScope,

    /// <summary>Tests one <c>if</c>/<c>switch</c> arm a member sits in and skips the member when it is not selected (<c>SelectArm</c>).</summary>
    SelectArm,

    /// <summary>Saves and restores the names a conditional member's scope affects (<c>CompleteMember</c>).</summary>
    CompleteMember,

    /// <summary>Places a member through the runtime placement cursor (<c>PlaceMember</c>).</summary>
    PlaceMember,

    /// <summary>Places a bitfield in the runtime placement cursor's storage unit (<c>PlaceBitfield</c>).</summary>
    PlaceBitfield,

    /// <summary>Applies a zero-width bitfield separator through the runtime placement cursor (<c>PlaceSeparator</c>).</summary>
    PlaceSeparator,

    /// <summary>Tells the runtime placement cursor where a member ended (<c>CompletePlacement</c>).</summary>
    CompletePlacement,

    /// <summary>Moves forward over padding whose size is known (<c>Seek</c>).</summary>
    Seek,

    /// <summary>Aligns the position from the struct's first byte, because the padding depends on the data (<c>Align</c>).</summary>
    Align,

    /// <summary>Checks a member's <c>@N</c> offset assertion at run time (<c>CheckOffset</c>).</summary>
    CheckOffset,

    /// <summary>Ends a struct whose members the runtime placement cursor placed (<c>FinishPlaced</c>).</summary>
    FinishPlaced,

    /// <summary>Ends a struct with its tail padding, known or aligned at run time (<c>FinishComposite</c>).</summary>
    FinishComposite,
}
