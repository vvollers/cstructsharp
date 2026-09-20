namespace CStructSharp.Values;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using System.Text;
using CStructSharp.Diagnostics;

/// <summary>
///     Represents the complete storage and overlapping decoded views of a C union without inventing an active member.
/// </summary>
/// <remarks>
///     Instances are shallowly immutable. Parsed values snapshot the complete raw storage and expose every decoded
///     member view; callers must explicitly select a member before changing what a writer encodes.
/// </remarks>
public sealed class UnionValue : IDynamicMetaObjectProvider, IReadOnlyDictionary<string, object?>
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
        // An anonymous (promoted) union has no name of its own (the empty string); its value is only ever an
        // intermediate. Any other name must be a real declaration name.
        ArgumentNullException.ThrowIfNull(unionName);
        if (unionName.Length > 0 && string.IsNullOrWhiteSpace(unionName))
        {
            throw new ArgumentException("Union name must not be whitespace.", nameof(unionName));
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
    /// <exception cref="ArgumentException"><paramref name="unionName"/> is whitespace (the empty string names an anonymous union).</exception>
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
    /// <exception cref="ArgumentException"><paramref name="unionName"/> is whitespace, or <paramref name="memberName"/> is empty or whitespace.</exception>
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

    /// <summary>
    ///     Reads a member, or a nested value below it, as <typeparamref name="T"/> with the same checked conversion
    ///     <c>ReadValue&lt;T&gt;</c> applies - <c>choice.Get&lt;ushort&gt;("wide")</c>.
    /// </summary>
    /// <typeparam name="T">The destination type: a scalar, string, enum, array, <see cref="StructValue"/>, <see cref="UnionValue"/>, <see cref="Pointer"/>, or a class implementing <see cref="ICStructMapped{TSelf}"/>.</typeparam>
    /// <param name="path">A member name, or a dotted and indexed path relative to this union.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="CStructPathException">The path is malformed or selects nothing; the message names the failing segment and the members that exist.</exception>
    /// <exception cref="CStructReadException">The value cannot be converted to <typeparamref name="T"/> without loss.</exception>
    public T Get<T>(string path)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!ValuePath.TryResolve(this, path, out object? value, out string? failure))
        {
            throw new CStructPathException(failure);
        }

        return (T)TypedValueConverter.Convert(value, typeof(T), path)!;
    }

    /// <summary>
    ///     Reads a member, or a nested value below it, as <typeparamref name="T"/>; returns <see langword="false"/>
    ///     instead of throwing when the path selects nothing or the value does not convert.
    /// </summary>
    /// <typeparam name="T">The destination type: a scalar, string, enum, array, <see cref="StructValue"/>, <see cref="UnionValue"/>, <see cref="Pointer"/>, or a class implementing <see cref="ICStructMapped{TSelf}"/>.</typeparam>
    /// <param name="path">A member name, or a dotted and indexed path relative to this union.</param>
    /// <param name="value">The converted value, or <see langword="default"/> when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the path resolved and the value converted.</returns>
    public bool TryGet<T>(string path, [MaybeNullWhen(false)] out T value)
    {
        ArgumentNullException.ThrowIfNull(path);
        if (!ValuePath.TryResolve(this, path, out object? natural, out _))
        {
            value = default;
            return false;
        }

        try
        {
            value = (T)TypedValueConverter.Convert(natural, typeof(T), path)!;
            return true;
        }
        catch (CStructReadException)
        {
            value = default;
            return false;
        }
    }

    /// <summary>
    ///     Describes the union for debugging: its name, the selected member when one is set, every decoded member,
    ///     and the raw storage length when the value carries the bytes as read.
    /// </summary>
    /// <returns>A <c>choice { selected: small; small = 52, large = 4660; 2 raw bytes }</c> style rendering.</returns>
    public override string ToString()
    {
        var text = new StringBuilder(this.UnionName.Length == 0 ? "union" : this.UnionName).Append(" { ");
        if (this.SelectedMember is not null)
        {
            text.Append("selected: ").Append(this.SelectedMember).Append("; ");
        }

        text.Append(string.Join(", ", this.members.Select(pair => pair.Key + " = " + (pair.Value ?? "null"))));
        if (this.rawStorage is not null)
        {
            text.Append(this.members.Count == 0 ? string.Empty : "; ").Append(this.rawStorage.Length).Append(" raw bytes");
        }

        return text.Append(" }").ToString();
    }

    /// <summary>
    ///     Binds <see langword="dynamic"/> member reads (<c>choice.wide</c>) to the decoded member views by exact
    ///     name; everything else binds to the ordinary members of this class.
    /// </summary>
    /// <param name="parameter">The expression representing this value at the call site.</param>
    /// <returns>The meta-object that binds member reads to the decoded views.</returns>
    DynamicMetaObject IDynamicMetaObjectProvider.GetMetaObject(Expression parameter)
    {
        return new MetaUnionValue(parameter, this);
    }

    /// <summary>Creates the lossless result returned by the compiled union reader.</summary>
    internal static UnionValue FromParsed(
        string unionName,
        byte[] rawStorage,
        IEnumerable<KeyValuePair<string, object?>> members)
    {
        return new UnionValue(unionName, rawStorage, members, null, null);
    }

    /// <summary>
    ///     Gets the raw storage array for validated writer use without exposing the private snapshot publicly. The
    ///     internal writer only reads from the returned array; it never mutates it.
    /// </summary>
    internal byte[] GetRawStorageArray()
    {
        return this.rawStorage ?? throw new InvalidOperationException("This union value has no raw storage.");
    }

    /// <summary>Rejects names that cannot identify a declared union member.</summary>
    private static void ValidateMemberName(string memberName)
    {
        if (string.IsNullOrWhiteSpace(memberName))
        {
            throw new ArgumentException("A union member name is required.", nameof(memberName));
        }
    }

    private sealed class MetaUnionValue(Expression expression, UnionValue value)
        : DynamicMetaObject(expression, BindingRestrictions.Empty, value)
    {
        private static readonly MethodInfo TryGetValueMethod = typeof(UnionValue).GetMethod(nameof(TryGetValue), BindingFlags.Instance | BindingFlags.Public)!;

        public override DynamicMetaObject BindGetMember(GetMemberBinder binder)
        {
            ParameterExpression result = Expression.Variable(typeof(object), "result");
            DynamicMetaObject missing = binder.FallbackGetMember(this);
            Expression body = Expression.Block(
                [result],
                Expression.Condition(
                    Expression.Call(Expression.Convert(this.Expression, typeof(UnionValue)), TryGetValueMethod, Expression.Constant(binder.Name), result),
                    result,
                    Expression.Convert(missing.Expression, typeof(object))));
            return new DynamicMetaObject(body, BindingRestrictions.GetTypeRestriction(this.Expression, typeof(UnionValue)).Merge(missing.Restrictions));
        }

        public override IEnumerable<string> GetDynamicMemberNames()
        {
            return ((UnionValue)this.Value!).members.Keys;
        }
    }
}
