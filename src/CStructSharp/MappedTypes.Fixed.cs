namespace CStructSharp;

using System;
using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

/// <summary>The registry of the direct members of layout-bound mapped classes (<see cref="ICStructFixedMapped{TSelf}"/>).</summary>
public static partial class MappedTypes
{
    private static readonly ConcurrentDictionary<Type, FixedWriterEntry> FixedWriters = new();

    /// <summary>Reads one instance directly from a fixed struct's bytes; see <see cref="ICStructFixedMapped{TSelf}.TryReadFixed"/>.</summary>
    /// <typeparam name="T">The mapped type read.</typeparam>
    /// <param name="source">The struct's bytes, at least the struct's size.</param>
    /// <param name="trimFixedText">Whether fixed-capacity text drops its trailing NUL padding.</param>
    /// <param name="value">The instance when the delegate returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> when the bytes cannot be read directly.</returns>
    internal delegate bool FixedReader<T>(ReadOnlySpan<byte> source, bool trimFixedText, [MaybeNullWhen(false)] out T value);

    /// <summary>Writes one instance directly into a fixed struct's bytes; see <see cref="ICStructFixedMapped{TSelf}.TryWriteFixed"/>.</summary>
    /// <typeparam name="T">The mapped type written.</typeparam>
    /// <param name="value">The instance to write.</param>
    /// <param name="target">The struct's bytes, already zero, at least the struct's size.</param>
    /// <returns><see langword="false"/> when the instance cannot be written directly.</returns>
    internal delegate bool FixedWriter<T>(T value, Span<byte> target);

    /// <summary>
    ///     Registers the direct members of <typeparamref name="T"/>, after <see cref="Register{T}"/>. The generated module
    ///     initializer of a layout-bound mapped class calls both.
    /// </summary>
    /// <typeparam name="T">A mapped class that also implements <see cref="ICStructFixedMapped{TSelf}"/>.</typeparam>
    public static void RegisterFixed<T>()
        where T : ICStructMapped<T>, ICStructFixedMapped<T>
    {
        Fixed<T>.Entry = new FixedEntry<T>(T.FixedLayoutFingerprint, T.TryReadFixed, T.TryWriteFixed);
        FixedWriters[typeof(T)] = new FixedWriterEntry(T.FixedLayoutFingerprint, static (instance, target) => T.TryWriteFixed((T)instance, target));
    }

    /// <summary>
    ///     Reads a nested mapped instance directly, for a generated direct reader: only when <typeparamref name="T"/> has
    ///     direct members generated for a struct with <paramref name="fingerprint"/> - the nested struct the caller was
    ///     generated against.
    /// </summary>
    /// <typeparam name="T">The nested mapped class.</typeparam>
    /// <param name="source">The nested struct's bytes.</param>
    /// <param name="fingerprint">The fingerprint of the nested struct in the caller's layout.</param>
    /// <param name="trimFixedText">Whether fixed-capacity text drops its trailing NUL padding.</param>
    /// <param name="value">The instance when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="false"/> when <typeparamref name="T"/> cannot be read directly here.</returns>
    public static bool TryReadFixed<T>(ReadOnlySpan<byte> source, ulong fingerprint, bool trimFixedText, [MaybeNullWhen(false)] out T value)
    {
        if (Fixed<T>.Entry is { } entry && entry.Fingerprint == fingerprint)
        {
            return entry.Read(source, trimFixedText, out value);
        }

        value = default;
        return false;
    }

    /// <summary>Writes a nested mapped instance directly, for a generated direct writer; the counterpart of <see cref="TryReadFixed{T}"/>.</summary>
    /// <typeparam name="T">The nested mapped class.</typeparam>
    /// <param name="value">The nested instance.</param>
    /// <param name="target">The nested struct's bytes, already zero.</param>
    /// <param name="fingerprint">The fingerprint of the nested struct in the caller's layout.</param>
    /// <returns><see langword="false"/> when <typeparamref name="T"/> cannot be written directly here.</returns>
    public static bool TryWriteFixed<T>(T value, Span<byte> target, ulong fingerprint)
    {
        return Fixed<T>.Entry is { } entry && entry.Fingerprint == fingerprint && entry.Write(value, target);
    }

    /// <summary>The direct reader of <typeparamref name="T"/> for a struct with <paramref name="fingerprint"/>, or <see langword="null"/>.</summary>
    /// <typeparam name="T">The mapped class to read.</typeparam>
    /// <param name="fingerprint">The fingerprint of the struct the caller reads.</param>
    /// <returns>
    ///     The reader, or <see langword="null"/> when <typeparamref name="T"/> has no direct members for that struct.
    /// </returns>
    internal static FixedReader<T>? FindFixedReader<T>(ulong fingerprint)
    {
        return Fixed<T>.Entry is { } entry && entry.Fingerprint == fingerprint ? entry.Read : null;
    }

    /// <summary>The direct writer of an instance's type for a struct with <paramref name="fingerprint"/>, or <see langword="null"/>.</summary>
    /// <param name="type">The instance's runtime type, as registered by <see cref="RegisterFixed{T}"/>.</param>
    /// <param name="fingerprint">The fingerprint of the struct the caller writes.</param>
    /// <returns>
    ///     The untyped writer, or <see langword="null"/> when the type is unregistered or was generated for
    ///     another struct.
    /// </returns>
    internal static FixedWriter<object>? FindFixedWriter(Type type, ulong fingerprint)
    {
        return FixedWriters.TryGetValue(type, out FixedWriterEntry? entry) && entry.Fingerprint == fingerprint ? entry.Write : null;
    }

    /// <summary>The direct members registered for one type, with the fingerprint they were generated for.</summary>
    /// <param name="Fingerprint">The layout fingerprint.</param>
    /// <param name="Read">The direct reader.</param>
    /// <param name="Write">The direct writer.</param>
    private sealed record FixedEntry<T>(ulong Fingerprint, FixedReader<T> Read, FixedWriter<T> Write);

    /// <summary>A registered direct writer reached by the instance's runtime type, for the untyped write path.</summary>
    /// <param name="Fingerprint">The layout fingerprint.</param>
    /// <param name="Write">Writes an instance of the registered type.</param>
    private sealed record FixedWriterEntry(ulong Fingerprint, FixedWriter<object> Write);

    /// <summary>The direct members of <typeparamref name="T"/>, reached through the type parameter without a lookup.</summary>
    /// <typeparam name="T">The mapped class; unconstrained so that any caller type parameter can ask.</typeparam>
    private static class Fixed<T>
    {
        /// <summary>Gets or sets the registered entry, or <see langword="null"/> when <typeparamref name="T"/> has none.</summary>
        public static FixedEntry<T>? Entry { get; set; }
    }
}
