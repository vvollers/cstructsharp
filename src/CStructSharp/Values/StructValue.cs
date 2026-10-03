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
public sealed class StructValue : IDynamicMetaObjectProvider, IDictionary<string, object?>, IReadOnlyDictionary<string, object?>
{
    private static readonly object Unset = new();

    private readonly StructShape shape;
    private readonly object?[] slots;
    private Dictionary<string, object?>? extra;
    private List<string>? insertionOrder;
    private int count;

    // An upper bound on the index of every present shape slot (-1 when none was ever set). A slot inserted above it
    // extends shape order without the scan TrackInsertion otherwise needs, so filling a value in shape order is O(1)
    // per member; a removal leaves the bound where it was, which only sends a later insertion to the exact scan.
    private int highestSlot = -1;

    /// <summary>
    ///     Creates an empty value whose slots follow <paramref name="shape"/>; every member starts absent.
    /// </summary>
    /// <param name="shape">The member table shared by every value of the same composite.</param>
    internal StructValue(StructShape shape)
    {
        this.shape = shape;
        this.slots = shape.Count == 0 ? Array.Empty<object?>() : new object?[shape.Count];
        Array.Fill(this.slots, Unset);
    }

    /// <summary>Owns a complete fixed-plan result without initializing or tracking absent members.</summary>
    /// <param name="shape">The member table shared by values of this composite.</param>
    /// <param name="slots">One value per shape member, in shape order; ownership passes to this instance.</param>
    /// <remarks>
    ///     Only a complete fixed plan may use this constructor. The caller fills every slot before exposing the value;
    ///     after publishing the completed result, it must not retain or modify the array.
    /// </remarks>
    internal StructValue(StructShape shape, object?[] slots)
    {
        this.shape = shape;
        this.slots = slots;
        this.count = slots.Length;
        this.highestSlot = slots.Length - 1;
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

    /// <summary>Gets <see langword="false"/>: members can be added, replaced, and removed.</summary>
    bool ICollection<KeyValuePair<string, object?>>.IsReadOnly => false;

    /// <summary>Gets the member names in insertion order.</summary>
    public ICollection<string> Keys => this.EnumerateKeys().ToList();

    /// <summary>Gets the member values in insertion order.</summary>
    public ICollection<object?> Values => this.EnumeratePairs().Select(pair => pair.Value).ToList();

    /// <summary>Gets the member names in insertion order, evaluated lazily over the current members.</summary>
    IEnumerable<string> IReadOnlyDictionary<string, object?>.Keys => this.EnumerateKeys();

    /// <summary>Gets the member values in insertion order, evaluated lazily over the current members.</summary>
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
    ///     <see cref="TryGetValue(string, out object?)"/> for a member named by a section of <paramref name="text"/>,
    ///     so a path walk looks up each segment without allocating it.
    /// </summary>
    /// <param name="text">The string holding the name, usually a whole path.</param>
    /// <param name="start">The name's first character.</param>
    /// <param name="length">The name's length in characters.</param>
    /// <param name="value">The member value when present; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the member is present.</returns>
    internal bool TryGetValue(string text, int start, int length, out object? value)
    {
        if (this.shape.TryGetIndex(text, start, length, out int index))
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
            // Members outside the shape exist only on values assembled by a caller; the substring is rare here.
            return this.extra.TryGetValue(text.Substring(start, length), out value);
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
    /// <typeparam name="T">The destination type: a scalar, string, enum, array, <see cref="StructValue"/>, <see cref="UnionValue"/>, <see cref="Pointer"/>, or a class implementing <see cref="ICStructMapped{TSelf}"/>.</typeparam>
    /// <param name="path">A member name, or a dotted and indexed path relative to this struct.</param>
    /// <returns>The converted value.</returns>
    /// <exception cref="CStructPathException">The path is malformed or selects nothing; the message names the failing segment and the members that exist.</exception>
    /// <exception cref="CStructReadException">The value cannot be converted to <typeparamref name="T"/> without loss.</exception>
    public T Get<T>(string path)
    {
        return ValuePath.Get<T>(this, path);
    }

    /// <summary>Maps this struct to <typeparamref name="T"/> through its <c>ReadFrom</c>: <c>parsed.ToMapped&lt;Header&gt;()</c>.</summary>
    /// <typeparam name="T">A class implementing <see cref="ICStructMapped{TSelf}"/> (hand-written, or generated by <c>[CStructMapped]</c>).</typeparam>
    /// <returns>The mapped instance.</returns>
    public T ToMapped<T>()
        where T : ICStructMapped<T>
        => T.ReadFrom(this);

    /// <summary>
    ///     Reads a member, or a nested value below it, as <typeparamref name="T"/>; returns <see langword="false"/>
    ///     instead of throwing when the path selects nothing or the value does not convert.
    /// </summary>
    /// <typeparam name="T">The destination type: a scalar, string, enum, array, <see cref="StructValue"/>, <see cref="UnionValue"/>, <see cref="Pointer"/>, or a class implementing <see cref="ICStructMapped{TSelf}"/>.</typeparam>
    /// <param name="path">A member name, or a dotted and indexed path relative to this struct.</param>
    /// <param name="value">The converted value, or <see langword="default"/> when the method returns <see langword="false"/>.</param>
    /// <returns><see langword="true"/> when the path resolved and the value converted.</returns>
    public bool TryGet<T>(string path, [MaybeNullWhen(false)] out T value)
    {
        return ValuePath.TryGet(this, path, describeFailure: false, out value, out _);
    }

    /// <summary>
    ///     The non-throwing read that says why it failed: <paramref name="failure"/> is the <see cref="CStructPathException"/>
    ///     <see cref="Get{T}"/> raises for a member that is not there or the <see cref="CStructReadException"/> it raises
    ///     for a value that does not convert to <typeparamref name="T"/>, unthrown.
    /// </summary>
    /// <typeparam name="T">The requested type.</typeparam>
    /// <param name="path">The member name or nested path.</param>
    /// <param name="value">The converted value, or the default when the read failed.</param>
    /// <param name="failure">The failure, or <see langword="null"/>.</param>
    /// <returns>Whether <paramref name="value"/> holds the member.</returns>
    public bool TryGet<T>(string path, [MaybeNullWhen(false)] out T value, out CStructException? failure)
    {
        return ValuePath.TryGet(this, path, describeFailure: true, out value, out failure);
    }

    /// <summary>
    ///     <see cref="Get{T}"/> with a fallback: <paramref name="fallback"/> when the member is not there, holds
    ///     <see langword="null"/> where <typeparamref name="T"/> cannot, or does not convert; the value otherwise.
    /// </summary>
    /// <typeparam name="T">The requested type.</typeparam>
    /// <param name="path">The member name or nested path.</param>
    /// <param name="fallback">The value to return when the member cannot be read as <typeparamref name="T"/>.</param>
    /// <returns>The member or <paramref name="fallback"/>.</returns>
    public T GetOrDefault<T>(string path, T fallback)
        => this.TryGet(path, out T? value, out _) ? value! : fallback;

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
        this.highestSlot = -1;
    }

    /// <summary>Enumerates members in insertion order without allocating.</summary>
    /// <returns>A value-type enumerator over the present members.</returns>
    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    /// <summary>Enumerates the present members in insertion order through the boxed interface.</summary>
    /// <returns>A boxed <see cref="Enumerator"/>.</returns>
    IEnumerator<KeyValuePair<string, object?>> IEnumerable<KeyValuePair<string, object?>>.GetEnumerator()
    {
        return this.GetEnumerator();
    }

    /// <summary>Enumerates the present members in insertion order for non-generic callers.</summary>
    /// <returns>A boxed <see cref="Enumerator"/> yielding <see cref="KeyValuePair{TKey, TValue}"/> items.</returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return this.GetEnumerator();
    }

    /// <summary>Adds a member from a key/value pair, as <see cref="Add(string, object?)"/> does.</summary>
    /// <param name="item">The member name and value.</param>
    /// <exception cref="ArgumentException">A member with the same name is already present.</exception>
    void ICollection<KeyValuePair<string, object?>>.Add(KeyValuePair<string, object?> item)
    {
        this.Add(item.Key, item.Value);
    }

    /// <summary>
    ///     Returns whether a member with the pair's name is present and its value equals the pair's value.
    /// </summary>
    /// <param name="item">The member name and value to look for.</param>
    /// <returns><see langword="true"/> when both the name and the value match.</returns>
    bool ICollection<KeyValuePair<string, object?>>.Contains(KeyValuePair<string, object?> item)
    {
        return this.TryGetValue(item.Key, out object? value) && Equals(value, item.Value);
    }

    /// <summary>Copies the present members, in insertion order, into <paramref name="array"/>.</summary>
    /// <param name="array">
    ///     The destination; it must have room for <see cref="Count"/> pairs after <paramref name="arrayIndex"/>.
    /// </param>
    /// <param name="arrayIndex">The destination start index.</param>
    /// <exception cref="ArgumentNullException"><paramref name="array"/> is <see langword="null"/>.</exception>
    void ICollection<KeyValuePair<string, object?>>.CopyTo(KeyValuePair<string, object?>[] array, int arrayIndex)
    {
        ArgumentNullException.ThrowIfNull(array);
        foreach (KeyValuePair<string, object?> pair in this.EnumeratePairs())
        {
            array[arrayIndex++] = pair;
        }
    }

    /// <summary>Removes a member only when its value equals the pair's value.</summary>
    /// <param name="item">The member name and the value it must hold.</param>
    /// <returns><see langword="true"/> when the member matched and has been removed.</returns>
    bool ICollection<KeyValuePair<string, object?>>.Remove(KeyValuePair<string, object?> item)
    {
        return this.TryGetValue(item.Key, out object? value) && Equals(value, item.Value) && this.Remove(item.Key);
    }

    /// <summary>
    ///     Binds <see langword="dynamic"/> member access (<c>parsed.length</c>) directly to this value's shape, so a
    ///     call site compiled once serves every struct of the same shape. Names outside the shape fall back to the
    ///     runtime lookup; anything else (methods, conversions, indexers) binds to the ordinary members of this class.
    /// </summary>
    /// <param name="parameter">The expression representing this value at the call site.</param>
    /// <returns>The meta-object that binds member reads and writes to this value's shape.</returns>
    DynamicMetaObject IDynamicMetaObjectProvider.GetMetaObject(Expression parameter)
    {
        return new MetaStructValue(parameter, this);
    }

    /// <summary>Lists the members present, for debugging.</summary>
    /// <returns>A <c>{ name = value, … }</c> rendering of the members.</returns>
    public override string ToString()
    {
        return "{ " + string.Join(", ", this.EnumeratePairs().Select(pair => pair.Key + " = " + (pair.Value ?? "null"))) + " }";
    }

    /// <summary>Slot read for bound call sites; false when the member is absent so the binder's fallback throws.</summary>
    /// <param name="index">The member's slot index in this value's shape.</param>
    /// <param name="value">The member value when present; otherwise <see langword="null"/>.</param>
    /// <returns><see langword="true"/> when the slot holds a member.</returns>
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
    /// <param name="index">The member's slot index in this value's shape.</param>
    /// <param name="value">The member value to store; the member count grows by one.</param>
    internal void SetFreshSlot(int index, object? value)
    {
        this.slots[index] = value;
        this.count++;
        this.highestSlot = Math.Max(this.highestSlot, index);
    }

    /// <summary>
    ///     Stores a member by slot exactly as storing it by name does: a new member is counted and keeps its insertion
    ///     position, a present one is replaced in place. The compiled engine stores every member value through here.
    /// </summary>
    /// <param name="index">The member's slot index in this value's shape.</param>
    /// <param name="value">The member value.</param>
    internal void StoreSlot(int index, object? value) => this.SetSlot(index, value);

    /// <summary>Slot write for bound call sites.</summary>
    private object? SetSlot(int index, object? value)
    {
        if (ReferenceEquals(this.slots[index], Unset))
        {
            this.count++;
            if (this.insertionOrder is null && index > this.highestSlot)
            {
                // Above every present slot: shape order is still insertion order (TrackInsertion's first case, inlined).
                this.highestSlot = index;
            }
            else
            {
                this.TrackInsertion(this.shape.Names[index], index);
            }
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
            if (index > this.highestSlot)
            {
                // Above every present slot: shape order is still insertion order.
                this.highestSlot = index;
                return;
            }

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

        /// <summary>Starts before the first member of <paramref name="owner"/>.</summary>
        /// <param name="owner">
        ///     The value whose members are enumerated; it should not be changed while the enumeration runs.
        /// </param>
        internal Enumerator(StructValue owner)
        {
            this.owner = owner;
            this.order = owner.insertionOrder;
            this.index = -1;
            this.Current = default;
        }

        /// <summary>Gets the member at the current position.</summary>
        public KeyValuePair<string, object?> Current { get; private set; }

        /// <summary>
        ///     Gets the member at the current position as a boxed <see cref="KeyValuePair{TKey, TValue}"/>.
        /// </summary>
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
        private static readonly MethodInfo TryGetValueMethod = typeof(StructValue).GetMethod(nameof(TryGetValue), BindingFlags.Instance | BindingFlags.Public)!;
        private static readonly MethodInfo SetMethod = typeof(StructValue).GetMethod(nameof(Set), BindingFlags.Instance | BindingFlags.NonPublic)!;
        private static readonly MethodInfo RemoveMethod = typeof(StructValue).GetMethod(nameof(Remove), BindingFlags.Instance | BindingFlags.Public, [typeof(string)])!;
        private static readonly FieldInfo ShapeField = typeof(StructValue).GetField(nameof(shape), BindingFlags.Instance | BindingFlags.NonPublic)!;

        public MetaStructValue(Expression expression, StructValue value)
            : base(expression, BindingRestrictions.Empty, value)
        {
        }

        private StructValue Target => (StructValue)this.Value!;

        private Expression Self => Expression.Convert(this.Expression, typeof(StructValue));

        public override DynamicMetaObject BindGetMember(GetMemberBinder binder)
        {
            ParameterExpression value = Expression.Variable(typeof(object), "value");
            DynamicMetaObject missing = binder.FallbackGetMember(this);
            Expression found = this.Target.shape.TryGetIndex(binder.Name, out int index)
                ? Expression.Call(this.Self, TryGetSlotMethod, Expression.Constant(index), value)
                : Expression.Call(this.Self, TryGetValueMethod, Expression.Constant(binder.Name), value);
            Expression body = Expression.Block(
                [value],
                Expression.Condition(found, value, Expression.Convert(missing.Expression, typeof(object))));
            return new DynamicMetaObject(body, this.ShapeRestrictions().Merge(missing.Restrictions));
        }

        public override DynamicMetaObject BindSetMember(SetMemberBinder binder, DynamicMetaObject value)
        {
            Expression converted = Expression.Convert(value.Expression, typeof(object));
            Expression body = this.Target.shape.TryGetIndex(binder.Name, out int index)
                ? Expression.Call(this.Self, SetSlotMethod, Expression.Constant(index), converted)
                : Expression.Block(
                    Expression.Call(this.Self, SetMethod, Expression.Constant(binder.Name), converted),
                    converted);
            return new DynamicMetaObject(body, this.ShapeRestrictions().Merge(value.Restrictions));
        }

        public override DynamicMetaObject BindDeleteMember(DeleteMemberBinder binder)
        {
            DynamicMetaObject missing = binder.FallbackDeleteMember(this);
            Expression body = Expression.Condition(
                Expression.Call(this.Self, RemoveMethod, Expression.Constant(binder.Name)),
                Expression.Empty(),
                missing.Expression);
            return new DynamicMetaObject(body, this.ShapeRestrictions().Merge(missing.Restrictions));
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
