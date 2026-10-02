namespace CStructSharp.Generated;

using System;
using CStructSharp.Diagnostics;

/// <summary>Nesting: entering and leaving composites and unions, and following pointers to their targets with the depth, size and cycle rules.</summary>
public ref partial struct ReadCursor
{
    /// <summary>Enters a nested struct or union, enforcing <c>MaxNestingDepth</c>.</summary>
    /// <param name="member">The composite field being entered, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <exception cref="CStructReadLimitException">The nesting limit is exceeded.</exception>
    public void EnterComposite(string member, string? memberType)
    {
        this.settings.CancellationToken.ThrowIfCancellationRequested();
        if (this.nestingDepth >= this.settings.MaxNestingDepth)
        {
            throw this.FailLimit(ReadFailures.NestingLimit, member, memberType);
        }

        this.nestingDepth++;
    }

    /// <summary>Leaves the current struct or union.</summary>
    public void ExitComposite() => this.nestingDepth--;

    /// <summary>Reads a stored pointer address of the layout's width, as a signed stream position.</summary>
    /// <param name="pointerSize">The pointer width in bytes (1, 2, 4, or 8).</param>
    /// <param name="littleEndian">The layout's byte order.</param>
    /// <param name="member">The pointer field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The address; 0 is null.</returns>
    /// <exception cref="CStructReadException">The address does not fit the signed position range.</exception>
    public long TakePointerAddress(int pointerSize, bool littleEndian, string member, string? memberType)
    {
        ulong raw = Codec.ReadUnsigned(this.Take(pointerSize, member, memberType), littleEndian);
        if (raw > long.MaxValue)
        {
            throw this.Fail(ReadFailures.PointerAddressRange, member, memberType, new OverflowException("Pointer address exceeds the signed stream-position range."));
        }

        return (long)raw;
    }

    /// <summary>Marks the start of a union's members; pointers inside are not followed. Pair with <see cref="ExitUnion"/>.</summary>
    public void EnterUnion() => this.unionDepth++;

    /// <summary>Marks the end of a union's members.</summary>
    public void ExitUnion() => this.unionDepth--;

    /// <summary>
    ///     Follows a non-null pointer with the runtime's checks: the pointer depth limit, the resolved address
    ///     (relative to <see cref="Origin"/> when configured) inside the input, the optional target byte budget, and
    ///     a cycle on the active path. Restore the position afterwards with <see cref="ExitPointer"/>.
    /// </summary>
    /// <param name="address">The stored address (non-zero).</param>
    /// <param name="depth">The pointer depth being followed (1 for <c>T *</c>).</param>
    /// <param name="targetSize">The target's fixed size, or <see langword="null"/> for a variable-length target.</param>
    /// <param name="targetType">The pointer field's type spelling, which keys the cycle check.</param>
    /// <param name="member">The pointer field, for the diagnostics.</param>
    /// <param name="memberType">The field's type spelling, for the diagnostics.</param>
    /// <returns>The position to return to.</returns>
    /// <exception cref="CStructReadLimitException">The pointer depth or target byte limit is exceeded.</exception>
    /// <exception cref="CStructReadException">The target lies outside the input or is already being read.</exception>
    public int EnterPointer(long address, int depth, long? targetSize, string targetType, string member, string? memberType)
    {
        this.settings.CancellationToken.ThrowIfCancellationRequested();
        if (this.pointerDepth >= this.settings.MaxPointerDepth)
        {
            throw this.FailLimit(ReadFailures.PointerDepthLimit, member, memberType);
        }

        long target;
        try
        {
            target = this.settings.AddressingMode == PointerAddressingMode.Relative ? checked(address + this.settings.Origin) : address;
        }
        catch (OverflowException exception)
        {
            throw this.Fail(ReadFailures.RelativePointerOverflow, member, memberType, exception);
        }

        if (target < 0 || target >= this.source.Length)
        {
            // A target past a partly buffered source may lie inside the input: read on over more of it. A negative
            // target lies outside any input, so it fails here without buffering more.
            if (target >= 0)
            {
                this.RequireBuffered(target, target + Math.Max(targetSize ?? 1, 1));
            }

            throw this.Fail(ReadFailures.PointerTargetOutside(target), member, memberType);
        }

        if (this.settings.MaxPointerTargetBytes is { } limit)
        {
            if (targetSize is null)
            {
                throw this.FailLimit(ReadFailures.PointerTargetVariableLength, member, memberType);
            }

            if (targetSize.Value > limit)
            {
                throw this.FailLimit(ReadFailures.PointerTargetLimit, member, memberType);
            }
        }

        this.activeTargets ??= new System.Collections.Generic.HashSet<(long, string, int)>();
        if (!this.activeTargets.Add((target, targetType, depth)))
        {
            throw this.Fail(ReadFailures.CyclicPointer(target), member, memberType);
        }

        int resume = this.position;
        this.pointerDepth++;
        this.position = (int)target;
        this.activeTargetStack ??= new System.Collections.Generic.Stack<(long, string, int)>();
        this.activeTargetStack.Push((target, targetType, depth));
        return resume;
    }

    /// <summary>Returns from a pointer target to <paramref name="resume"/>, the value <see cref="EnterPointer"/> returned.</summary>
    /// <param name="resume">The position to return to.</param>
    public void ExitPointer(int resume)
    {
        this.pointerDepth--;
        this.position = resume;
        if (this.activeTargetStack is { Count: > 0 })
        {
            this.activeTargets!.Remove(this.activeTargetStack.Pop());
        }
    }
}
