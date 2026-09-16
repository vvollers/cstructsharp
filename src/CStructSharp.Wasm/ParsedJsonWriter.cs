namespace CStructSharpWeb.Wasm;

using System;
using System.Buffers;
using System.Buffers.Text;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using System.Text;
using CStructSharp;

/// <summary>
///     Writes parsed values as UTF-8 JSON straight into a byte buffer (E3.3b). The value set is closed - the
///     core parser produces only <see cref="StructValue"/>, <see cref="PrimitiveArray{T}"/>, lists, boxed
///     numbers, strings, enums, unions and pointers - so this writer needs no state machine, no name validation
///     and no per-write escaping decisions, which is what made <see cref="System.Text.Json.Utf8JsonWriter"/> cost
///     ≈ 50 ns per output byte under the WebAssembly interpreter. Output is byte-compatible with the previous
///     <c>Utf8JsonWriter</c> projection (same number formatting, same JavaScript-safe integer rule, same
///     <c>JavaScriptEncoder.Default</c> escaping).
/// </summary>
internal sealed class ParsedJsonWriter
{
    private const long MaximumSafeInteger = 9_007_199_254_740_991;

    private static readonly byte[] NullBytes = "null"u8.ToArray();
    private static readonly byte[] TrueBytes = "true"u8.ToArray();
    private static readonly byte[] FalseBytes = "false"u8.ToArray();
    private static readonly byte[] UnionHead = "{\"$kind\":\"union\",\"Union\":"u8.ToArray();
    private static readonly byte[] RawStorageName = ",\"RawStorage\":"u8.ToArray();
    private static readonly byte[] MembersName = ",\"Members\":{"u8.ToArray();
    private static readonly byte[] SelectedMemberName = "},\"SelectedMember\":"u8.ToArray();
    private static readonly byte[] PointerAddress = "{\"Address\":"u8.ToArray();
    private static readonly byte[] PointerDepth = ",\"Depth\":"u8.ToArray();
    private static readonly byte[] PointerIsDereferenced = ",\"IsDereferenced\":"u8.ToArray();
    private static readonly byte[] PointerValue = ",\"Value\":"u8.ToArray();
    private static readonly byte[] EnumHead = "{\"Enum\":"u8.ToArray();
    private static readonly byte[] EnumName = ",\"Name\":"u8.ToArray();
    private static readonly byte[] EnumValue = ",\"Value\":"u8.ToArray();
    private static readonly byte[] FlagNames = ",\"Names\":"u8.ToArray();
    private static readonly byte[] FlagRemainder = ",\"Remainder\":"u8.ToArray();
    private static readonly byte[] HexDigits = "0123456789abcdef"u8.ToArray();
    private static readonly SearchValues<byte> UnescapedUtf8 = SearchValues.Create("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 !#$%()*,-./:;=?@[]^_`{|}~"u8);

    private byte[] buffer;
    private int length;

    public ParsedJsonWriter(int capacity)
    {
        this.buffer = new byte[capacity];
    }

    /// <summary>The bytes written so far.</summary>
    public ReadOnlySpan<byte> WrittenSpan => this.buffer.AsSpan(0, this.length);

    public int Capacity => this.buffer.Length;

    public void Reset()
    {
        this.length = 0;
    }

    /// <summary>Appends bytes that are already valid JSON (envelope framing, source-generated fragments).</summary>
    public void WriteRawBytes(ReadOnlySpan<byte> bytes)
    {
        this.Ensure(bytes.Length);
        bytes.CopyTo(this.buffer.AsSpan(this.length));
        this.length += bytes.Length;
    }

    /// <summary>Writes one parsed value (the top-level or any nested one).</summary>
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
            this.WriteRaw(PointerAddress);
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
            // Any other value (for example a scalar produced by a typed alias) renders as its invariant text, as before.
            this.WriteString(value is IFormattable formattable ? formattable.ToString(null, CultureInfo.InvariantCulture) : value.ToString() ?? string.Empty);
            return;
        }
    }

    private static bool IsUnescapedAscii(char character)
    {
        // JavaScriptEncoder.Default's allowed set: letters, digits, and  !#$%()*,-./:;=?@[]^_`{|}~ and space.
        if (character is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9'))
        {
            return true;
        }

        return character is ' ' or '!' or '#' or '$' or '%' or '(' or ')' or '*' or ',' or '-' or '.' or '/' or ':' or ';' or '=' or '?' or '@' or '[' or ']' or '^' or '_' or '`' or '{' or '|' or '}' or '~';
    }

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

    private void WriteNumber(long value)
    {
        this.Ensure(20);
        Utf8Formatter.TryFormat(value, this.buffer.AsSpan(this.length), out int written);
        this.length += written;
    }

    private void WriteNumber(ulong value)
    {
        this.Ensure(20);
        Utf8Formatter.TryFormat(value, this.buffer.AsSpan(this.length), out int written);
        this.length += written;
    }

    private void WriteNumber(float value)
    {
        if (!float.IsFinite(value))
        {
            throw new ArgumentException("A non-finite floating-point value cannot be written as JSON.");
        }

        this.Ensure(32);
        Utf8Formatter.TryFormat(value, this.buffer.AsSpan(this.length), out int written);
        this.length += written;
    }

    private void WriteNumber(double value)
    {
        if (!double.IsFinite(value))
        {
            throw new ArgumentException("A non-finite floating-point value cannot be written as JSON.");
        }

        this.Ensure(32);
        Utf8Formatter.TryFormat(value, this.buffer.AsSpan(this.length), out int written);
        this.length += written;
    }

    private void WriteNumber(decimal value)
    {
        this.Ensure(40);
        Utf8Formatter.TryFormat(value, this.buffer.AsSpan(this.length), out int written);
        this.length += written;
    }

    /// <summary>Integers beyond JavaScript's exact range are sent as decimal strings, as before.</summary>
    private void WriteSafeInteger(long value)
    {
        if (value is >= -MaximumSafeInteger and <= MaximumSafeInteger)
        {
            this.WriteNumber(value);
        }
        else
        {
            this.WriteString(value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void WriteSafeInteger(ulong value)
    {
        if (value <= MaximumSafeInteger)
        {
            this.WriteNumber(value);
        }
        else
        {
            this.WriteString(value.ToString(CultureInfo.InvariantCulture));
        }
    }

    private void WriteSafeInteger(BigInteger value)
    {
        if (value >= -MaximumSafeInteger && value <= MaximumSafeInteger)
        {
            this.WriteNumber((long)value);
        }
        else
        {
            this.WriteString(value.ToString(CultureInfo.InvariantCulture));
        }
    }

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
    ///     Escapes like <c>JavaScriptEncoder.Default</c>: ASCII letters, digits and a small punctuation set pass
    ///     through; quotes, backslashes, control characters, HTML-sensitive characters and everything non-ASCII
    ///     are written as escapes.
    /// </summary>
    private void WriteString(string text)
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

    private void WriteRaw(byte[] bytes)
    {
        this.Ensure(bytes.Length);
        bytes.CopyTo(this.buffer.AsSpan(this.length));
        this.length += bytes.Length;
    }

    private void WriteByte(byte value)
    {
        if (this.length == this.buffer.Length)
        {
            this.Grow(1);
        }

        this.buffer[this.length++] = value;
    }

    private void Ensure(int additional)
    {
        if (this.buffer.Length - this.length < additional)
        {
            this.Grow(additional);
        }
    }

    private void Grow(int additional)
    {
        int required = checked(this.length + additional);
        int capacity = Math.Max(required, this.buffer.Length * 2);
        Array.Resize(ref this.buffer, capacity);
    }
}
