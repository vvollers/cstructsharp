namespace CStructSharp.Engine;

using System;
using System.Collections.Generic;
using CStructSharp.Compilation.Programs;
using CStructSharp.Expressions;

/// <summary>
///     The conditional state of one compiled-engine operation's frames and the steps that use it: the stack of
///     conditional-scope locals, where a frame takes a range on entry and gives it back when it completes, so frames nest
///     like the calls that run them (a failed operation abandons its ranges and <see cref="Release"/> returns the stack,
///     cleared, to the thread's spare when the operation ends), and the arm selection over the selected arms each frame
///     keeps on its own call stack. Shared by the reader (<see cref="ReadEngineState"/>) and the writer
///     (<see cref="WriteEngineState"/>), because both select arms and keep scopes by the same rules.
/// </summary>
/// <remarks>A mutable struct held by one operation's state and changed only through it.</remarks>
internal struct FrameArena
{
    /// <summary>The selected-arm value of a conditional group whose selector the frame has not evaluated yet.</summary>
    public const int Undecided = int.MinValue;

    /// <summary>The most conditional groups whose selected arms a frame keeps in a fixed stack buffer; a composite with more allocates them.</summary>
    public const int StackArmLimit = 8;

    // The thread's spare locals stack, taken for the length of an operation; a nested operation on the same thread (from
    // a callback) finds it taken and allocates its own.
    [ThreadStatic]
    private static SlotValue[]? spareLocals;

    private SlotValue[]? locals;
    private int localTop;
    private int localHigh;

    /// <summary>Gets the conditional-scope locals stack; a frame reads its locals at the base <see cref="TakeLocals"/> returned. Read it again after a nested frame ran.</summary>
    public readonly SlotValue[] Locals => this.locals!;

    /// <summary>
    ///     Returns the arm a conditional branch's group selected in a frame, evaluating the group's selector the first time
    ///     the frame needs it, as <c>ConditionalFieldSelection</c> does once per composite instance.
    /// </summary>
    /// <param name="frameArms">The frame's selected arms, one per group, <see cref="Undecided"/> until evaluated; the frame keeps them on its call stack.</param>
    /// <param name="slots">The operation's variables.</param>
    /// <param name="groups">The composite's conditional groups.</param>
    /// <param name="expressions">The program's expressions, which hold the selectors.</param>
    /// <param name="contexts">The expressions' failure contexts.</param>
    /// <param name="branch">The branch being tested.</param>
    /// <param name="domain">Whether a selector failure is a read or a write failure.</param>
    /// <returns>The selected arm of the branch's group.</returns>
    /// <exception cref="Diagnostics.CStructException">The selector cannot be evaluated.</exception>
    public static int SelectedArm(Span<int> frameArms, VariableSlots slots, ReadProgram.ConditionalGroup[] groups, ProgramExpression[] expressions, string[] contexts, ReadProgram.ConditionalBranch branch, ExpressionFailureDomain domain)
    {
        int arm = frameArms[branch.Group];
        if (arm == Undecided)
        {
            ReadProgram.ConditionalGroup group = groups[branch.Group];
            Int128 value = slots.Evaluate(expressions[group.Selector], contexts[group.Selector], domain);
            arm = group.Decision.SelectArm(value);
            frameArms[branch.Group] = arm;
        }

        return arm;
    }

    /// <summary>
    ///     After an active member of a conditional composite, saves the member's own names into the frame's locals,
    ///     then restores the composite's names a nested declaration replaced (an absent saved value removes the name).
    /// </summary>
    /// <param name="arena">The operation's arena.</param>
    /// <param name="slots">The operation's variables.</param>
    /// <param name="scope">The composite's scope in slot terms.</param>
    /// <param name="member">The member's index.</param>
    /// <param name="localBase">The base of the frame's saved values in the arena.</param>
    public static void CompleteMember(ref FrameArena arena, VariableSlots slots, ReadConditionalScope scope, int member, int localBase)
    {
        SlotValue[] saved = arena.Locals;
        int[] captured = scope.GetCaptured(member);
        for (int index = 0; index < captured.Length; index++)
        {
            int local = captured[index];
            saved[localBase + local] = slots.Get(scope.LocalSlots[local]);
        }

        int[] restored = scope.GetRestored(member);
        for (int index = 0; index < restored.Length; index++)
        {
            int local = restored[index];
            slots.Set(scope.LocalSlots[local], saved[localBase + local]);
        }
    }

    /// <summary>Takes a frame's conditional-scope locals, every one <see cref="SlotValue.Undefined"/> ("no saved value").</summary>
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

    /// <summary>Returns the locals stack to the thread's spare at the end of the operation, cleared so no payload stays alive.</summary>
    public void Release()
    {
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
