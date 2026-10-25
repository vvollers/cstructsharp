namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Diagnostics;

/// <summary>
///     The project-wide defaults for the <c>[CStructLayout]</c> options, read from the build properties
///     <c>CStructSharpSurface</c>, <c>CStructSharpViews</c>, <c>CStructSharpEnumerators</c>, <c>CStructSharpRuntimeByteOrder</c>, <c>CStructSharpShape</c>, <c>CStructSharpConstraintFailures</c>, <c>CStructSharpKeepNames</c>, <c>CStructSharpAligned</c>,
///     <c>CStructSharpLittleEndian</c>, <c>CStructSharpPointerSize</c>, <c>CStructSharpBitfieldPacking</c>,
///     <c>CStructSharpBitfieldAllocation</c>, <c>CStructSharpCLongWidth</c> and <c>CStructSharpDefaultEnumStorage</c>.
///     An option the attribute names keeps the attribute's value; every other option takes the project's default, and
///     then the library's. A record with value equality, so an unchanged set of properties never re-runs generation.
/// </summary>
/// <param name="Surface">The default surface, or <see langword="null"/> for the library's.</param>
/// <param name="Views">The default for <c>Views</c>, or <see langword="null"/>.</param>
/// <param name="Enumerators">The default for <c>Enumerators</c>, or <see langword="null"/>.</param>
/// <param name="RuntimeByteOrder">The default for <c>RuntimeByteOrder</c>, or <see langword="null"/>.</param>
/// <param name="Shape">The default shape (<c>Class</c>, <c>Struct</c> or <c>Auto</c>), or <see langword="null"/> for the library's.</param>
/// <param name="MaxValueBytes">The default largest value-type struct in bytes (1 to 65,536), or <see langword="null"/> for the library's.</param>
/// <param name="FormatConstraintFailures">The default for <c>ConstraintFailures</c> (<see langword="true"/> for <c>FormatExceptions</c>), or <see langword="null"/>.</param>
/// <param name="KeepNames">The default for <c>KeepNames</c>, or <see langword="null"/>.</param>
/// <param name="Aligned">The default for <c>Aligned</c>, or <see langword="null"/>.</param>
/// <param name="LittleEndian">The default for <c>LittleEndian</c>, or <see langword="null"/>.</param>
/// <param name="PointerSize">The default pointer width in bytes, or <see langword="null"/>.</param>
/// <param name="BitfieldPacking">The default <see cref="CStructSharp.BitfieldPacking"/> member name, or <see langword="null"/>.</param>
/// <param name="BitfieldAllocation">The default <see cref="CStructSharp.BitfieldAllocation"/> member name, or <see langword="null"/>.</param>
/// <param name="CLongWidth">The default width of C <c>long</c> in bits, or <see langword="null"/>.</param>
/// <param name="DefaultEnumStorage">The default storage of an enum declared without one, or <see langword="null"/>.</param>
/// <param name="Invalid">The properties whose value did not parse, as <c>Name=value</c>, in property order.</param>
internal sealed record LayoutDefaults(
    GenerationSurface? Surface,
    bool? Views,
    bool? Enumerators,
    bool? RuntimeByteOrder,
    GenerationShape? Shape,
    int? MaxValueBytes,
    bool? FormatConstraintFailures,
    bool? KeepNames,
    bool? Aligned,
    bool? LittleEndian,
    int? PointerSize,
    string? BitfieldPacking,
    string? BitfieldAllocation,
    int? CLongWidth,
    string? DefaultEnumStorage,
    EquatableArray<string> Invalid)
{
    /// <summary>No project defaults: every option keeps the library's value.</summary>
    public static LayoutDefaults None { get; } = new(null, null, null, null, null, null, null, null, null, null, null, null, null, null, null, EquatableArray<string>.Empty);

    /// <summary>
    ///     Whether an attribute's named argument names its option, so that a project default must not replace it.
    ///     <c>CLongWidth = 0</c> means "the library default", so it names nothing.
    /// </summary>
    /// <param name="argument">The named argument of a <c>[CStructLayout]</c> attribute.</param>
    /// <returns>Whether the argument is an explicit choice.</returns>
    public static bool NamesOption(KeyValuePair<string, TypedConstant> argument)
        => argument.Key != "CLongWidth" || argument.Value.Value is int and not 0;

    /// <summary>Reads the defaults from the build's global analyzer options.</summary>
    /// <param name="options">The options MSBuild hands to the compiler (the <c>CompilerVisibleProperty</c> values).</param>
    /// <returns>The defaults; a property that is missing or empty is not a default, and one that does not parse is listed in <see cref="Invalid"/>.</returns>
    public static LayoutDefaults From(AnalyzerConfigOptions options)
    {
        var invalid = new List<string>();

        // The trimmed text of a CStructSharp build property, or null when it is missing or empty.
        string? Text(string name) => options.TryGetValue("build_property.CStructSharp" + name, out string? value) && !string.IsNullOrWhiteSpace(value) ? value!.Trim() : null;

        // A true/false property; a value that does not parse is recorded and ignored.
        bool? Flag(string name)
        {
            if (Text(name) is not { } text)
            {
                return null;
            }

            if (bool.TryParse(text, out bool parsed))
            {
                return parsed;
            }

            invalid.Add(name + "=" + text);
            return null;
        }

        // A numeric property restricted to the allowed values; anything else is recorded and ignored.
        int? Number(string name, params int[] allowed)
        {
            if (Text(name) is not { } text)
            {
                return null;
            }

            if (int.TryParse(text, out int parsed) && Array.IndexOf(allowed, parsed) >= 0)
            {
                return parsed;
            }

            invalid.Add(name + "=" + text);
            return null;
        }

        // A property that names one of the members, compared without regard to case; anything else is recorded and ignored.
        string? Member(string name, params string[] members)
        {
            if (Text(name) is not { } text)
            {
                return null;
            }

            foreach (string member in members)
            {
                if (string.Equals(member, text, StringComparison.OrdinalIgnoreCase))
                {
                    return member;
                }
            }

            invalid.Add(name + "=" + text);
            return null;
        }

        // A whole-number property within a range; anything else is recorded and ignored.
        int? Range(string name, int minimum, int maximum)
        {
            if (Text(name) is not { } text)
            {
                return null;
            }

            if (int.TryParse(text, out int parsed) && parsed >= minimum && parsed <= maximum)
            {
                return parsed;
            }

            invalid.Add(name + "=" + text);
            return null;
        }

        GenerationSurface? surface = Member("Surface", "Full", "Reader", "Views", "Parser") switch
        {
            "Full" => GenerationSurface.Full,
            "Reader" => GenerationSurface.Reader,
            "Views" => GenerationSurface.Views,
            "Parser" => GenerationSurface.Parser,
            _ => null,
        };

        return new LayoutDefaults(
            surface,
            Flag("Views"),
            Flag("Enumerators"),
            Flag("RuntimeByteOrder"),
            Member("Shape", "Class", "Struct", "Auto") switch { "Struct" => GenerationShape.Struct, "Class" => GenerationShape.Class, "Auto" => GenerationShape.Auto, _ => null },
            Range("MaxValueBytes", 1, 65536),
            Member("ConstraintFailures", "Constraint", "FormatExceptions") switch { "FormatExceptions" => true, "Constraint" => false, _ => null },
            Flag("KeepNames"),
            Flag("Aligned"),
            Flag("LittleEndian"),
            Number("PointerSize", 1, 2, 4, 8),
            Member("BitfieldPacking", "SysV", "Msvc"),
            Member("BitfieldAllocation", "LowBitFirst", "HighBitFirst"),
            Number("CLongWidth", 32, 64),
            Text("DefaultEnumStorage"),
            new EquatableArray<string>(invalid.ToArray()));
    }

    /// <summary>The settings a layout compiles with once the project's defaults replace the library's, before the attribute's own arguments apply.</summary>
    /// <returns>The base settings.</returns>
    public LayoutSettings BaseSettings()
    {
        LayoutSettings settings = LayoutSettings.Default;
        return settings with
        {
            Aligned = this.Aligned ?? settings.Aligned,
            LittleEndian = this.LittleEndian ?? settings.LittleEndian,
            PointerSize = this.PointerSize ?? settings.PointerSize,
            BitfieldPacking = this.BitfieldPacking ?? settings.BitfieldPacking,
            BitfieldAllocation = this.BitfieldAllocation ?? settings.BitfieldAllocation,
            CLongWidth = this.CLongWidth ?? settings.CLongWidth,
            DefaultEnumStorage = this.DefaultEnumStorage ?? settings.DefaultEnumStorage,
        };
    }
}
