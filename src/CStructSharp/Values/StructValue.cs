namespace CStructSharp.Values;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Dynamic;
using System.Linq.Expressions;
using System.Reflection;
using CStructSharp.Compilation;
using CStructSharp.Diagnostics;

/// <summary>
///     A parsed struct: the members of one composite in declaration order, readable through <see langword="dynamic"/>
///     member access (<c>parsed.length</c>), typed through <see cref="Get{T}"/> (<c>parsed.Get&lt;uint&gt;("length")</c>),
///     through <see cref="IDictionary{TKey, TValue}"/> / <see cref="IReadOnlyDictionary{TKey, TValue}"/>
///     (<c>values["length"]</c>), or by enumerating key/value pairs.
///     Members that a conditional arm did not select are absent rather than null. Values are the same objects the
///     documented value table describes (boxed primitives, <see cref="string"/>, nested <see cref="StructValue"/>,
///     <see cref="IList{T}"/> of <see cref="object"/> for arrays, <see cref="UnionValue"/>, <see cref="Pointer"/>,
///     <see cref="EnumValueResult"/>). Instances are mutable, so a parsed
///     value can be edited and handed back to <c>Serialize</c>/<c>Update</c>.
/// </summary>
public sealed class StructValue : DynamicObject, IDictionary<string, object?>, IReadOnlyDictionary<string, object?>
{
    private static readonly object Unset = new();

    private readonly StructShape shape;
    private readonly object?[] slots;
    private Dictionary<string, object?>? extra;
    private List<string>? insertionOrder;
    private int count;

    internal StructValue(StructShape shape)
    {
        this.shape = shape;
        this.slots = shape.Count == 0 ? Array.Empty<object?>() : new object?[shape.Count];
        Array.Fill(this.slots, Unset);
    }

    /// <summary>Creates an empty value that accepts any member names, for callers assembling data to write.</summary>
    public StructValue()
        : this(StructShape.Empty)
    {
    }

    /// <summary>Gets the number of members present.</summary>
    public int Count => this.count;

    /// <summary>The member table this value was created for (the static read plan requires the composite's own).</summary>
    internal StructShape Shape => this.shape;

    bool ICollection<KeyValuePair<string, object?>>.IsReadOnly => false;

    /// <summary>Gets the member names in insertion order.</summary>
    public ICollection<string> Keys => this.EnumerateKeys().ToList();

    /// <summary>Gets the member values in insertion order.</summary>
    public ICollection<object?> Values => this.EnumeratePairs().Select(pair => pair.Value).ToList();

    IEnumerable<string> IReadOnlyDictionary<string, object?>.Keys => this.EnumerateKeys();

    IEnumerable<object?> IReadOnlyDictionary<string, object?>.Values => this.EnumeratePairs().Select(pair => pair.Value);

    /// <summary>Gets or sets a member by name; reading an absent member throws <see cref="KeyNotFoundException"/>.</summary>
    /// <param name="name">The member name, case-sensitive.</param>
    public object? this[string name]
    {
        get => this.TryGetValue(name, out object? value) ? value : throw new KeyNotFoundException($"The parsed struct has no member '{name}'.");
        set => this.Set(name, value);
    }

    /// <summary>Returns the member value, or <see langword="false"/> when the member is absent.</summary>
    /// <param name="name">The member name, case-sensitive.</param>
    /// <param name="value">The member value when present; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the member is present.</returns>
    public bool TryGetValue(string name, out object? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (this.shape.TryGetIndex(name, out int index))
        {
            object? slot = this.slots[index];
            if (!ReferenceEquals(slot, Unset))
            {
                value = slot;
                return true;
            }

            value = null;
            return false;
        }

        if (this.extra is not null)
        {
            return this.extra.TryGetValue(name, out value);
        }

        value = null;
        return false;
    }

    /// <summary>
    ///     Reads a member, or a nested value below it, as <typeparamref name="T"/> with the same checked conversion
    ///     <c>ReadValue&lt;T&gt;</c> applies: <c>header.Get&lt;ushort&gt;("kind")</c>,
    ///     <c>packet.Get&lt;byte&gt;("items[2].tag")</c>, <c>record.Get&lt;Point&gt;("origin")</c> for a nested struct
    ///     mapped to a class or record, <c>node.Get&lt;uint&gt;("next.value.id")</c> through a dereferenced pointer.
    /// </summary>
    /// <typeparam name="T">The destination type; a POCO needs a public parameterless constructor and public bindable members.</typeparam>
    /// <param name="path">A member name, or a dotted and indexed path relative to this struct.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="CStructPathException">The path is malformed or selects nothing; the message names the failing segment and the members that exist.</exception>
    /// <exception cref="CStructReadException">The value cannot be converted to <typeparamref name="T"/> without loss.</exception>
    public T Get<[DynamicallyAccessedMembers(TypedValueConverter.MappedMembers)] T>(string path)
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
    /// <typeparam name="T">The destination type; a POCO needs a public parameterless constructor and public bindable members.</typeparam>
    /// <param name="path">A member name, or a dotted and indexed path relative to this struct.</param>
    /// <param name="value">The converted value, or <see langword="default"/> when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the path resolved and the value converted.</returns>
    public bool TryGet<[DynamicallyAccessedMembers(TypedValueConverter.MappedMembers)] T>(string path, [MaybeNullWhen(false)] out T value)
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

    /// <summary>Returns whether a member is present.</summary>
    /// <param name="name">The member name, case-sensitive.</param>
    /// <returns><see langword="true"/> when the member is present.</returns>
    public bool ContainsKey(string name)
    {
        return this.TryGetValue(name, out _);
    }

    /// <summary>Adds a member; throws when it is already present.</summary>
    /// <param name="name">The member name, case-sensitive.</param>
    /// <param name="value">The member value.</param>
    public void Add(string name, object? value)
    {
        if (this.ContainsKey(name))
        {
            throw new ArgumentException($"The parsed struct already has a member '{name}'.", nameof(name));
        }

        this.Set(name, value);
    }

    /// <summary>Removes a member; returns whether it was present.</summary>
    /// <param name="name">The member name, case-sensitive.</param>
    /// <returns><see langword="true"/> when the member was present and has been removed.</returns>
    public bool Remove(string name)
    {
        ArgumentNullException.ThrowIfNull(name);
        bool removed;
        if (this.shape.TryGetIndex(name, out int index))
        {
            removed = !ReferenceEquals(this.slots[index], Unset);
            this.slots[index] = Unset;
        }
        else
        {
            removed = this.extra?.Remove(name) ?? false;
        }

        if (removed)
        {
            this.count--;
            this.insertionOrder?.Remove(name);
        }

        return removed;
    }

    /// <summary>Removes every member.</summary>
    public void Clear()
    {
        Array.Fill(this.slots, Unset);
        this.extra = null;
        this.insertionOrder = null;
        this.count = 0;
    }

    /// <summary>Enumerates members in insertion order without allocating.</summary>
    /// <returns>A value-type enumerator over the present members.</returns>
    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    IEnumerator<KeyValuePair<string, object?>> IEnumerable<KeyValuePair<string, object?>>.GetEnumerator()
    {
        return this.GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return this.GetEnumerator();
    }

    void ICollection<KeyValuePair<string, object?>>.Add(KeyValuePair<string, object?> item)
    {
        this.Add(item.Key, item.Value);
    }

    bool ICollection<KeyValuePair<string, object?>>.Contains(KeyValuePair<string, object?> item)
    {
        return this.TryGetValue(item.Key, out object? value) && Equals(value, item.Value);
    }

    void ICollection<KeyValuePair<string, object?>>.CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        foreach (KeyValuePair<string, object?> pair in this.EnumeratePairs())
        {
            array[arrayIndex++] = pair;
        }
    }

    bool ICollection<KeyValuePair<string, object?>>.Remove(KeyValuePair<string, object?> item)
    {
        return this.TryGetValue(item.Key, out object? value) && Equals(value, item.Value) && this.Remove(item.Key);
    }

    /// <summary>
    ///     Binds <see langword="dynamic"/> member access to a slot index resolved once per call site and shape, the
    ///     way <c>ExpandoObject</c> binds to its class version - a member read is then a type/shape check plus an
    ///     array index instead of a virtual <see cref="TryGetMember"/> call through a binder object.
    /// </summary>
    /// <param name="parameter">The expression representing this value at the call site.</param>
    /// <returns>The meta-object that binds member reads and writes to this value's shape.</returns>
    public override DynamicMetaObject GetMetaObject(Expression parameter)
    {
        return new MetaStructValue(parameter, this, base.GetMetaObject(parameter));
    }

    /// <summary>Reads a member for <see langword="dynamic"/> access; an absent member reports false so the binder throws.</summary>
    /// <param name="binder">The binder carrying the member name.</param>
    /// <param name="result">The member value when present; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the member is present.</returns>
    public override bool TryGetMember(GetMemberBinder binder, out object? result)
    {
        ArgumentNullException.ThrowIfNull(binder);
        if (this.TryGetValue(binder.Name, out result))
        {
            return true;
        }

        result = null;
        return false;
    }

    /// <summary>Sets or adds a member through <see langword="dynamic"/> assignment.</summary>
    /// <param name="binder">The binder carrying the member name.</param>
    /// <param name="value">The value to store.</param>
    /// <returns>Always <see langword="true"/>; any member name is accepted.</returns>
    public override bool TrySetMember(SetMemberBinder binder, object? value)
    {
        ArgumentNullException.ThrowIfNull(binder);
        this.Set(binder.Name, value);
        return true;
    }

    /// <summary>Removes a member through a dynamic delete; returns whether it was present.</summary>
    /// <param name="binder">The binder carrying the member name.</param>
    /// <returns><see langword="true"/> when the member was present and has been removed.</returns>
    public override bool TryDeleteMember(DeleteMemberBinder binder)
    {
        ArgumentNullException.ThrowIfNull(binder);
        return this.Remove(binder.Name);
    }

    /// <summary>Lists the member names present, in insertion order (used by debuggers and dynamic tooling).</summary>
    /// <returns>The member names.</returns>
    public override IEnumerable<string> GetDynamicMemberNames()
    {
        return this.EnumerateKeys();
    }

    /// <summary>Lists the members present, for debugging.</summary>
    /// <returns>A <c>{ name = value, … }</c> rendering of the members.</returns>
    public override string ToString()
    {
        return "{ " + string.Join(", ", this.EnumeratePairs().Select(pair => pair.Key + " = " + (pair.Value ?? "null"))) + " }";
    }

    /// <summary>Slot read for bound call sites; false when the member is absent so the binder's fallback throws.</summary>
    internal bool TryGetSlot(int index, out object? value)
    {
        object? slot = this.slots[index];
        if (ReferenceEquals(slot, Unset))
        {
            value = null;
            return false;
        }

        value = slot;
        return true;
    }

    /// <summary>Direct slot write for the static read plan: the shape guarantees the index and that the slot was unset.</summary>
    internal void SetFreshSlot(int index, object? value)
    {
        this.slots[index] = value;
        this.count++;
    }

    /// <summary>Slot write for bound call sites.</summary>
    private object? SetSlot(int index, object? value)
    {
        if (ReferenceEquals(this.slots[index], Unset))
        {
            this.count++;
            this.TrackInsertion(this.shape.Names[index], index);
        }

        this.slots[index] = value;
        return value;
    }

    private void Set(string name, object? value)
    {
        ArgumentNullException.ThrowIfNull(name);
        if (this.shape.TryGetIndex(name, out int index))
        {
            bool present = !ReferenceEquals(this.slots[index], Unset);
            this.slots[index] = value;
            if (!present)
            {
                this.count++;
                this.TrackInsertion(name, index);
            }

            return;
        }

        this.extra ??= new Dictionary<string, object?>(StringComparer.Ordinal);
        if (this.extra.TryAdd(name, value))
        {
            this.count++;
            this.TrackInsertion(name, -1);
        }
        else
        {
            this.extra[name] = value;
        }
    }

    /// <summary>
    ///     Enumeration follows insertion order, exactly like the ExpandoObject this type replaces. The common case -
    ///     members set in shape order with no extras - needs no bookkeeping; anything else records the order.
    /// </summary>
    private void TrackInsertion(string name, int index)
    {
        if (this.insertionOrder is null)
        {
            if (index >= 0 && this.IsShapeOrderUpTo(index))
            {
                return;
            }

            this.insertionOrder = new List<string>(this.shape.Count + 4);
            for (int slot = 0; slot < this.slots.Length; slot++)
            {
                if (!ReferenceEquals(this.slots[slot], Unset) && slot != index)
                {
                    this.insertionOrder.Add(this.shape.Names[slot]);
                }
            }

            if (this.extra is not null)
            {
                foreach (string key in this.extra.Keys)
                {
                    if (!string.Equals(key, name, StringComparison.Ordinal))
                    {
                        this.insertionOrder.Add(key);
                    }
                }
            }
        }

        this.insertionOrder.Add(name);
    }

    /// <summary>True when every present slot precedes <paramref name="index"/> (so shape order is insertion order).</summary>
    private bool IsShapeOrderUpTo(int index)
    {
        for (int slot = index + 1; slot < this.slots.Length; slot++)
        {
            if (!ReferenceEquals(this.slots[slot], Unset))
            {
                return false;
            }
        }

        return true;
    }

    private IEnumerable<string> EnumerateKeys()
    {
        if (this.insertionOrder is not null)
        {
            return this.insertionOrder;
        }

        return this.EnumerateShapeKeys();
    }

    private IEnumerable<string> EnumerateShapeKeys()
    {
        for (int slot = 0; slot < this.slots.Length; slot++)
        {
            if (!ReferenceEquals(this.slots[slot], Unset))
            {
                yield return this.shape.Names[slot];
            }
        }
    }

    private IEnumerable<KeyValuePair<string, object?>> EnumeratePairs()
    {
        foreach (KeyValuePair<string, object?> pair in this)
        {
            yield return pair;
        }
    }

    /// <summary>
    ///     Walks the slot array directly while members sit in shape order (every freshly parsed value) and falls
    ///     back to the recorded insertion order otherwise.
    /// </summary>
    public struct Enumerator : IEnumerator<KeyValuePair<string, object?>>
    {
        private readonly StructValue owner;
        private readonly List<string>? order;
        private int index;

        internal Enumerator(StructValue owner)
        {
            this.owner = owner;
            this.order = owner.insertionOrder;
            this.index = -1;
            this.Current = default;
        }

        /// <summary>Gets the member at the current position.</summary>
        public KeyValuePair<string, object?> Current { get; private set; }

        readonly object IEnumerator.Current => this.Current;

        /// <summary>Advances to the next present member.</summary>
        /// <returns><see langword="true"/> when another member is available.</returns>
        public bool MoveNext()
        {
            if (this.order is null)
            {
                object?[] slots = this.owner.slots;
                while (++this.index < slots.Length)
                {
                    object? slot = slots[this.index];
                    if (!ReferenceEquals(slot, Unset))
                    {
                        this.Current = new KeyValuePair<string, object?>(this.owner.shape.Names[this.index], slot);
                        return true;
                    }
                }

                return false;
            }

            if (++this.index < this.order.Count)
            {
                string key = this.order[this.index];
                this.owner.TryGetValue(key, out object? value);
                this.Current = new KeyValuePair<string, object?>(key, value);
                return true;
            }

            return false;
        }

        /// <summary>Returns to the position before the first member.</summary>
        public void Reset()
        {
            this.index = -1;
            this.Current = default;
        }

        /// <summary>Nothing to release.</summary>
        public readonly void Dispose()
        {
        }
    }

    private sealed class MetaStructValue : DynamicMetaObject
    {
        private static readonly MethodInfo TryGetSlotMethod = typeof(StructValue).GetMethod(nameof(TryGetSlot), BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly MethodInfo SetSlotMethod = typeof(StructValue).GetMethod(nameof(SetSlot), BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly FieldInfo ShapeField = typeof(StructValue).GetField(nameof(shape), BindingFlags.Instance | BindingFlags.NonPublic)!;

        private readonly DynamicMetaObject fallback;

        public MetaStructValue(Expression expression, StructValue value, DynamicMetaObject fallback)
            : base(expression, BindingRestrictions.Empty, value)
        {
            this.fallback = fallback;
        }

        private StructValue Target => (StructValue)this.Value!;

        private Expression Self => Expression.Convert(this.Expression, typeof(StructValue));

        public override DynamicMetaObject BindGetMember(GetMemberBinder binder)
        {
            if (!this.Target.shape.TryGetIndex(binder.Name, out int index))
            {
                return this.fallback.BindGetMember(binder);
            }

            ParameterExpression value = Expression.Variable(typeof(object), "value");
            DynamicMetaObject missing = binder.FallbackGetMember(this);
            Expression body = Expression.Block(
                [value],
                Expression.Condition(
                    Expression.Call(this.Self, TryGetSlotMethod, Expression.Constant(index), value),
                    value,
                    Expression.Convert(missing.Expression, typeof(object))));
            return new DynamicMetaObject(body, this.ShapeRestrictions().Merge(missing.Restrictions));
        }

        public override DynamicMetaObject BindSetMember(SetMemberBinder binder, DynamicMetaObject value)
        {
            if (!this.Target.shape.TryGetIndex(binder.Name, out int index))
            {
                return this.fallback.BindSetMember(binder, value);
            }

            Expression body = Expression.Call(
                this.Self,
                SetSlotMethod,
                Expression.Constant(index),
                Expression.Convert(value.Expression, typeof(object)));
            return new DynamicMetaObject(body, this.ShapeRestrictions().Merge(value.Restrictions));
        }

        public override DynamicMetaObject BindDeleteMember(DeleteMemberBinder binder)
        {
            return this.fallback.BindDeleteMember(binder);
        }

        public override DynamicMetaObject BindInvokeMember(InvokeMemberBinder binder, DynamicMetaObject[] args)
        {
            return this.fallback.BindInvokeMember(binder, args);
        }

        public override DynamicMetaObject BindConvert(ConvertBinder binder)
        {
            return this.fallback.BindConvert(binder);
        }

        public override DynamicMetaObject BindGetIndex(GetIndexBinder binder, DynamicMetaObject[] indexes)
        {
            return this.fallback.BindGetIndex(binder, indexes);
        }

        public override DynamicMetaObject BindSetIndex(SetIndexBinder binder, DynamicMetaObject[] indexes, DynamicMetaObject value)
        {
            return this.fallback.BindSetIndex(binder, indexes, value);
        }

        public override IEnumerable<string> GetDynamicMemberNames()
        {
            return this.Target.EnumerateKeys();
        }

        /// <summary>The rule stays valid for any StructValue of the same shape, so one call site serves every parse.</summary>
        private BindingRestrictions ShapeRestrictions()
        {
            return BindingRestrictions.GetTypeRestriction(this.Expression, typeof(StructValue)).Merge(
                BindingRestrictions.GetExpressionRestriction(
                    Expression.ReferenceEqual(
                        Expression.Field(this.Self, ShapeField),
                        Expression.Constant(this.Target.shape, typeof(StructShape)))));
        }
    }
}
