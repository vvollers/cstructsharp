namespace CStructSharp;

using System;
using CStructSharp.Structure;

/// <summary>Projects a just-written value into the layout expression variable domain.</summary>
internal static class WriterVariableProjection
{
    /// <summary>Stores a just-written scalar value so later array lengths and expressions can use its name.</summary>
    public static void UpdateVariablesFromValue(CStructElementWriterState state, string name, object value)
    {
        if (value is Pointer pointer)
        {
            // Expressions referring to a pointer use its address, not the complex Pointer wrapper.
            if (Int32Capture.TryFromInt64(pointer.Address, out int address))
            {
                state.Variables[name] = new Literal(address);
            }
            else
            {
                state.Variables.Remove(name);
            }

            return;
        }

        if (value is string str)
        {
            // Existing parser behavior treats a string as an identifier for later expression use.
            state.Variables[name] = new Identifier(str);
            return;
        }

        // Normal scalar values become literal expressions for following array counts and calculations. The field
        // still shadows a caller/definition value even when it cannot feed the Int32 expression language: this method
        // receives arbitrary caller-supplied POCO/dynamic values (byte arrays, nested objects, out-of-range numbers),
        // and the former Convert.ToInt32 try/catch threw for each of them on every write (E2.6a). Int32Capture keeps
        // the same accept/reject decision without raising.
        if (Int32Capture.TryConvert(value, out int captured))
        {
            state.Variables[name] = new Literal(captured);
        }
        else
        {
            state.Variables.Remove(name);
        }
    }
}
