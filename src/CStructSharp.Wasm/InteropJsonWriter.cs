namespace CStructSharpWeb.Wasm;

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using CStructSharp;
using CStructSharp.Syntax;
using CStructSharp.Values;

/// <summary>
///     The browser bridge's one JSON writer: every envelope, parsed value, error, debug range, and static read plan
///     the exports return is written by it as UTF-8 straight into a byte buffer. The value set is closed - the
///     core parser produces only <see cref="StructValue"/>, <see cref="PrimitiveArray{T}"/>, lists, boxed
///     numbers, strings, enums, unions and pointers - so this writer needs no state machine, no name validation
///     and no per-write escaping decisions, the work that makes <see cref="System.Text.Json.Utf8JsonWriter"/> cost
///     ≈ 50 ns per output byte under the WebAssembly interpreter. Output is byte-identical to a
///     <c>Utf8JsonWriter</c> projection of the same values (same number formatting, same JavaScript-safe integer
///     rule, same <c>JavaScriptEncoder.Default</c> escaping).
/// </summary>
/// <remarks>
///     Callers compose objects from <see cref="WriteRawBytes"/> for fixed punctuation and property names (which
///     must already be valid JSON) and the typed methods for values; strings are always escaped by
///     <see cref="WriteString"/>, the bridge's only escaping implementation.
/// </remarks>
internal sealed class InteropJsonWriter
{
    private static readonly byte[] NullBytes = "null"u8.ToArray();
    private static readonly byte[] TrueBytes = "true"u8.ToArray();
    private static readonly byte[] FalseBytes = "false"u8.ToArray();
    private static readonly byte[] UnionHead = "{\"kind\":\"union\",\"union\":"u8.ToArray();
    private static readonly byte[] RawStorageName = ",\"rawStorage\":"u8.ToArray();
    private static readonly byte[] MembersName = ",\"members\":{"u8.ToArray();
    private static readonly byte[] SelectedMemberName = "},\"selectedMember\":"u8.ToArray();
    private static readonly byte[] PointerHead = "{\"kind\":\"pointer\",\"address\":"u8.ToArray();
    private static readonly byte[] PointerDepth = ",\"depth\":"u8.ToArray();
    private static readonly byte[] PointerIsDereferenced = ",\"dereferenced\":"u8.ToArray();
    private static readonly byte[] PointerValue = ",\"value\":"u8.ToArray();
    private static readonly byte[] EnumHead = "{\"kind\":\"enum\",\"enum\":"u8.ToArray();
    private static readonly byte[] EnumName = ",\"name\":"u8.ToArray();
    private static readonly byte[] EnumValue = ",\"value\":"u8.ToArray();
    private static readonly byte[] FlagNames = ",\"names\":"u8.ToArray();
    private static readonly byte[] FlagRemainder = ",\"remainder\":"u8.ToArray();
    private static readonly byte[] HexDigits = "0123456789abcdef"u8.ToArray();
    private static readonly SearchValues<byte> UnescapedUtf8 = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 !#$%()*,-./:;=?@[]^_`{|}~"u8);

    private byte[] buffer;
    private int length;

    /// <summary>Creates an empty writer whose buffer starts at the given size and doubles as output grows.</summary>
    /// <param name="capacity">The initial buffer size in bytes.</param>
    public InteropJsonWriter(int capacity)
    {
        this.buffer = new byte[capacity];
    }

    /// <summary>The bytes written so far.</summary>
    public ReadOnlySpan<byte> WrittenSpan => this.buffer.AsSpan(0, this.length);

    /// <summary>Gets the current buffer size in bytes, which callers use to drop an unusually large writer.</summary>
    public int Capacity => this.buffer.Length;

    /// <summary>Discards the written bytes so the writer can be reused; the buffer and its capacity are kept.</summary>
    public void Reset()
    {
        this.length = 0;
    }

    /// <summary>Appends bytes that are already valid JSON (envelope framing, source-generated fragments).</summary>
    /// <param name="bytes">The UTF-8 JSON bytes, copied without validation or escaping.</param>
    public void WriteRawBytes(ReadOnlySpan<byte> bytes)
    {
        this.Ensure(bytes.Length);
        bytes.CopyTo(this.buffer.AsSpan(this.length));
        this.length += bytes.Length;
    }

    /// <summary>Writes JSON <c>null</c>.</summary>
    public void WriteNull()
    {
        this.WriteRaw(NullBytes);
    }

    /// <summary>Writes JSON <c>true</c> or <c>false</c>.</summary>
    /// <param name="value">The value to write.</param>
    public void WriteBoolean(bool value)
    {
        this.WriteRaw(value ? TrueBytes : FalseBytes);
    }

    /// <summary>Writes a string as an escaped JSON string, or JSON <c>null</c> when it is <see langword="null"/>.</summary>
    /// <param name="value">The text to write.</param>
    public void WriteStringOrNull(string? value)
    {
        if (value is null)
        {
            this.WriteRaw(NullBytes);
        }
        else
        {
            this.WriteString(value);
        }
    }

    /// <summary>
    ///     Writes an integer as a JSON number, or JSON <c>null</c> when it is <see langword="null"/>. The caller
    ///     guarantees the value lies within JavaScript's exact integer range (offsets, lengths, line numbers).
    /// </summary>
    /// <param name="value">The integer to write.</param>
    public void WriteIntegerOrNull(long? value)
    {
        if (value is null)
        {
            this.WriteRaw(NullBytes);
        }
        else
        {
            this.WriteNumber(value.Value);
        }
    }

    /// <summary>Writes one parsed value (the top-level or any nested one).</summary>
    /// <param name="value">
    ///     A value the core parser produces, or <see langword="null"/>, which is written as JSON <c>null</c>.
    /// </param>
    public void WriteValue(object? value)
    {
        switch (value)
        {
        case null:
            this.WriteRaw(NullBytes);
            return;
        case string text:
            this.WriteString(text);
            return;
        case bool flag:
            this.WriteRaw(flag ? TrueBytes : FalseBytes);
            return;
        case StructValue structValue:
            this.WriteStruct(structValue);
            return;
        case byte number:
            this.WriteNumber(number);
            return;
        case sbyte number:
            this.WriteNumber(number);
            return;
        case short number:
            this.WriteNumber(number);
            return;
        case ushort number:
            this.WriteNumber(number);
            return;
        case int number:
            this.WriteNumber(number);
            return;
        case uint number:
            this.WriteNumber(number);
            return;
        case long number:
            this.WriteSafeInteger(number);
            return;
        case ulong number:
            this.WriteSafeInteger(number);
            return;
        case float number:
            this.WriteNumber(number);
            return;
        case double number:
            this.WriteNumber(number);
            return;
        case decimal number:
            this.WriteNumber(number);
            return;
        case BigInteger number:
            this.WriteSafeInteger(number);
            return;
        case Int128 wide:
            this.WriteSafeInteger((BigInteger)wide);
            return;
        case UInt128 wide:
            this.WriteSafeInteger((BigInteger)wide);
            return;
        case Half half:
            this.WriteNumber((double)half);
            return;
        case Guid identifier:
            this.WriteString(identifier.ToString("D"));
            return;
        case byte[] bytes:
            this.WriteBase64(bytes);
            return;
        case EnumValueResult enumValue:
            this.WriteRaw(EnumHead);
            this.WriteString(enumValue.Enum);
            this.WriteRaw(EnumName);
            this.WriteValue(enumValue.Name);
            this.WriteRaw(EnumValue);
            this.WriteSafeInteger(enumValue.Value);
            if (enumValue is FlagValueResult flagValue)
            {
                // A flag adds its decomposition; the three enum keys stay exactly as they are.
                this.WriteRaw(FlagNames);
                this.WriteByte((byte)'[');
                for (int index = 0; index < flagValue.Names.Length; index++)
                {
                    if (index > 0)
                    {
                        this.WriteByte((byte)',');
                    }

                    this.WriteString(flagValue.Names[index]);
                }

                this.WriteByte((byte)']');
                this.WriteRaw(FlagRemainder);
                this.WriteSafeInteger(flagValue.Remainder);
            }

            this.WriteByte((byte)'}');
            return;
        case UnionValue unionValue:
            this.WriteUnion(unionValue);
            return;
        case Pointer pointer:
            this.WriteRaw(PointerHead);
            this.WriteSafeInteger(pointer.Address);
            this.WriteRaw(PointerDepth);
            this.WriteNumber(pointer.Depth);
            this.WriteRaw(PointerIsDereferenced);
            this.WriteRaw(pointer.IsDereferenced ? TrueBytes : FalseBytes);
            this.WriteRaw(PointerValue);
            this.WriteValue(pointer.Value);
            this.WriteByte((byte)'}');
            return;
        case PrimitiveArray<byte> array:
            this.WriteNumbers(array.Span);
            return;
        case PrimitiveArray<sbyte> array:
            this.WriteNumbers(array.Span);
            return;
        case PrimitiveArray<bool> array:
            this.WriteBooleans(array.Span);
            return;
        case PrimitiveArray<short> array:
            this.WriteNumbers(array.Span);
            return;
        case PrimitiveArray<ushort> array:
            this.WriteNumbers(array.Span);
            return;
        case PrimitiveArray<int> array:
            this.WriteNumbers(array.Span);
            return;
        case PrimitiveArray<uint> array:
            this.WriteNumbers(array.Span);
            return;
        case PrimitiveArray<long> array:
            this.WriteSafeIntegers(array.Span);
            return;
        case PrimitiveArray<ulong> array:
            this.WriteSafeIntegers(array.Span);
            return;
        case PrimitiveArray<float> array:
            this.WriteNumbers(array.Span);
            return;
        case PrimitiveArray<double> array:
            this.WriteNumbers(array.Span);
            return;
        case IDictionary<string, object?> dictionary:
            this.WriteByte((byte)'{');
            bool firstMember = true;
            foreach (KeyValuePair<string, object?> member in dictionary)
            {
                this.WriteMemberSeparator(ref firstMember);
                this.WriteString(member.Key);
                this.WriteByte((byte)':');
                this.WriteValue(member.Value);
            }

            this.WriteByte((byte)'}');
            return;
        case IEnumerable<object?> sequence:
            this.WriteByte((byte)'[');
            bool firstItem = true;
            foreach (object? item in sequence)
            {
                this.WriteMemberSeparator(ref firstItem);
                this.WriteValue(item);
            }

            this.WriteByte((byte)']');
            return;
        case char character:
            this.WriteString(character.ToString());
            return;
        default:
            // Any other value (for example a scalar produced by a typed alias) renders as its invariant text.
            this.WriteString(value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? string.Empty);
            return;
        }
    }

    /// <summary>Whether an ASCII character passes through <c>JavaScriptEncoder.Default</c> unescaped.</summary>
    /// <param name="character">A character below U+007F.</param>
    /// <returns>True for letters, digits, space, and the encoder's allowed punctuation.</returns>
    private static bool IsUnescapedAscii(char character)
    {
        // JavaScriptEncoder.Default's allowed set: letters, digits, and  !#$%()*,-./:;=?@[]^_`{|}~ and space.
        if (character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
        {
            return true;
        }

        return character is ' ' or '!' or '#' or '$' or '%' or '(' or ')' or '*' or ',' or '-' or '.' or '/' or ':' or ';' or '=' or '?' or '@' or '[' or ']' or '^' or '_' or '`' or '{' or '|' or '}' or '~';
    }

    /// <summary>Writes a parsed struct as a JSON object of its members, in declaration order.</summary>
    /// <param name="value">The struct.</param>
    private void WriteStruct(StructValue value)
    {
        this.WriteByte((byte)'{');
        bool first = true;
        foreach (KeyValuePair<string, object?> member in value)
        {
            this.WriteMemberSeparator(ref first);
            this.WritePropertyName(member.Key);
            this.WriteValue(member.Value);
        }

        this.WriteByte((byte)'}');
    }

    /// <summary>
    ///     Writes a parsed union as its tagged shape: <c>kind</c>, <c>union</c>, <c>rawStorage</c> (Base64 or null),
    ///     every decoded member view, and <c>selectedMember</c>.
    /// </summary>
    /// <param name="value">The union.</param>
    private void WriteUnion(UnionValue value)
    {
        this.WriteRaw(UnionHead);
        this.WriteString(value.UnionName);
        this.WriteRaw(RawStorageName);
        if (value.HasRawStorage)
        {
            this.WriteBase64(value.RawStorage!.Value.Span);
        }
        else
        {
            this.WriteRaw(NullBytes);
        }

        this.WriteRaw(MembersName);
        bool first = true;
        foreach (KeyValuePair<string, object?> member in value.Members)
        {
            this.WriteMemberSeparator(ref first);
            this.WritePropertyName(member.Key);
            this.WriteValue(member.Value);
        }

        this.WriteRaw(SelectedMemberName);
        this.WriteValue(value.SelectedMember);
        this.WriteByte((byte)'}');
    }

    /// <summary>Field names are identifiers (ASCII letters, digits, underscore); anything else takes the escaping path.</summary>
    private void WritePropertyName(string name)
    {
        this.Ensure(name.Length + 3);
        byte[] target = this.buffer;
        int position = this.length;
        target[position++] = (byte)'"';
        for (int index = 0; index < name.Length; index++)
        {
            char character = name[index];
            if (character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_')
            {
                target[position++] = (byte)character;
            }
            else
            {
                this.WriteString(name);
                this.WriteByte((byte)':');
                return;
            }
        }

        target[position++] = (byte)'"';
        target[position++] = (byte)':';
        this.length = position;
    }

    /// <summary>Writes the comma before every member or item except the first.</summary>
    /// <param name="first">True before the first member; cleared by the call.</param>
    private void WriteMemberSeparator(ref bool first)
    {
        if (first)
        {
            first = false;
        }
        else
        {
            this.WriteByte((byte)',');
        }
    }

    /// <summary>Writes a primitive array of 8- to 32-bit integers or floats as a JSON array of numbers.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="values">The elements.</param>
    private void WriteNumbers<T>(ReadOnlySpan<T> values)
        where T : struct
    {
        this.WriteByte((byte)'[');
        for (int index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                this.WriteByte((byte)',');
            }

            this.WriteFormatted(values[index]);
        }

        this.WriteByte((byte)']');
    }

    /// <summary>Writes a 64-bit integer array, each element under the JavaScript-safe integer rule.</summary>
    /// <typeparam name="T"><see cref="long"/> or <see cref="ulong"/>.</typeparam>
    /// <param name="values">The elements.</param>
    private void WriteSafeIntegers<T>(ReadOnlySpan<T> values)
        where T : struct
    {
        this.WriteByte((byte)'[');
        for (int index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                this.WriteByte((byte)',');
            }

            if (typeof(T) == typeof(long))
            {
                this.WriteSafeInteger((long)(object)values[index]);
            }
            else
            {
                this.WriteSafeInteger((ulong)(object)values[index]);
            }
        }

        this.WriteByte((byte)']');
    }

    /// <summary>Writes a boolean array as a JSON array of <c>true</c> and <c>false</c>.</summary>
    /// <param name="values">The elements.</param>
    private void WriteBooleans(ReadOnlySpan<bool> values)
    {
        this.WriteByte((byte)'[');
        for (int index = 0; index < values.Length; index++)
        {
            if (index > 0)
            {
                this.WriteByte((byte)',');
            }

            this.WriteRaw(values[index] ? TrueBytes : FalseBytes);
        }

        this.WriteByte((byte)']');
    }

    /// <summary>Writes one element of <see cref="WriteNumbers{T}"/> with the formatter of its exact type.</summary>
    /// <typeparam name="T">The element type.</typeparam>
    /// <param name="value">The element.</param>
    private void WriteFormatted<T>(T value)
        where T : struct
    {
        if (typeof(T) == typeof(byte))
        {
            this.WriteNumber((byte)(object)value);
        }
        else if (typeof(T) == typeof(sbyte))
        {
            this.WriteNumber((sbyte)(object)value);
        }
        else if (typeof(T) == typeof(short))
        {
            this.WriteNumber((short)(object)value);
        }
        else if (typeof(T) == typeof(ushort))
        {
            this.WriteNumber((ushort)(object)value);
        }
        else if (typeof(T) == typeof(int))
        {
            this.WriteNumber((int)(object)value);
        }
        else if (typeof(T) == typeof(uint))
        {
            this.WriteNumber((uint)(object)value);
        }
        else if (typeof(T) == typeof(float))
        {
            this.WriteNumber((float)(object)value);
        }
        else
        {
            this.WriteNumber((double)(object)value);
        }
    }

    /// <summary>Writes a signed integer as a JSON number; the caller has applied any safe-integer rule.</summary>
    /// <param name="value">The integer.</param>
    private void WriteNumber(long value)
    {
        this.Ensure(20);
        Utf8Formatter.TryFormat(value, this.buffer.AsSpan(this.length), out int written);
        this.length += written;
    }

    /// <summary>Writes an unsigned integer as a JSON number; the caller has applied any safe-integer rule.</summary>
    /// <param name="value">The integer.</param>
    private void WriteNumber(ulong value)
    {
        this.Ensure(20);
        Utf8Formatter.TryFormat(value, this.buffer.AsSpan(this.length), out int written);
        this.length += written;
    }

    /// <summary>
    ///     JSON has no NaN or infinity, so a non-finite float is projected as the strings <c>"NaN"</c>,
    ///     <c>"Infinity"</c>, or <c>"-Infinity"</c> - the same convention the projection already uses for integers
    ///     that do not fit a JavaScript number, and the text the write path accepts back for a float field.
    /// </summary>
    private void WriteNumber(float value)
    {
        if (!float.IsFinite(value))
        {
            this.WriteNonFinite(float.IsNaN(value), value > 0);
            return;
        }

        this.Ensure(32);
        Utf8Formatter.TryFormat(value, this.buffer.AsSpan(this.length), out int written);
        this.length += written;
    }

    /// <summary>Writes a double as its shortest round-trip JSON number, or a non-finite value as its string name.</summary>
    /// <param name="value">The number.</param>
    private void WriteNumber(double value)
    {
        if (!double.IsFinite(value))
        {
            this.WriteNonFinite(double.IsNaN(value), value > 0);
            return;
        }

        this.Ensure(32);
        Utf8Formatter.TryFormat(value, this.buffer.AsSpan(this.length), out int written);
        this.length += written;
    }

    /// <summary>Writes the string name of a non-finite float: <c>"NaN"</c>, <c>"Infinity"</c>, or <c>"-Infinity"</c>.</summary>
    /// <param name="isNaN">Whether the value is NaN.</param>
    /// <param name="isPositive">Whether an infinite value is positive.</param>
    private void WriteNonFinite(bool isNaN, bool isPositive)
    {
        this.WriteString(isNaN ? "NaN" : isPositive ? "Infinity" : "-Infinity");
    }

    /// <summary>Writes a decimal as a JSON number with its exact digits.</summary>
    /// <param name="value">The number.</param>
    private void WriteNumber(decimal value)
    {
        this.Ensure(40);
        Utf8Formatter.TryFormat(value, this.buffer.AsSpan(this.length), out int written);
        this.length += written;
    }

    /// <summary>
    ///     Writes an integer as a JSON number when it lies within ±<see cref="InteropLimits.MaximumSafeInteger"/>, and
    ///     as a decimal string beyond JavaScript's exact range.
    /// </summary>
    /// <param name="value">The integer to write.</param>
    public void WriteSafeInteger(long value)
    {
        if (value is >= -InteropLimits.MaximumSafeInteger and <= InteropLimits.MaximumSafeInteger)
        {
            this.WriteNumber(value);
        }
        else
        {
            this.WriteString(value.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Writes an unsigned integer as a JSON number up to 2^53 - 1 and as a decimal string beyond.</summary>
    /// <param name="value">The integer.</param>
    private void WriteSafeInteger(ulong value)
    {
        if (value <= InteropLimits.MaximumSafeInteger)
        {
            this.WriteNumber(value);
        }
        else
        {
            this.WriteString(value.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Writes an integer of any width with the same JavaScript-safe rule as <see cref="WriteSafeInteger(long)"/>.</summary>
    /// <param name="value">The integer to write.</param>
    public void WriteSafeInteger(BigInteger value)
    {
        if (value >= -InteropLimits.MaximumSafeInteger && value <= InteropLimits.MaximumSafeInteger)
        {
            this.WriteNumber((long)value);
        }
        else
        {
            this.WriteString(value.ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Writes bytes as a JSON string of standard Base64 text.</summary>
    /// <param name="bytes">The bytes.</param>
    private void WriteBase64(ReadOnlySpan<byte> bytes)
    {
        int encodedLength = Base64.GetMaxEncodedToUtf8Length(bytes.Length);
        this.Ensure(encodedLength + 2);
        this.buffer[this.length++] = (byte)'"';
        Base64.EncodeToUtf8(bytes, this.buffer.AsSpan(this.length), out _, out int written);
        this.length += written;
        this.buffer[this.length++] = (byte)'"';
    }

    /// <summary>
    ///     Writes text as a JSON string, escaping like <c>JavaScriptEncoder.Default</c>: ASCII letters, digits and a
    ///     small punctuation set pass through; quotes, backslashes, control characters, HTML-sensitive characters and
    ///     everything non-ASCII are written as escapes. This is the bridge's only string-escaping implementation.
    /// </summary>
    /// <param name="text">The text to write.</param>
    public void WriteString(string text)
    {
        // Transcode once (the runtime's vectorized UTF-8 encoder), then only the bytes that need escaping are
        // rewritten: a byte-level IndexOfAnyExcept over the safe set finds them, and text without any (the common
        // case for identifiers and char[] buffers) costs one copy.
        this.Ensure((text.Length * 3) + 2);
        this.buffer[this.length++] = (byte)'"';
        int encoded = Encoding.UTF8.GetBytes(text, this.buffer.AsSpan(this.length));
        ReadOnlySpan<byte> utf8 = this.buffer.AsSpan(this.length, encoded);
        int firstUnsafe = utf8.IndexOfAnyExcept(UnescapedUtf8);
        if (firstUnsafe < 0)
        {
            this.length += encoded;
            this.buffer[this.length++] = (byte)'"';
            return;
        }

        // Escape from the first unsafe character on, character by character, into a fresh region past the
        // transcoded bytes (which are then discarded).
        int prefixLength = firstUnsafe;
        int suffixStart = this.length + prefixLength;
        this.length += prefixLength;
        string tail = Encoding.UTF8.GetString(this.buffer.AsSpan(suffixStart, encoded - prefixLength));
        this.Ensure((tail.Length * 6) + 1);
        byte[] target = this.buffer;
        int position = this.length;
        for (int index = 0; index < tail.Length; index++)
        {
            char character = tail[index];
            if (character < 0x7F && IsUnescapedAscii(character))
            {
                target[position++] = (byte)character;
                continue;
            }

            switch (character)
            {
            case '"':
                target[position++] = (byte)'\\';
                target[position++] = (byte)'"';
                continue;
            case '\\':
                target[position++] = (byte)'\\';
                target[position++] = (byte)'\\';
                continue;
            case '\n':
                target[position++] = (byte)'\\';
                target[position++] = (byte)'n';
                continue;
            case '\r':
                target[position++] = (byte)'\\';
                target[position++] = (byte)'r';
                continue;
            case '\t':
                target[position++] = (byte)'\\';
                target[position++] = (byte)'t';
                continue;
            case '\b':
                target[position++] = (byte)'\\';
                target[position++] = (byte)'b';
                continue;
            case '\f':
                target[position++] = (byte)'\\';
                target[position++] = (byte)'f';
                continue;
            }

            target[position++] = (byte)'\\';
            target[position++] = (byte)'u';
            target[position++] = HexDigits[(character >> 12) & 0xF];
            target[position++] = HexDigits[(character >> 8) & 0xF];
            target[position++] = HexDigits[(character >> 4) & 0xF];
            target[position++] = HexDigits[character & 0xF];
        }

        target[position++] = (byte)'"';
        this.length = position;
    }

    /// <summary>Appends one of the writer's fixed JSON fragments.</summary>
    /// <param name="bytes">The UTF-8 JSON bytes.</param>
    private void WriteRaw(byte[] bytes)
    {
        this.Ensure(bytes.Length);
        bytes.CopyTo(this.buffer.AsSpan(this.length));
        this.length += bytes.Length;
    }

    /// <summary>Appends one byte of JSON punctuation.</summary>
    /// <param name="value">The byte.</param>
    private void WriteByte(byte value)
    {
        if (this.length == this.buffer.Length)
        {
            this.Grow(1);
        }

        this.buffer[this.length++] = value;
    }

    /// <summary>Makes room for at least the given number of further bytes.</summary>
    /// <param name="additional">The bytes about to be written.</param>
    private void Ensure(int additional)
    {
        if (this.buffer.Length - this.length < additional)
        {
            this.Grow(additional);
        }
    }

    /// <summary>Enlarges the buffer to hold the written bytes plus <paramref name="additional"/>, at least doubling it.</summary>
    /// <param name="additional">The bytes about to be written.</param>
    /// <exception cref="OverflowException">The output would exceed the largest array size.</exception>
    private void Grow(int additional)
    {
        int required = checked(this.length + additional);
        int capacity = Math.Max(required, this.buffer.Length * 2);
        Array.Resize(ref this.buffer, capacity);
    }
}
