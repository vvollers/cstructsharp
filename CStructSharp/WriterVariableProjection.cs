namespace CStructSharp;

using System;
using CStructSharp.Structure;

/// <summary>Projects a just-written value into the layout expression variable domain.</summary>
internal static class WriterVariableProjection
{
    /// <summary>Stores a just-written scalar value so later array lengths and expressions can use its name.</summary>
    public static void UpdateVariablesFromValue(CStructElementWriterState state, string name, object value)
    {
        try
        {
            if (value is Pointer pointer)
            {
                // Expressions referring to a pointer use its address, not the complex Pointer wrapper.
                state.Variables[name] = new Literal(Convert.ToInt32(pointer.Address));
                return;
            }

            if (value is string str)
            {
                // Existing parser behavior treats a string as an identifier for later expression use.
                state.Variables[name] = new Identifier(str);
                return;
            }

            // Normal scalar values become literal expressions for following array counts and calculations.
            state.Variables[name] = new Literal(Convert.ToInt32(value));
        }
        catch
        {
            // The field still shadows a caller/definition value even when it cannot feed the Int32 expression language.
            state.Variables.Remove(name);
        }
    }
}
