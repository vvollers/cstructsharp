namespace CStructSharp.Codecs;

using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using CStructSharp.Compilation;

/// <summary>
///     The compile-time knowledge about primitive types: every readable name (canonical direction-suffixed names,
///     neutral names, aliases, the long family at the layout's width), its descriptor, alignment, fixed size, and
///     the <b>codec id</b> that the runtime uses to find the delegate that reads or writes it. The catalog holds no
///     delegates and no I/O, so the same class runs inside the compiler (source generator) and at run time.
/// </summary>
/// <remarks>
///     Codec ids are the indexes of <see cref="CanonicalNames"/>; a neutral or alias name shares the id of the
///     canonical delegate it resolves to. Custom codecs registered on one layout get ids after the canonical ones
///     (see <see cref="WithCustomCodecs"/>), so a runtime table is one array per direction indexed by id.
/// </remarks>
internal sealed class PrimitiveCatalog
{
    /// <summary>The id a symbol carries when no delegate reads it: composites, <c>void</c>, and pointer-only types.</summary>
    public const int NoCodec = -1;

    /// <summary>
    ///     The canonical, direction-suffixed primitive names in registration order. The index of a name is its codec
    ///     id; the runtime registers exactly one reader and one writer delegate per entry, in this order.
    /// </summary>
    public static readonly ImmutableArray<string> CanonicalNames = ImmutableArray.Create(
        "uleb128_32",
        "uleb128_64",
        "sleb128_32",
        "sleb128_64",
        "fixed16_16>",
        "fixed16_16<",
        "ufixed16_16>",
        "ufixed16_16<",
        "fixed2_30>",
        "fixed2_30<",
        "ufixed8_8>",
        "ufixed8_8<",
        "uuid",
        "guid",
        "byte",
        "int8",
        "uint8",
        "bool",
        "char",
        "latin1",
        "cp437",
        "utf16le",
        "utf16be",
        "utf8",
        "wchar>",
        "wchar<",
        "int16>",
        "int16<",
        "uint16>",
        "uint16<",
        "int24>",
        "int24<",
        "uint24>",
        "uint24<",
        "int32>",
        "int32<",
        "uint32>",
        "uint32<",
        "int48>",
        "int48<",
        "uint48>",
        "uint48<",
        "int128>",
        "int128<",
        "uint128>",
        "uint128<",
        "float16>",
        "float16<",
        "int64>",
        "int64<",
        "uint64>",
        "uint64<",
        "float32>",
        "float32<",
        "float64>",
        "float64<",
        "ascii_string_zero",
        "ascii_string_newline",
        "utf8_string_zero",
        "utf8_string_newline",
        "unicode_string_zero>",
        "unicode_string_zero<",
        "unicode_string_newline>",
        "unicode_string_newline<");

    private static readonly object CacheLock = new();
    private static readonly Dictionary<(bool LittleEndian, int CLongWidth), PrimitiveCatalog> Cache = new();

    private PrimitiveCatalog(
        bool littleEndian,
        int cLongWidth,
        ImmutableDictionary<string, int> codecIds,
        ImmutableDictionary<string, byte> alignments,
        ImmutableDictionary<string, CompiledTypeReference> symbols,
        ImmutableDictionary<string, string> aliases,
        BitfieldCodecTable bitfields,
        ImmutableArray<CustomCodecDescriptor> customCodecs)
    {
        this.LittleEndian = littleEndian;
        this.CLongWidth = cLongWidth;
        this.CodecIds = codecIds;
        this.Alignments = alignments;
        this.Symbols = symbols;
        this.Aliases = aliases;
        this.Bitfields = bitfields;
        this.CustomCodecs = customCodecs;
    }

    /// <summary>The layout byte order the neutral names resolve to.</summary>
    public bool LittleEndian { get; }

    /// <summary>The width the <c>long</c> family resolves to (32 or 64).</summary>
    public int CLongWidth { get; }

    /// <summary>Every readable name (canonical, neutral, alias, custom) to its codec id.</summary>
    public ImmutableDictionary<string, int> CodecIds { get; }

    /// <summary>Alignment in bytes per readable name (1 for variable-length codecs and for 3-, 6-, and 16-byte identifiers).</summary>
    public ImmutableDictionary<string, byte> Alignments { get; }

    /// <summary>The compiled type symbol per readable name, including pointer spellings and <c>void</c>.</summary>
    public ImmutableDictionary<string, CompiledTypeReference> Symbols { get; }

    /// <summary>Alias spelling to canonical name.</summary>
    public ImmutableDictionary<string, string> Aliases { get; }

    /// <summary>The bitfield storage types this catalog accepts.</summary>
    public BitfieldCodecTable Bitfields { get; }

    /// <summary>The custom codecs registered on top of the shared names (empty for the shared catalogs).</summary>
    public ImmutableArray<CustomCodecDescriptor> CustomCodecs { get; }

    /// <summary>The number of codec ids: canonical delegates plus this catalog's custom codecs.</summary>
    public int CodecCount => CanonicalNames.Length + this.CustomCodecs.Length;

    /// <summary>The shared catalog for one byte order and <c>long</c> width; built once per process.</summary>
    public static PrimitiveCatalog For(bool littleEndian, int cLongWidth)
    {
        (bool, int) key = (littleEndian, cLongWidth == 32 ? 32 : 64);
        lock (CacheLock)
        {
            if (!Cache.TryGetValue(key, out PrimitiveCatalog? catalog))
            {
                catalog = Build(key.Item1, key.Item2);
                Cache.Add(key, catalog);
            }

            return catalog;
        }
    }

    /// <summary>Whether <paramref name="name"/> is a primitive type this catalog can read (built-in or custom).</summary>
    public bool IsKnownName(string name)
    {
        return this.CodecIds.ContainsKey(name);
    }

    /// <summary>The codec id of a readable name, or <see cref="NoCodec"/>.</summary>
    public int CodecIdOf(string name)
    {
        return this.CodecIds.TryGetValue(name, out int id) ? id : NoCodec;
    }

    /// <summary>
    ///     Derives the catalog one layout uses when it registers custom codecs: each descriptor gets the next codec
    ///     id, a symbol, and an alignment entry. Names are validated the way the runtime validates
    ///     <c>ICustomCodec</c> registrations, so the generator reports the same errors.
    /// </summary>
    public PrimitiveCatalog WithCustomCodecs(IReadOnlyList<CustomCodecDescriptor> codecs)
    {
        if (codecs.Count == 0)
        {
            return this;
        }

        ImmutableDictionary<string, int>.Builder ids = this.CodecIds.ToBuilder();
        ImmutableDictionary<string, byte>.Builder alignments = this.Alignments.ToBuilder();
        ImmutableDictionary<string, CompiledTypeReference>.Builder symbols = this.Symbols.ToBuilder();
        ImmutableArray<CustomCodecDescriptor>.Builder custom = ImmutableArray.CreateBuilder<CustomCodecDescriptor>(codecs.Count);
        foreach (CustomCodecDescriptor codec in codecs)
        {
            string name = codec.Name;
            if (string.IsNullOrEmpty(name) || !(name[0] == '_' || char.IsLetter(name[0])) || !IsIdentifierTail(name))
            {
                throw new ArgumentException($"Custom codec name '{name}' is not an identifier.", nameof(codecs));
            }

            if (this.Symbols.ContainsKey(name) || symbols.ContainsKey(name))
            {
                throw new ArgumentException($"Custom codec name '{name}' is already a primitive type.", nameof(codecs));
            }

            if (codec.Alignment <= 0 || (codec.Alignment & (codec.Alignment - 1)) != 0 || codec.FixedSize is < 0)
            {
                throw new ArgumentException($"Custom codec '{name}' needs a power-of-two alignment and a non-negative size.", nameof(codecs));
            }

            int id = CanonicalNames.Length + custom.Count;
            custom.Add(codec);
            ids.Add(name, id);
            alignments[name] = (byte)Math.Min(codec.Alignment, byte.MaxValue);
            var symbol = new CompiledTypeSymbol(name, CompiledTypeKind.Primitive, null, codec.Alignment, codec.FixedSize, id, isCustomCodec: true);
            symbol.Bind(new CompiledPrimitiveType(symbol));
            symbol.Freeze();
            symbols.Add(name, new CompiledTypeReference(symbol, 0, name));
        }

        return new PrimitiveCatalog(this.LittleEndian, this.CLongWidth, ids.ToImmutable(), alignments.ToImmutable(), symbols.ToImmutable(), this.Aliases, this.Bitfields, custom.ToImmutable());
    }

    /// <summary>The alignment rule for one canonical descriptor: 1 for variable-length codecs and for the 3-, 6-, and 16-byte identifiers, otherwise the size.</summary>
    internal static byte AlignmentOf(PrimitiveCodec codec)
    {
        if (codec.Size == 0 || codec.Size is 3 or 6 || codec.IsIdentifier)
        {
            return 1;
        }

        return codec.Size;
    }

    private static bool IsIdentifierTail(string name)
    {
        foreach (char character in name)
        {
            if (character != '_' && !char.IsLetterOrDigit(character))
            {
                return false;
            }
        }

        return true;
    }

    private static PrimitiveCatalog Build(bool littleEndian, int cLongWidth)
    {
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        var alignments = new Dictionary<string, byte>(StringComparer.Ordinal);
        var sizes = new Dictionary<string, int?>(StringComparer.Ordinal);
        for (int index = 0; index < CanonicalNames.Length; index++)
        {
            string name = CanonicalNames[index];
            PrimitiveCodec codec = PrimitiveCodec.Resolve(name, littleEndian);
            ids.Add(name, index);
            alignments.Add(name, AlignmentOf(codec));
            sizes.Add(name, codec.Size == 0 ? null : codec.Size);
        }

        // A neutral name (`int16`) is the canonical name of the layout's byte order (`int16<` or `int16>`).
        foreach (string name in CanonicalNames)
        {
            if (name[name.Length - 1] != '>')
            {
                continue;
            }

            string neutral = name.Substring(0, name.Length - 1);
            string canonical = neutral + (littleEndian ? '<' : '>');
            ids.Add(neutral, ids[canonical]);
            alignments.Add(neutral, alignments[canonical]);
            sizes.Add(neutral, sizes[canonical]);
        }

        // Every canonical and neutral name gets its own symbol; aliases below share those symbols.
        ImmutableDictionary<string, CompiledTypeReference>.Builder symbols = ImmutableDictionary.CreateBuilder<string, CompiledTypeReference>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, int> entry in ids)
        {
            string name = entry.Key;
            var symbol = new CompiledTypeSymbol(name, CompiledTypeKind.Primitive, null, alignments[name], sizes[name], entry.Value);
            symbol.Bind(new CompiledPrimitiveType(symbol));
            symbol.Freeze();
            symbols.Add(name, new CompiledTypeReference(symbol, 0, name));
        }

        // `void` is a type with no value of its own: only `void *` (an opaque address) is storable, which the
        // compiler enforces; the symbol exists so the name resolves.
        var voidSymbol = new CompiledTypeSymbol("void", CompiledTypeKind.Primitive, null, 1, 0, NoCodec);
        voidSymbol.Bind(new CompiledPrimitiveType(voidSymbol));
        voidSymbol.Freeze();
        symbols.Add("void", new CompiledTypeReference(voidSymbol, 0, "void"));
        foreach (KeyValuePair<string, string> spelling in PrimitiveSpellings.PointerSpellings)
        {
            symbols.Add(spelling.Key, new CompiledTypeReference(symbols[spelling.Value].Symbol, 1, spelling.Value));
        }

        var aliases = new Dictionary<string, string>(PrimitiveSpellings.Aliases, StringComparer.Ordinal);
        foreach (KeyValuePair<string, bool> spelling in PrimitiveSpellings.LongFamilyIsUnsigned)
        {
            aliases.Add(spelling.Key, PrimitiveSpellings.LongCanonical(spelling.Value, cLongWidth));
        }

        foreach (KeyValuePair<string, string> alias in aliases)
        {
            symbols.Add(alias.Key, symbols[alias.Value]);
            if (ids.TryGetValue(alias.Value, out int id))
            {
                ids.Add(alias.Key, id);
                alignments.Add(alias.Key, alignments[alias.Value]);
            }
        }

        ImmutableDictionary<string, string> aliasMap = aliases.ToImmutableDictionary(StringComparer.Ordinal);
        ImmutableDictionary<string, byte> alignmentMap = alignments.ToImmutableDictionary(StringComparer.Ordinal);
        return new PrimitiveCatalog(
            littleEndian,
            cLongWidth,
            ids.ToImmutableDictionary(StringComparer.Ordinal),
            alignmentMap,
            symbols.ToImmutable(),
            aliasMap,
            new BitfieldCodecTable(littleEndian, alignmentMap, aliasMap),
            ImmutableArray<CustomCodecDescriptor>.Empty);
    }
}
