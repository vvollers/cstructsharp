namespace CStructSharp.Compilation.Programs;

/// <summary>
///     What <see cref="ReadProgramCompiler"/> knows, while it walks a struct's members, about where the read position
///     will be: an <em>anchor</em> the data decides, a guarantee about the anchor's offset from the struct's first byte,
///     and the fixed number of bytes read since the anchor. From it the compiler decides each member's placement: nothing,
///     a relative <see cref="ReadOpCode.Seek"/> over padding whose size is known, or an <see cref="ReadOpCode.Align"/> the
///     executor computes because the padding depends on the data.
/// </summary>
/// <remarks>
///     <para>
///         The rule it reproduces is the runtime's <see cref="PlacementCursor"/>: a member of an aligned layout starts at
///         the next multiple of its alignment counted from the struct's first byte (D-18), and the struct ends at the next
///         multiple of the struct's alignment. With the anchor's offset a multiple of <c>g</c> and <c>d</c> bytes read
///         since, a member aligned to <c>a</c> that divides <c>g</c> starts at <c>anchor + AlignUp(d, a)</c>: its padding
///         is known. Otherwise the executor aligns, and the aligned position becomes the new anchor with guarantee
///         <c>a</c>. A guarantee of 0 means the anchor <em>is</em> the struct's first byte, so the offset is known exactly
///         (0 is a multiple of every alignment, which the arithmetic below gets for free: <c>0 % a == 0</c> and
///         <c>gcd(0, x) == x</c>).
///     </para>
///     <para>
///         A member whose size the data decides ends at a new anchor. Its guarantee is what the data cannot change: an
///         array of <c>n</c> elements of a fixed size <c>s</c> ends a multiple of <c>s</c> after its start, and a struct in
///         an aligned layout is padded to a multiple of its own alignment; anything else (terminated text, LEB128) gives
///         no guarantee (1). A conditional member ends either where it started (not selected) or after itself; the two
///         states merge into their common guarantee.
///     </para>
///     <para>A packed layout never pads, so no member needs a placement step there.</para>
/// </remarks>
internal struct ReadPlacement
{
    private readonly bool aligned;
    private long anchor;
    private long delta;

    /// <summary>Starts at a struct's first byte, whose offset is known to be 0.</summary>
    /// <param name="aligned">Whether the layout applies the portable alignment rules.</param>
    public ReadPlacement(bool aligned)
    {
        this.aligned = aligned;
        this.anchor = 0;
        this.delta = 0;
    }

    /// <summary>Gets the position's offset from the struct's first byte, or <see langword="null"/> when the data decides it.</summary>
    public readonly long? KnownOffset => this.anchor == 0 ? this.delta : null;

    /// <summary>
    ///     Gets a number the position's offset from the struct's first byte is always a multiple of: the greatest common
    ///     divisor of the anchor's guarantee and the bytes read since. 0 means the offset is exactly 0.
    /// </summary>
    public readonly long Guarantee => Gcd(this.anchor, this.delta);

    /// <summary>
    ///     The state after a conditional member: where the member left it when selected, or unchanged when not. Equal
    ///     states stay as they are; different ones continue from an anchor whose guarantee holds for both.
    /// </summary>
    /// <param name="skipped">The state before the member, which stands when the member is not selected.</param>
    /// <param name="read">The state after the member was read.</param>
    /// <returns>The merged state.</returns>
    public static ReadPlacement Merge(ReadPlacement skipped, ReadPlacement read)
    {
        if (skipped.anchor == read.anchor && skipped.delta == read.delta)
        {
            return read;
        }

        long common = Gcd(skipped.Guarantee, read.Guarantee);
        return new ReadPlacement(read.aligned) { anchor = common == 0 ? 1 : common, delta = 0, };
    }

    /// <summary>
    ///     Places a member aligned to <paramref name="alignment"/> and returns the step that moves the position there, if
    ///     any: none when no padding is needed, a <see cref="ReadOpCode.Seek"/> over known padding, or an
    ///     <see cref="ReadOpCode.Align"/> when the padding depends on the data.
    /// </summary>
    /// <param name="field">The member's index, the step's <see cref="ReadStep.Field"/>.</param>
    /// <param name="alignment">The member's alignment in bytes.</param>
    /// <param name="step">The placement step, when one is needed.</param>
    /// <returns>Whether a placement step is needed.</returns>
    public bool Place(int field, int alignment, out ReadStep step)
    {
        step = default;
        if (!this.aligned || alignment <= 1)
        {
            return false;
        }

        if (this.anchor % alignment == 0)
        {
            long start = LayoutMath.AlignUp(this.delta, (long)alignment);
            long padding = start - this.delta;
            this.delta = start;
            if (padding == 0)
            {
                return false;
            }

            step = new ReadStep(ReadOpCode.Seek, field, checked((int)padding), 0);
            return true;
        }

        this.anchor = alignment;
        this.delta = 0;
        step = new ReadStep(ReadOpCode.Align, field, alignment, 0);
        return true;
    }

    /// <summary>Records a member of known size, read from the placed position.</summary>
    /// <param name="size">The member's storage size in bytes.</param>
    public void Advance(long size)
    {
        this.delta = checked(this.delta + size);
    }

    /// <summary>Records a member whose size the data decides: the position after it is a new anchor.</summary>
    /// <param name="unit">A size the member's extent is always a multiple of (1 when there is none).</param>
    public void Restart(long unit)
    {
        long guarantee = Gcd(this.Guarantee, unit);
        this.anchor = guarantee == 0 ? 1 : guarantee;
        this.delta = 0;
    }

    /// <summary>
    ///     The tail padding that ends the struct: known when the struct's alignment divides the anchor's guarantee (or
    ///     the layout is packed, where there is none).
    /// </summary>
    /// <param name="compositeAlignment">The struct's alignment in bytes.</param>
    /// <param name="padding">The tail padding in bytes, when known.</param>
    /// <returns>Whether the tail padding is known; otherwise the executor aligns to <paramref name="compositeAlignment"/>.</returns>
    public readonly bool TryFinish(int compositeAlignment, out int padding)
    {
        padding = 0;
        if (!this.aligned || compositeAlignment <= 1)
        {
            return true;
        }

        if (this.anchor % compositeAlignment != 0)
        {
            return false;
        }

        padding = checked((int)(LayoutMath.AlignUp(this.delta, (long)compositeAlignment) - this.delta));
        return true;
    }

    /// <summary>The greatest common divisor, with <c>gcd(0, x) == x</c>.</summary>
    /// <param name="left">The first non-negative number.</param>
    /// <param name="right">The second non-negative number.</param>
    /// <returns>The divisor; 0 only when both are 0.</returns>
    private static long Gcd(long left, long right)
    {
        while (right != 0)
        {
            long remainder = left % right;
            left = right;
            right = remainder;
        }

        return left;
    }
}
