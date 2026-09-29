namespace CStructSharp;

/// <summary>
///     Which implementation runs the general path of one read or write: the member-by-member interpreter or the
///     compiled engine. Orthogonal to <see cref="ExecutionPath"/>, which chooses between the fast paths and the general
///     path; tests set both through the internal options of <see cref="ReadOptions"/> and <see cref="WriteOptions"/> to
///     compare every combination. <see cref="Engine.EngineSelector"/> makes the one decision per operation.
/// </summary>
/// <remarks>
///     One byte wide, so the field fits the padding of the option records and their snapshots instead of growing every
///     options copy an operation makes.
/// </remarks>
internal enum EngineSelection : byte
{
    /// <summary>The engine runs every operation it supports; the interpreter runs the others.</summary>
    Automatic = 0,

    /// <summary>The interpreter runs every operation; the engine is not consulted.</summary>
    InterpreterOnly = 1,

    /// <summary>
    ///     The engine must run the operation: an operation it declines fails with <see cref="System.InvalidOperationException"/>
    ///     naming the reason, which proves that a case is covered by the engine.
    /// </summary>
    EngineRequired = 2,
}
