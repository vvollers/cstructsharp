namespace CStructSharp.Compilation.Programs;

using System.Collections.Generic;

/// <summary>
///     What the shared struct walk (<see cref="StructProgramCompiler{TBuilder}"/>) needs from a read or write program
///     builder: the members, how they are placed, the conditional scope, and the structural steps it emits.
/// </summary>
internal interface IStructProgramBuilder
{
    /// <summary>Gets the members, indexed by the steps' field.</summary>
    CompiledField[] Fields { get; }

    /// <summary>Gets a value indicating whether the members are placed by a runtime placement cursor (a struct with bitfields).</summary>
    bool UsesPlacementCursor { get; }

    /// <summary>Gets or sets the composite's conditional scope in slot terms, or <see langword="null"/> when it has none.</summary>
    ConditionalScopeSlots? Scope { get; set; }

    /// <summary>Gets the slot table the programs index.</summary>
    SlotTable Table { get; }

    /// <summary>Adds the branch a member's arm needs, registering its decision the first time.</summary>
    /// <param name="branch">The compiled branch.</param>
    /// <returns>The branch's index.</returns>
    int AddBranch(CompiledConditionalBranch branch);

    /// <summary>Appends a structural step as the builder's own opcode of the same name.</summary>
    /// <param name="step">The step.</param>
    /// <param name="field">The member, or -1.</param>
    /// <param name="a">The first operand.</param>
    /// <param name="b">The second operand.</param>
    /// <returns>The step's index.</returns>
    int EmitStructural(StructuralStep step, int field, int a, int b);

    /// <summary>Points a member's arm steps at the step after it, where an unselected member continues.</summary>
    /// <param name="selections">The indexes of the member's <see cref="StructuralStep.SelectArm"/> steps.</param>
    void PatchSkipTargets(List<int> selections);
}
