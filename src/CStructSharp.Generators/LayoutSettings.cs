namespace CStructSharp.Generators;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using CStructSharp.Codecs;
using CStructSharp.Compilation;
using Microsoft.CodeAnalysis;

/// <summary>
///     The settings a layout compiles with - a <c>[CStructLayout]</c> attribute's, or a <c>new CStruct(...)</c> call's -
///     with the runtime's defaults in one place, so the source generator and the analyzer compile a layout exactly as
///     the runtime would. A record with value equality, so it can key a cache and sit in an incremental request.
/// </summary>
/// <param name="Aligned">Whether the portable composite-alignment rules apply.</param>
/// <param name="LittleEndian">Whether neutral values are little-endian.</param>
/// <param name="PointerSize">The pointer width in bytes.</param>
/// <param name="BitfieldPacking">The <see cref="CStructSharp.BitfieldPacking"/> member name.</param>
/// <param name="BitfieldAllocation">The <see cref="CStructSharp.BitfieldAllocation"/> member name.</param>
/// <param name="CLongWidth">The width of C <c>long</c> in bits; the attribute's 0 (unset) is already 64 here.</param>
/// <param name="Defined">The preprocessor symbols defined for the layout.</param>
/// <param name="DefaultEnumStorage">The storage of an enum declared without one, or <see langword="null"/> for the default.</param>
/// <param name="Codecs">The custom codec declarations (<c>"name:size:alignment"</c>), in order.</param>
internal sealed record LayoutSettings(
    bool Aligned,
    bool LittleEndian,
    int PointerSize,
    string BitfieldPacking,
    string BitfieldAllocation,
    int CLongWidth,
    EquatableArray<string> Defined,
    string? DefaultEnumStorage,
    EquatableArray<string> Codecs)
{
    /// <summary>The runtime's defaults: packed, little-endian, 8-byte pointers, SysV bitfields from the low bit, 64-bit <c>long</c>.</summary>
    public static LayoutSettings Default { get; } = new(false, true, 8, "SysV", "LowBitFirst", 64, EquatableArray<string>.Empty, null, EquatableArray<string>.Empty);

    /// <summary>Reads the settings a <c>[CStructLayout]</c> attribute names; every other named argument is ignored.</summary>
    /// <param name="attribute">The attribute.</param>
    /// <returns>The settings, with <see cref="Default"/>'s value for each one the attribute does not name.</returns>
    public static LayoutSettings FromAttribute(AttributeData attribute)
    {
        LayoutSettings settings = Default;
        foreach (KeyValuePair<string, TypedConstant> named in attribute.NamedArguments)
        {
            settings = named.Key switch
            {
                "Aligned" => settings with { Aligned = named.Value.Value is true },
                "LittleEndian" => settings with { LittleEndian = named.Value.Value is not false },
                "PointerSize" => settings with { PointerSize = named.Value.Value is int size ? size : Default.PointerSize },
                "BitfieldPacking" => settings with { BitfieldPacking = EnumMemberName(named.Value, Default.BitfieldPacking) },
                "BitfieldAllocation" => settings with { BitfieldAllocation = EnumMemberName(named.Value, Default.BitfieldAllocation) },
                "CLongWidth" => settings with { CLongWidth = named.Value.Value is int width and not 0 ? width : Default.CLongWidth },

                // Copy the names out of compiler-owned attribute metadata into the settings' own arrays, in order.
                "Defined" => settings with { Defined = new EquatableArray<string>(named.Value.Values.Select(value => value.Value as string ?? string.Empty).ToArray()) },
                "DefaultEnumStorage" => settings with { DefaultEnumStorage = named.Value.Value as string },
                "Codecs" => settings with { Codecs = new EquatableArray<string>(named.Value.Values.Select(value => value.Value as string ?? string.Empty).ToArray()) },
                _ => settings,
            };
        }

        return settings;
    }

    /// <summary>A defined-symbol set as the compilation options take it.</summary>
    /// <param name="defined">The symbol names.</param>
    /// <returns>The set, compared ordinally.</returns>
    public static IReadOnlySet<string> DefinedSet(EquatableArray<string> defined)
    {
        var set = new HashSet<string>(defined, StringComparer.Ordinal);
#if NETSTANDARD2_0
        return new ReadOnlySetAdapter<string>(set);
#else
        return set;
#endif
    }

    /// <summary>The runtime's compilation options for these settings.</summary>
    /// <returns>The options.</returns>
    public CStructCompilationOptions CompilationOptions() => new()
    {
        CLongWidth = this.CLongWidth,
        BitfieldPacking = ParseEnum(this.BitfieldPacking, CStructSharp.BitfieldPacking.SysV),
        BitfieldAllocation = ParseEnum(this.BitfieldAllocation, CStructSharp.BitfieldAllocation.LowBitFirst),
        Defined = this.Defined.Count == 0 ? null : DefinedSet(this.Defined),
        DefaultEnumStorage = this.DefaultEnumStorage,
    };

    /// <summary>Parses the codec declarations.</summary>
    /// <param name="codecs">The parsed codecs, in order.</param>
    /// <param name="invalid">The first declaration that does not parse, or <see langword="null"/>.</param>
    /// <returns>Whether every declaration parsed.</returns>
    public bool TryParseCodecs(out List<CustomCodecDescriptor> codecs, out string? invalid)
    {
        codecs = new List<CustomCodecDescriptor>(this.Codecs.Count);
        foreach (string declaration in this.Codecs)
        {
            if (!CustomCodecDeclaration.TryParse(declaration, out CustomCodecDescriptor descriptor))
            {
                invalid = declaration;
                return false;
            }

            codecs.Add(descriptor);
        }

        invalid = null;
        return true;
    }

    /// <summary>The primitive catalog with the custom codecs, as the runtime's <c>CStruct</c> constructor builds it.</summary>
    /// <param name="codecs">The parsed codec declarations.</param>
    /// <returns>The catalog.</returns>
    /// <exception cref="ArgumentException">A codec's name or storage facts are invalid.</exception>
    public PrimitiveCatalog CodecCatalog(IReadOnlyList<CustomCodecDescriptor> codecs)
        => PrimitiveCatalog.For(this.LittleEndian, this.CLongWidth).WithCustomCodecs(codecs);

    /// <summary>Compiles a layout with these settings, as the runtime's <c>CStruct</c> constructor does.</summary>
    /// <param name="definition">The layout text.</param>
    /// <param name="catalog">The catalog from <see cref="CodecCatalog"/>.</param>
    /// <returns>The compiled layout.</returns>
    /// <exception cref="Diagnostics.CStructLayoutException">The layout is invalid.</exception>
    /// <exception cref="ArgumentException">A setting is out of range.</exception>
    public LayoutCompilation Compile(string definition, PrimitiveCatalog catalog)
        => LayoutCompilation.Create(definition, this.PointerSize, this.Aligned, this.LittleEndian, this.CompilationOptions(), catalog);

    /// <summary>The member name of an enum-typed attribute argument.</summary>
    /// <param name="constant">The argument.</param>
    /// <param name="fallback">The name when the value names no member.</param>
    /// <returns>The member name.</returns>
    private static string EnumMemberName(TypedConstant constant, string fallback)
    {
        if (constant.Type is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType && constant.Value is not null)
        {
            foreach (IFieldSymbol member in enumType.GetMembers().OfType<IFieldSymbol>())
            {
                if (member.HasConstantValue && Equals(member.ConstantValue, constant.Value))
                {
                    return member.Name;
                }
            }
        }

        return fallback;
    }

    /// <summary>Parses an enum member name.</summary>
    /// <typeparam name="TEnum">The enum type.</typeparam>
    /// <param name="name">The member name.</param>
    /// <param name="fallback">The value when the name is not a member.</param>
    /// <returns>The value.</returns>
    private static TEnum ParseEnum<TEnum>(string name, TEnum fallback)
        where TEnum : struct
        => Enum.TryParse(name, out TEnum value) ? value : fallback;
}
