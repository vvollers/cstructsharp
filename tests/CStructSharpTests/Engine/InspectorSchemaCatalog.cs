namespace CStructSharp.Tests;

using System.Globalization;
using System.Text;

/// <summary>
///     Reads the inspector's schema catalog (<c>apps/inspector/src/schema-catalog.ts</c>) at test time: the detection
///     schema of every registered file extension, composed as the app's <c>schemaForFile</c> composes it, and every
///     teaching sample with its bytes and parser options.
/// </summary>
/// <remarks>
///     The catalog is TypeScript, but its registrations are plain object literals: string and template literals
///     (whose <c>${name}</c> substitutions name top-level string constants), numbers, booleans, identifiers, arrays,
///     objects with <c>...spread</c>, and comments. <see cref="Parse"/> evaluates exactly that subset and fails loudly on
///     anything else, so a catalog change that this reader cannot follow breaks the corpus test rather than silently
///     shrinking it. The app's <c>formatLayout</c> only changes whitespace, so it is not reproduced.
/// </remarks>
internal static class InspectorSchemaCatalog
{
    /// <summary>
    ///     Loads the catalog: one entry per detection schema (named <c>detected-EXT</c>) and one per sample (named by its
    ///     id), each with its definition, root, parser options, and bytes (a sample's own; for a detection schema the
    ///     first sample registered with that format, or none).
    /// </summary>
    /// <param name="path">The catalog file.</param>
    /// <returns>The entries, in catalog order.</returns>
    /// <exception cref="FormatException">The catalog uses syntax outside the subset this reader evaluates.</exception>
    public static IReadOnlyList<Entry> Load(string path)
    {
        string source = File.ReadAllText(path);
        Dictionary<string, object?> constants = Parse(source);
        var formats = (List<object?>)(constants.GetValueOrDefault("formatDefinitions") ?? throw new FormatException("formatDefinitions not found in " + path));
        var entries = new List<Entry>();
        foreach (Dictionary<string, object?> format in formats.Cast<Dictionary<string, object?>>())
        {
            var extensions = ((List<object?>)format["extensions"]!).Cast<string>().ToList();
            var samples = ((List<object?>?)format.GetValueOrDefault("samples") ?? []).Cast<Dictionary<string, object?>>().ToList();
            byte[]? sampleBytes = samples.Count > 0 ? Hex((string)samples[0]["binaryHex"]!) : null;
            foreach (string extension in extensions)
            {
                // Mirrors schemaForFile: header comments, the format's types, the fields in file_EXT, and a root.
                string name = "file_" + string.Concat(extension.Select(character => char.IsAsciiLetterOrDigit(character) || character == '_' ? character : '_'));
                string definition = "// " + extension.ToUpperInvariant() + " - " + format["family"] + "\n// " + format["scope"] + "\n" + (string?)format.GetValueOrDefault("types") +
                                    "\nstruct " + name + " { " + format["fields"] + " };\nstruct root { " + name + " header; };";
                entries.Add(new Entry(
                    "detected-" + extension,
                    definition,
                    "root",
                    false,
                    (bool?)format.GetValueOrDefault("littleEndian") ?? true,
                    (byte)(double)(format.GetValueOrDefault("pointerSize") ?? 4.0),
                    PointerAddressingMode.Absolute,
                    sampleBytes));
            }

            foreach (Dictionary<string, object?> sample in samples)
            {
                var options = (Dictionary<string, object?>)sample["parserOptions"]!;
                entries.Add(new Entry(
                    (string)sample["id"]!,
                    (string)sample["definition"]!,
                    (string)sample["rootType"]!,
                    (bool)options["aligned"]!,
                    (bool)options["littleEndian"]!,
                    (byte)(double)options["pointerSize"]!,
                    Enum.Parse<PointerAddressingMode>((string?)options.GetValueOrDefault("addressingMode") ?? "Absolute"),
                    Hex((string)sample["binaryHex"]!)));
            }
        }

        return entries;
    }

    /// <summary>
    ///     Evaluates every top-level <c>const NAME = value;</c> (with an optional type annotation) whose value is in the
    ///     literal subset, in order, so later constants can use earlier ones; other statements are skipped.
    /// </summary>
    /// <param name="source">The TypeScript text.</param>
    /// <returns>The constants by name.</returns>
    /// <exception cref="FormatException">A literal constant uses syntax outside the subset.</exception>
    public static Dictionary<string, object?> Parse(string source)
    {
        var constants = new Dictionary<string, object?>(StringComparer.Ordinal);
        int index = 0;
        while ((index = source.IndexOf("\nconst ", index, StringComparison.Ordinal)) >= 0)
        {
            var reader = new Reader(source, index + "\nconst ".Length, constants);
            string name = reader.Identifier();
            if (reader.TrySkipAnnotation() && reader.Peek() == '=')
            {
                reader.Expect('=');
                if (reader.StartsLiteral())
                {
                    constants[name] = reader.Value();
                }
            }

            index = reader.Position;
        }

        return constants;
    }

    /// <summary>Decodes space-separated hexadecimal byte text.</summary>
    /// <param name="text">The text, such as <c>42 4d 36</c>.</param>
    /// <returns>The bytes.</returns>
    private static byte[] Hex(string text) => Convert.FromHexString(string.Concat(text.Split((char[])[' ', '\n', '\r', '\t'], StringSplitOptions.RemoveEmptyEntries)));

    /// <summary>One detection schema or teaching sample of the catalog.</summary>
    /// <param name="Id">The schema's name: <c>detected-EXT</c> or the sample's id.</param>
    /// <param name="Definition">The layout text.</param>
    /// <param name="Root">The root struct.</param>
    /// <param name="Aligned">Whether the parser aligns.</param>
    /// <param name="LittleEndian">Whether neutral values are little-endian.</param>
    /// <param name="PointerSize">The pointer width in bytes.</param>
    /// <param name="AddressingMode">How pointer values address the input.</param>
    /// <param name="Bytes">The sample bytes, or <see langword="null"/> when the format registers no sample.</param>
    internal sealed record Entry(string Id, string Definition, string Root, bool Aligned, bool LittleEndian, byte PointerSize, PointerAddressingMode AddressingMode, byte[]? Bytes);

    /// <summary>A cursor over the TypeScript text that evaluates the literal subset.</summary>
    private sealed class Reader
    {
        /// <summary>The text.</summary>
        private readonly string text;

        /// <summary>The constants evaluated so far, for identifiers, spreads, and substitutions.</summary>
        private readonly Dictionary<string, object?> constants;

        /// <summary>Creates a reader.</summary>
        /// <param name="text">The text.</param>
        /// <param name="position">The index to start at.</param>
        /// <param name="constants">The constants evaluated so far.</param>
        public Reader(string text, int position, Dictionary<string, object?> constants)
        {
            this.text = text;
            this.Position = position;
            this.constants = constants;
        }

        /// <summary>Gets the index of the next unread character.</summary>
        public int Position { get; private set; }

        /// <summary>Returns the next character after whitespace and comments, without consuming it.</summary>
        /// <returns>The character, or <c>\0</c> at the end.</returns>
        public char Peek()
        {
            this.SkipTrivia();
            return this.Position < this.text.Length ? this.text[this.Position] : '\0';
        }

        /// <summary>Consumes <paramref name="expected"/>.</summary>
        /// <param name="expected">The character.</param>
        /// <exception cref="FormatException">The next character differs.</exception>
        public void Expect(char expected)
        {
            if (this.Peek() != expected)
            {
                throw this.Error("expected '" + expected + "'");
            }

            this.Position++;
        }

        /// <summary>Consumes an identifier.</summary>
        /// <returns>The identifier.</returns>
        /// <exception cref="FormatException">No identifier follows.</exception>
        public string Identifier()
        {
            this.SkipTrivia();
            int start = this.Position;
            while (this.Position < this.text.Length && (char.IsAsciiLetterOrDigit(this.text[this.Position]) || this.text[this.Position] is '_' or '$'))
            {
                this.Position++;
            }

            return start == this.Position ? throw this.Error("expected an identifier") : this.text[start..this.Position];
        }

        /// <summary>Skips a <c>: Type</c> annotation up to the <c>=</c>; returns <see langword="false"/> when the statement has none of either.</summary>
        /// <returns>Whether an <c>=</c> may follow.</returns>
        public bool TrySkipAnnotation()
        {
            if (this.Peek() != ':')
            {
                return true;
            }

            // A type annotation of a constant never contains '=', so the value starts after the first one.
            int equals = this.text.IndexOf('=', this.Position);
            int end = this.text.IndexOf(';', this.Position);
            if (equals < 0 || (end >= 0 && end < equals))
            {
                return false;
            }

            this.Position = equals;
            return true;
        }

        /// <summary>Returns whether the next value is in the literal subset: a string, template, number, array, or object.</summary>
        /// <returns><see langword="true"/> for a literal.</returns>
        public bool StartsLiteral() => this.Peek() is '"' or '\'' or '`' or '[' or '{' or '-' || char.IsAsciiDigit(this.Peek());

        /// <summary>Consumes one value.</summary>
        /// <returns>A string, <see cref="double"/>, <see cref="bool"/>, list, dictionary, or <see langword="null"/>.</returns>
        /// <exception cref="FormatException">The value is outside the subset.</exception>
        public object? Value()
        {
            char next = this.Peek();
            switch (next)
            {
            case '"' or '\'':
                return this.QuotedString(next);
            case '`':
                return this.Template();
            case '[':
                return this.Array();
            case '{':
                return this.Object();
            }

            if (next == '-' || char.IsAsciiDigit(next))
            {
                int start = this.Position;
                this.Position++;
                while (this.Position < this.text.Length && (char.IsAsciiDigit(this.text[this.Position]) || this.text[this.Position] == '.'))
                {
                    this.Position++;
                }

                return double.Parse(this.text[start..this.Position], CultureInfo.InvariantCulture);
            }

            string identifier = this.Identifier();
            return identifier switch
            {
                "true" => true,
                "false" => false,
                "null" or "undefined" => null,
                _ when this.constants.TryGetValue(identifier, out object? value) => value,
                _ => throw this.Error("unknown identifier '" + identifier + "'"),
            };
        }

        /// <summary>Skips whitespace and line and block comments.</summary>
        private void SkipTrivia()
        {
            while (this.Position < this.text.Length)
            {
                if (char.IsWhiteSpace(this.text[this.Position]))
                {
                    this.Position++;
                }
                else if (string.CompareOrdinal(this.text, this.Position, "//", 0, 2) == 0)
                {
                    int end = this.text.IndexOf('\n', this.Position);
                    this.Position = end < 0 ? this.text.Length : end + 1;
                }
                else if (string.CompareOrdinal(this.text, this.Position, "/*", 0, 2) == 0)
                {
                    int end = this.text.IndexOf("*/", this.Position + 2, StringComparison.Ordinal);
                    this.Position = end < 0 ? this.text.Length : end + 2;
                }
                else
                {
                    return;
                }
            }
        }

        /// <summary>Consumes a quoted string with JavaScript escapes.</summary>
        /// <param name="quote">The opening quote.</param>
        /// <returns>The string's value.</returns>
        private string QuotedString(char quote)
        {
            this.Position++;
            var value = new StringBuilder();
            while (this.text[this.Position] != quote)
            {
                value.Append(this.text[this.Position] == '\\' ? this.Escape() : this.text[this.Position++]);
            }

            this.Position++;
            return value.ToString();
        }

        /// <summary>Consumes a template literal, substituting each <c>${name}</c> with a string constant.</summary>
        /// <returns>The template's value.</returns>
        private string Template()
        {
            this.Position++;
            var value = new StringBuilder();
            while (this.text[this.Position] != '`')
            {
                if (string.CompareOrdinal(this.text, this.Position, "${", 0, 2) == 0)
                {
                    this.Position += 2;
                    string name = this.Identifier();
                    this.Expect('}');
                    value.Append(this.constants.TryGetValue(name, out object? constant) && constant is string text ? text : throw this.Error("unknown substitution '" + name + "'"));
                }
                else
                {
                    value.Append(this.text[this.Position] == '\\' ? this.Escape() : this.text[this.Position++]);
                }
            }

            this.Position++;
            return value.ToString();
        }

        /// <summary>Consumes an escape sequence starting at a backslash.</summary>
        /// <returns>The escaped text.</returns>
        private string Escape()
        {
            char code = this.text[this.Position + 1];
            this.Position += 2;
            switch (code)
            {
            case 'n':
                return "\n";
            case 'r':
                return "\r";
            case 't':
                return "\t";
            case '0':
                return "\0";
            case 'u':
                string hex = this.text.Substring(this.Position, 4);
                this.Position += 4;
                return ((char)int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture)).ToString();
            default:
                return code.ToString();
            }
        }

        /// <summary>Consumes an array literal; trailing commas are allowed.</summary>
        /// <returns>The elements.</returns>
        private List<object?> Array()
        {
            this.Expect('[');
            var items = new List<object?>();
            while (this.Peek() != ']')
            {
                items.Add(this.Value());
                if (this.Peek() == ',')
                {
                    this.Position++;
                }
            }

            this.Position++;
            return items;
        }

        /// <summary>Consumes an object literal with identifier or quoted keys and <c>...name</c> spreads; trailing commas are allowed.</summary>
        /// <returns>The members, in order, later members replacing earlier ones.</returns>
        private Dictionary<string, object?> Object()
        {
            this.Expect('{');
            var members = new Dictionary<string, object?>(StringComparer.Ordinal);
            while (this.Peek() != '}')
            {
                if (string.CompareOrdinal(this.text, this.Position, "...", 0, 3) == 0)
                {
                    this.Position += 3;
                    string name = this.Identifier();
                    foreach (KeyValuePair<string, object?> member in this.constants.TryGetValue(name, out object? spread) && spread is Dictionary<string, object?> source ? source : throw this.Error("unknown spread '" + name + "'"))
                    {
                        members[member.Key] = member.Value;
                    }
                }
                else
                {
                    string key = this.Peek() is '"' or '\'' ? this.QuotedString(this.text[this.Position]) : this.Identifier();
                    this.Expect(':');
                    members[key] = this.Value();
                }

                if (this.Peek() == ',')
                {
                    this.Position++;
                }
            }

            this.Position++;
            return members;
        }

        /// <summary>Creates a parse failure that names the line.</summary>
        /// <param name="problem">What went wrong.</param>
        /// <returns>The failure.</returns>
        private FormatException Error(string problem)
        {
            int line = 1 + this.text.AsSpan(0, Math.Min(this.Position, this.text.Length)).Count('\n');
            return new FormatException("schema-catalog.ts line " + line + ": " + problem);
        }
    }
}
