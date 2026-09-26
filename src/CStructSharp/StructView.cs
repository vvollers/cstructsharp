namespace CStructSharp;

using System;
using CStructSharp.Compilation;
using CStructSharp.Values;

/// <summary>
///     The bytes of one struct, read member by member when asked instead of all at once: the runtime counterpart of a
///     generated view. <c>layout.CreateView(bytes, "reading")</c> builds nothing; <c>view.Get&lt;float&gt;("pos.x")</c>
///     or <c>view.Get(accessor)</c> reads one member. In a struct whose members all have build-time offsets, a
///     fixed-width number read as its own type is decoded straight from the span and nothing is allocated.
/// </summary>
/// <remarks>
///     Every member read returns what <c>layout.ReadValue&lt;T&gt;(bytes, root + "." + path, options: options)</c>
///     returns, and fails as it fails; members the view cannot decode directly (text, enums, whole arrays, nested structs
///     as values, converted types, runtime-sized layouts) are read exactly that way. A view is a <see langword="ref"/>
///     struct over the caller's memory: it cannot be stored in a field or used after the memory changes.
/// </remarks>
public readonly ref struct StructView
{
    private readonly CStruct layout;
    private readonly CompiledCompositeType composite;
    private readonly ReadOnlySpan<byte> bytes;
    private readonly ReadOnlySpan<byte> source;
    private readonly ReadOptions? options;
    private readonly bool direct;

    /// <summary>Creates a view; see <see cref="CStruct.CreateView(ReadOnlySpan{byte}, string?, ReadOptions?)"/>.</summary>
    internal StructView(CStruct layout, string root, CompiledCompositeType composite, ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> source, ReadOptions? options, bool direct)
    {
        this.layout = layout;
        this.Root = root;
        this.composite = composite;
        this.bytes = bytes;
        this.source = source;
        this.options = options;
        this.direct = direct;
    }

    /// <summary>Gets the struct declaration the view reads.</summary>
    public string Root { get; }

    /// <summary>Gets the struct's bytes: exactly its size for a fixed-size struct, otherwise the whole source.</summary>
    public ReadOnlySpan<byte> Bytes => this.bytes;

    /// <summary>Reads one member through a prepared accessor for this view's layout and root.</summary>
    /// <typeparam name="T">The type the member is read as.</typeparam>
    /// <param name="accessor">An accessor from <c>GetAccessor&lt;T&gt;</c> of the same layout, for a path starting at <see cref="Root"/>.</param>
    /// <returns>The member.</returns>
    /// <exception cref="ArgumentException">The accessor belongs to another layout or root.</exception>
    /// <exception cref="Diagnostics.CStructPathException">The path cannot be resolved.</exception>
    /// <exception cref="Diagnostics.CStructReadException">The bytes cannot be decoded or converted to <typeparamref name="T"/>.</exception>
    public T Get<T>(FieldAccessor<T> accessor)
    {
        ArgumentNullException.ThrowIfNull(accessor);
        if (!ReferenceEquals(accessor.Layout, this.layout) || !ReferenceEquals(accessor.RootComposite, this.composite))
        {
            throw new ArgumentException("The accessor was resolved for another layout or root declaration.", nameof(accessor));
        }

        // The view checked at creation that the options cover the whole struct, so the accessor's own checks, made for
        // any options, are not repeated here.
        return this.direct && accessor.TryReadFixed(this.bytes, null, out T member)
                   ? member
                   : this.layout.ReadValue<T>(this.source, accessor.Path, null, this.options);
    }

    /// <summary>Reads one member by its path below the root (<c>pos.x</c>, <c>samples[3]</c>); the path is resolved once per layout and type.</summary>
    /// <typeparam name="T">The type the member is read as.</typeparam>
    /// <param name="path">The path below <see cref="Root"/>.</param>
    /// <returns>The member.</returns>
    /// <exception cref="Diagnostics.CStructPathException">The path is malformed or cannot be resolved.</exception>
    /// <exception cref="Diagnostics.CStructReadException">The bytes cannot be decoded or converted to <typeparamref name="T"/>.</exception>
    public T Get<T>(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        return this.Get(this.layout.GetViewAccessor<T>(this.Root, path));
    }

    /// <summary>Parses the whole struct into a <see cref="StructValue"/>, as <c>layout.Parse(bytes, root, options: options)</c> does.</summary>
    /// <returns>The parsed struct.</returns>
    public StructValue ToStructValue() => this.layout.Parse(this.source, this.Root, null, this.options);
}
