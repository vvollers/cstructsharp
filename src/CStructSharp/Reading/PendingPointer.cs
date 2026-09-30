namespace CStructSharp.Reading;

using CStructSharp.Compilation;
using CStructSharp.Compilation.Programs;
using CStructSharp.Diagnostics;
using CStructSharp.Values;

/// <summary>
///     A pointer whose address was read inside a struct and whose target is followed once that struct's last field
///     is read. Following after the struct lets a target's <c>@count(N)</c> name a field declared after the pointer.
/// </summary>
/// <param name="Placeholder">The unresolved pointer already stored in the result; it is resolved in place.</param>
/// <param name="Field">The pointer field, which describes the target type and its optional element count.</param>
/// <param name="DebugStack">The debug path of the pointer field, or <see langword="null"/> for an ordinary read.</param>
/// <param name="AddressEnd">The stream position just after the stored address, where the pointer was read; following starts
/// there so a failure reports the same offset as a pointer followed in place.</param>
/// <param name="Target">The read engine's description of the target, which it follows the pointer with.</param>
internal readonly record struct PendingPointer(Pointer Placeholder, CompiledField Field, DebugPath? DebugStack, long AddressEnd, ReadPointerTarget? Target = null);
