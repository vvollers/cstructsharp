namespace CStructSharp.Compilation.Programs;

/// <summary>How a pointer's final target is read (<see cref="ReadPointerTarget"/>), listed in the order the kinds are checked.</summary>
internal enum ReadPointerTargetKind : byte
{
    /// <summary>An enum or flag value through its storage codec.</summary>
    Enum,

    /// <summary>A struct or union, read as a member of that type would be.</summary>
    Composite,

    /// <summary>Text up to its terminator (<c>char *</c> and the other string pointers).</summary>
    Terminated,

    /// <summary>Any other value, decoded by its codec: a built-in codec by the engine, a caller's codec through its adapter.</summary>
    Value,

    /// <summary>A type no reader exists for (<c>void</c> below the first level): reading it fails.</summary>
    NoReader,

    /// <summary>Counted characters, read one by one into a string (trimmed; wide characters validated).</summary>
    CountedText,

    /// <summary>Counted fixed-width numbers, read in blocks into a typed array.</summary>
    CountedNumbers,

    /// <summary>Counted structs or unions, one after another into a list.</summary>
    CountedComposites,

    /// <summary>Counted enum values into a list.</summary>
    CountedEnums,

    /// <summary>Counted values of any other codec into a list.</summary>
    CountedValues,
}
