namespace CStructSharp.Compilation.Programs;

/// <summary>One arm a member sits in: the decision and the arm it needs.</summary>
/// <param name="Group">The decision's index in the program's groups and in the frame's selected-arm array.</param>
/// <param name="Arm">The arm: 1 or 0 for an <c>if</c>; a switch's case index, or -1 for <c>default</c>.</param>
internal readonly record struct ProgramBranch(int Group, int Arm);
