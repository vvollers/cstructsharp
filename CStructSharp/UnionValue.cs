namespace CStructSharp;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Dynamic;
using System.Linq;

/// <summary>
///     Represents the complete storage and overlapping decoded views of a C union without inventing an active member.
/// </summary>
/// <remarks>
///     Instances are shallowly immutable. Parsed values snapshot the complete raw storage and expose every decoded
///     member view; callers must explicitly select a member before changing what a writer encodes.
/// </remarks>
public sealed class UnionValue : DynamicObject, IReadOnlyDictionary<string, object?>
{
    private readonly byte[]? rawStorage;
    private readonly IReadOnlyDictionary<string, object?> members;

    /// <summary>Initializes a shallowly immutable union value from snapshotted storage and member views.</summary>
    private UnionValue(
        string unionName,
        byte[]? rawStorage,
        IEnumerable<KeyValuePair<string, object?>> members,
        string? selectedMember,
        object? selectedValue)
    {
        if (string.IsNullOrWhiteSpace(unionName))
        {
            throw new ArgumentException("A union name is required.", nameof(unionName));
        }

        this.UnionName = unionName;
        this.rawStorage = rawStorage is null ? null : (byte[])rawStorage.Clone();
        this.members = new ReadOnlyDictionary<string, object?>(
            members.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal));
        this.SelectedMember = selectedMember;
        this.SelectedValue = selectedValue;
    }

    /// <summary>Gets the declared union type name.</summary>
    public string UnionName { get; }

    /// <summary>Gets a defensive view of the complete parsed or explicitly supplied union storage.</summary>
    public ReadOnlyMemory<byte>? RawStorage =>
        this.rawStorage is null
            ? (ReadOnlyMemory<byte>?)null
            : new ReadOnlyMemory<byte>((byte[])this.rawStorage.Clone());

    /// <summary>Gets a value indicating whether this value contains complete raw union storage.</summary>
    public bool HasRawStorage => this.rawStorage is not null;

    /// <summary>Gets the decoded overlapping member views in declaration order.</summary>
    public IReadOnlyDictionary<string, object?> Members => this.members;

    /// <summary>Gets the explicitly selected member name, or <see langword="null"/> for untagged raw storage.</summary>
    public string? SelectedMember { get; }

    /// <summary>Gets the explicitly selected member value, including a selected <see langword="null"/> pointer.</summary>
    public object? SelectedValue { get; }

    /// <summary>Gets a value indicating whether a member was explicitly selected for writing.</summary>
    public bool HasSelection => this.SelectedMember is not null;

    /// <summary>Gets the decoded member names in declaration order.</summary>
    public IEnumerable<string> Keys => this.members.Keys;

    /// <summary>Gets the decoded member values in declaration order.</summary>
    public IEnumerable<object?> Values => this.members.Values;

    /// <summary>Gets the number of decoded member views.</summary>
    public int Count => this.members.Count;

    /// <summary>Gets the decoded view for the exact declared member name.</summary>
    /// <param name="key">The case-sensitive declared member name.</param>
    /// <returns>The decoded view, which may be <see langword="null"/>.</returns>
    /// <exception cref="KeyNotFoundException"><paramref name="key"/> has no decoded view.</exception>
    public object? this[string key] => this.members[key];

    /// <summary>Creates a byte-exact union value without inferring a selected member.</summary>
    /// <param name="unionName">The case-sensitive declared union type name.</param>
    /// <param name="rawStorage">The complete union storage to snapshot.</param>
    /// <returns>A union value configured for byte-exact raw pass-through.</returns>
    /// <exception cref="ArgumentException"><paramref name="unionName"/> is empty or whitespace.</exception>
    public static UnionValue FromRaw(string unionName, ReadOnlySpan<byte> rawStorage)
    {
        return new UnionValue(
            unionName,
            rawStorage.ToArray(),
            Array.Empty<KeyValuePair<string, object?>>(),
            null,
            null);
    }

    /// <summary>Creates a new union value with one member explicitly selected for writing.</summary>
    /// <param name="unionName">The case-sensitive declared union type name.</param>
    /// <param name="memberName">The case-sensitive declared member name to select.</param>
    /// <param name="value">The selected member value, including <see langword="null"/> for a null pointer member.</param>
    /// <returns>A union value configured to encode <paramref name="memberName"/>.</returns>
    /// <exception cref="ArgumentException"><paramref name="unionName"/> or <paramref name="memberName"/> is empty or whitespace.</exception>
    public static UnionValue FromMember(string unionName, string memberName, object? value)
    {
        ValidateMemberName(memberName);
        return new UnionValue(
            unionName,
            null,
            [new KeyValuePair<string, object?>(memberName, value),],
            memberName,
            value);
    }

    /// <summary>
    ///     Selects one member for writing while retaining any raw snapshot and other decoded views for inspection.
    /// </summary>
    /// <param name="memberName">The case-sensitive declared member name to select.</param>
    /// <param name="value">The selected member value, including <see langword="null"/> for a null pointer member.</param>
    /// <returns>A new shallowly immutable union value with the requested selection.</returns>
    /// <exception cref="ArgumentException"><paramref name="memberName"/> is empty or whitespace.</exception>
    public UnionValue WithSelectedMember(string memberName, object? value)
    {
        ValidateMemberName(memberName);
        var updatedMembers = new List<KeyValuePair<string, object?>>(this.members.Count + 1);
        bool replaced = false;
        foreach (KeyValuePair<string, object?> member in this.members)
        {
            if (string.Equals(member.Key, memberName, StringComparison.Ordinal))
            {
                updatedMembers.Add(new KeyValuePair<string, object?>(memberName, value));
                replaced = true;
            }
            else
            {
                updatedMembers.Add(member);
            }
        }

        if (!replaced)
        {
            updatedMembers.Add(new KeyValuePair<string, object?>(memberName, value));
        }

        return new UnionValue(this.UnionName, this.rawStorage, updatedMembers, memberName, value);
    }

    /// <summary>Removes an explicit selection and restores raw pass-through behavior.</summary>
    /// <returns>A new union value that writes its retained raw storage byte for byte.</returns>
    /// <exception cref="InvalidOperationException">This value has no complete raw storage to preserve.</exception>
    public UnionValue WithoutSelection()
    {
        if (this.rawStorage is null)
        {
            throw new InvalidOperationException("A union without raw storage must keep an explicit selected member.");
        }

        return new UnionValue(this.UnionName, this.rawStorage, this.members, null, null);
    }

    /// <summary>Returns whether a decoded view exists for the exact member name.</summary>
    /// <param name="key">The case-sensitive declared member name.</param>
    /// <returns><see langword="true"/> when a decoded view exists; otherwise, <see langword="false"/>.</returns>
    public bool ContainsKey(string key)
    {
        return this.members.ContainsKey(key);
    }

    /// <summary>Attempts to get the decoded view for the exact member name.</summary>
    /// <param name="key">The case-sensitive declared member name.</param>
    /// <param name="value">Receives the decoded view when found; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when a decoded view exists; otherwise, <see langword="false"/>.</returns>
    public bool TryGetValue(string key, out object? value)
    {
        return this.members.TryGetValue(key, out value);
    }

    /// <summary>Returns an enumerator over decoded member names and values in declaration order.</summary>
    /// <returns>An enumerator over the read-only member snapshot.</returns>
    public IEnumerator<KeyValuePair<string, object?>> GetEnumerator()
    {
        return this.members.GetEnumerator();
    }

    /// <inheritdoc />
    IEnumerator IEnumerable.GetEnumerator()
    {
        return this.GetEnumerator();
    }

    /// <summary>Attempts dynamic lookup using an exact declared member name.</summary>
    /// <param name="binder">The dynamic member request, whose <see cref="GetMemberBinder.Name"/> is matched case-sensitively.</param>
    /// <param name="result">Receives the decoded member view when found; otherwise, <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the requested member has a decoded view; otherwise, <see langword="false"/>.</returns>
    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        return this.members.TryGetValue(binder.Name, out result);
    }

    /// <summary>Creates the lossless result returned by the compiled union reader.</summary>
    internal static UnionValue FromParsed(
        string unionName,
        byte[] rawStorage,
        IEnumerable<KeyValuePair<string, object?>> members)
    {
        return new UnionValue(unionName, rawStorage, members, null, null);
    }

    /// <summary>Copies the raw storage for validated writer use without exposing the private snapshot publicly.</summary>
    internal byte[] GetRawStorageCopy()
    {
        return this.rawStorage is null
                   ? throw new InvalidOperationException("This union value has no raw storage.")
                   : (byte[])this.rawStorage.Clone();
    }

    /// <summary>Rejects names that cannot identify a declared union member.</summary>
    private static void ValidateMemberName(string memberName)
    {
        if (string.IsNullOrWhiteSpace(memberName))
        {
            throw new ArgumentException("A union member name is required.", nameof(memberName));
        }
    }
}
