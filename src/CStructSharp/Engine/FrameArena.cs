namespace CStructSharp.Engine;

using System;
using System.Collections.Generic;
using CStructSharp.Compilation.Programs;
using CStructSharp.Expressions;

/// <summary>
///     The two stacks one compiled-engine operation keeps its frames' conditional state in, and the steps that use them:
///     each frame's selected conditional arms and conditional-scope locals. A frame takes a range on entry and gives it
///     back when it completes, so frames nest like the calls that run them; a failed operation abandons its ranges and
///     <see cref="Release"/> returns the stacks, cleared, to the thread's spares when the operation ends. Shared by the
///     reader (<see cref="ReadEngineState"/>) and the writer (<see cref="WriteEngineState"/>), because both select arms and
///     keep scopes by the same rules.
/// </summary>
/// <remarks>A mutable struct held by one operation's state and changed only through it.</remarks>
internal struct FrameArena
{
    /// <summary>The selected-arm value of a conditional group whose selector the frame has not evaluated yet.</summary>
    public const int Undecided = int.MinValue;

    // The thread's spare stacks, taken for the length of an operation; a nested operation on the same thread (from a
    // callback) finds them taken and allocates its own.
    [ThreadStatic]
    private static int[]? spareArms;

    [ThreadStatic]
    private static SlotValue[]? spareLocals;

    private int[]? arms;
    private int armTop;
    private SlotValue[]? locals;
    private int localTop;
    private int localHigh;

    /// <summary>Gets the selected-arm stack; a frame reads its arms at the base <see cref="TakeArms"/> returned. Read it again after a nested frame ran, which may have grown it.</summary>
    public readonly int[] Arms => this.arms!;

    /// <summary>Gets the conditional-scope locals stack; a frame reads its locals at the base <see cref="TakeLocals"/> returned. Read it again after a nested frame ran.</summary>
    public readonly SlotValue[] Locals => this.locals!;

    /// <summary>
    ///     Returns the arm a conditional branch's group selected in a frame, evaluating the group's selector the first time
    ///     the frame needs it, as <c>ConditionalFieldSelection</c> does once per composite instance.
    /// </summary>
    /// <param name="arena">The operation's arena.</param>
    /// <param name="slots">The operation's variables.</param>
    /// <param name="groups">The composite's conditional groups.</param>
    /// <param name="expressions">The program's expressions, which hold the selectors.</param>
    /// <param name="contexts">The expressions' failure contexts.</param>
    /// <param name="branch">The branch being tested.</param>
    /// <param name="armBase">The base of the frame's selected arms in the arena.</param>
    /// <param name="domain">Whether a selector failure is a read or a write failure.</param>
    /// <returns>The selected arm of the branch's group.</returns>
    /// <exception cref="Diagnostics.CStructException">The selector cannot be evaluated.</exception>
    public static int SelectedArm(ref FrameArena arena, VariableSlots slots, ReadProgram.ConditionalGroup[] groups, ProgramExpression[] expressions, string[] contexts, ReadProgram.ConditionalBranch branch, int armBase, ExpressionFailureDomain domain)
    {
        int arm = arena.Arms[armBase + branch.Group];
        if (arm == Undecided)
        {
            ReadProgram.ConditionalGroup group = groups[branch.Group];
            Int128 value = slots.Evaluate(expressions[group.Selector], contexts[group.Selector], domain);
            arm = group.Decision.SelectArm(value);
            arena.Arms[armBase + branch.Group] = arm;
        }

        return arm;
    }

    /// <summary>
    ///     After an active member of a conditional composite, saves the member's own names into the frame's locals, then
    ///     restores the composite's names a nested declaration replaced (an absent saved value removes the name), in the
    ///     order <c>ConditionalVariableScope.CompleteField</c> uses.
    /// </summary>
    /// <param name="arena">The operation's arena.</param>
    /// <param name="slots">The operation's variables.</param>
    /// <param name="scope">The composite's scope in slot terms.</param>
    /// <param name="member">The member's index.</param>
    /// <param name="localBase">The base of the frame's saved values in the arena.</param>
    public static void CompleteMember(ref FrameArena arena, VariableSlots slots, ReadConditionalScope scope, int member, int localBase)
    {
        SlotValue[] saved = arena.Locals;
        IReadOnlyList<int> captured = scope.GetCaptured(member);
        for (int index = 0; index < captured.Count; index++)
        {
            int local = captured[index];
            saved[localBase + local] = slots.Get(scope.LocalSlots[local]);
        }

        IReadOnlyList<int> restored = scope.GetRestored(member);
        for (int index = 0; index < restored.Count; index++)
        {
            int local = restored[index];
            slots.Set(scope.LocalSlots[local], saved[localBase + local]);
        }
    }

    /// <summary>Takes a frame's selected arms, every group <see cref="Undecided"/>.</summary>
    /// <param name="count">The frame's conditional group count, positive.</param>
    /// <returns>The index of the frame's first arm in <see cref="Arms"/>.</returns>
    public int TakeArms(int count)
    {
        int start = this.armTop;
        int end = start + count;
        if (this.arms is null || this.arms.Length < end)
        {
            this.arms = Grow(this.arms, ref spareArms, end);
        }

        Array.Fill(this.arms, Undecided, start, count);
        this.armTop = end;
        return start;
    }

    /// <summary>Gives back the arms a completed frame took.</summary>
    /// <param name="start">The base <see cref="TakeArms"/> returned.</param>
    public void ReleaseArms(int start) => this.armTop = start;

    /// <summary>Takes a frame's conditional-scope locals, every one <see cref="SlotValue.Undefined"/> (the interpreter's "no saved value").</summary>
    /// <param name="count">The scope's local count (or a union's slot count), not negative.</param>
    /// <returns>The index of the frame's first local in <see cref="Locals"/>.</returns>
    public int TakeLocals(int count)
    {
        int start = this.localTop;
        int end = start + count;
        if (this.locals is null || this.locals.Length < end)
        {
            this.locals = Grow(this.locals, ref spareLocals, end);
        }

        Array.Clear(this.locals, start, count);
        this.localTop = end;
        this.localHigh = Math.Max(this.localHigh, end);
        return start;
    }

    /// <summary>Gives back the locals a completed frame took.</summary>
    /// <param name="start">The base <see cref="TakeLocals"/> returned.</param>
    public void ReleaseLocals(int start) => this.localTop = start;

    /// <summary>Returns the stacks to the thread's spares at the end of the operation, the locals cleared so no payload stays alive.</summary>
    public void Release()
    {
        if (this.arms is not null)
        {
            spareArms = this.arms;
            this.arms = null;
        }

        if (this.locals is not null)
        {
            Array.Clear(this.locals, 0, this.localHigh);
            spareLocals = this.locals;
            this.locals = null;
        }
    }

    /// <summary>Returns a stack of at least <paramref name="length"/> entries holding the current one's entries: the thread's spare when it is free and large enough, else a new, larger array.</summary>
    /// <typeparam name="T">The entry type.</typeparam>
    /// <param name="current">The stack in use, or <see langword="null"/>.</param>
    /// <param name="spare">The thread's spare of this type; taken when used.</param>
    /// <param name="length">The length needed.</param>
    /// <returns>The stack.</returns>
    private static T[] Grow<T>(T[]? current, ref T[]? spare, int length)
    {
        T[] grown;
        if (current is null && spare is { } free && free.Length >= length)
        {
            grown = free;
            spare = null;
            return grown;
        }

        grown = new T[Math.Max(16, Math.Max(length, (current?.Length ?? 0) * 2))];
        if (current is not null)
        {
            Array.Copy(current, grown, current.Length);
        }

        return grown;
    }
}
