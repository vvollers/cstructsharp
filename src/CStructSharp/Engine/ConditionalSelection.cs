namespace CStructSharp.Engine;

/// <summary>How one conditional member was selected during a <see cref="CapturedLayout"/> capture.</summary>
/// <param name="Member">The member's declaration name.</param>
/// <param name="Position">The cursor position (bytes from the input's byte 0) at which the member was decided, before it was placed.</param>
/// <param name="Active">Whether the member's condition selected it.</param>
internal readonly record struct ConditionalSelection(string Member, long Position, bool Active);
