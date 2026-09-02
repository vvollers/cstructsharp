namespace CStructSharp;

using System;
using System.Collections.Generic;
using System.Dynamic;
using System.IO;
using System.Linq;
using CStructSharp.Structure;

/// <summary>Reads a selected nested object without materializing unrelated siblings.</summary>
public partial class CStruct
{
    /// <summary>Restores the expression context so overlapping union views cannot influence one another or later fields.</summary>
    private static void RestoreVariables(
        Dictionary<string, Expr> destination,
        IReadOnlyDictionary<string, Expr> snapshot)
    {
        destination.Clear();
        foreach (KeyValuePair<string, Expr> variable in snapshot)
        {
            destination.Add(variable.Key, variable.Value);
        }
    }

    /// <summary>Requires a semantic target that has reached a concrete struct rather than pointer storage.</summary>
    private static Struct ResolveStructTarget(ResolvedTarget target)
    {
        bool stopsAtPointerStorage = target.Kind == ResolvedTargetKind.PointerAddress ||
                                     ((target.Kind is ResolvedTargetKind.Field or ResolvedTargetKind.ArrayElement) &&
                                      target.EffectiveField?.PointerDepth > 0);
        if (stopsAtPointerStorage ||
            target.RemainingPointerDepth > 0 ||
            target.TargetElement is not Struct strct)
        {
            throw new CStructPathException("The selected path does not resolve to a struct object.");
        }

        return strct;
    }

    /// <summary>Reads one non-union struct through the shared compiled traversal and completes its storage extent.</summary>
    private void ReadCompiledStructInto(
        Struct strct,
        ExpandoObject destination,
        CStructOperationContext state,
        CStructElement[] debugStack)
    {
        state.EnterStructure();
        try
        {
            foreach (CompiledField field in this.GetCompiledComposite(strct).Fields)
            {
                this.HandleCStructElement(
                    field.Declaration,
                    destination,
                    state,
                    debugStack,
                    -1,
                    field.Declaration is Struct,
                    field);
            }
        }
        finally
        {
            state.ExitStructure();
        }

        if (state.CurrentBitOffset > 0)
        {
            state.Stream.Position = state.NextPosition;
            state.CurrentBitOffset = 0;
            state.CurrentBitfieldType = null;
        }

        if (state.Aligned)
        {
            int alignment = this.GetCompiledComposite(strct).Symbol.Alignment;
            state.Stream.Position = this.AlignUp(state.Stream.Position, alignment);
        }

        state.NextPosition = state.Stream.Position;
    }

    /// <summary>Reads one compiled struct or union at an already resolved address.</summary>
    private (object Result, List<DebugData> DebugData) ParseCompiledStructAt(
        CStructOperationContext state,
        long address,
        Struct target,
        CStructElement[] debugPrefix,
        int containingStructureDepth,
        int pointerDereferenceDepth,
        bool debug)
    {
        state.Stream.Position = address;
        state.Debug = debug;
        state.StructureDepth = containingStructureDepth;
        state.PointerDereferenceDepth = pointerDereferenceDepth;

        if (target.IsUnion)
        {
            return (this.ReadUnionValue(target, state, debugPrefix), state.DebugMapping);
        }

        dynamic result = new ExpandoObject();
        this.ReadCompiledStructInto(target, result, state, debugPrefix);

        return (result, state.DebugMapping);
    }

    /// <summary>
    ///     Reads every bounded interpretation of one union from the same address and retains its complete raw storage.
    /// </summary>
    private UnionValue ReadUnionValue(
        Struct union,
        CStructOperationContext state,
        CStructElement[] debugStack)
    {
        long unionPosition = state.Stream.Position;
        int unionSize = this.GetCompiledStructSizeInBytes(
            this.GetCompiledComposite(union),
            state.Variables,
            false);
        long unionEnd = checked(unionPosition + unionSize);
        byte[] rawStorage = new byte[unionSize];

        try
        {
            state.Stream.ReadExactly(rawStorage);
        }
        catch (EndOfStreamException exception)
        {
            throw new CStructReadException("Not enough bytes in stream.", exception);
        }

        state.Stream.Position = unionPosition;
        dynamic decodedMembers = new ExpandoObject();
        bool previousPointerSuppression = state.SuppressPointerDereference;
        var unionInputVariables = new Dictionary<string, Expr>(state.Variables, StringComparer.Ordinal);

        state.EnterStructure();
        try
        {
            // An untagged union does not identify an active member. Decode local views, but never follow an external
            // pointer merely because its address bytes overlap this storage.
            state.SuppressPointerDereference = true;
            foreach (CompiledField field in this.GetCompiledComposite(union).Fields)
            {
                RestoreVariables(state.Variables, unionInputVariables);
                this.HandleCStructElement(
                    field.Declaration,
                    decodedMembers,
                    state,
                    debugStack,
                    unionPosition,
                    field.Declaration is Struct,
                    field);
            }
        }
        finally
        {
            RestoreVariables(state.Variables, unionInputVariables);
            state.SuppressPointerDereference = previousPointerSuppression;
            state.ExitStructure();
            state.Stream.Position = unionEnd;
            state.NextPosition = unionEnd;
            state.CurrentBitOffset = 0;
            state.CurrentBitfieldType = null;
        }

        var memberViews = (IDictionary<string, object?>)decodedMembers;
        UnionValue result = UnionValue.FromParsed(union.Name.Name, rawStorage, memberViews);
        if (state.Debug)
        {
            // This record makes the overlapping extent explicit without rereading and recharging the same bytes.
            state.DebugMapping.Add(
                new DebugData
                {
                    CurPos = unionPosition,
                    EndPos = unionEnd,
                    DebugStack = debugStack,
                    Value = result,
                    Buffer = rawStorage.Select(value => (int)value).ToArray(),
                    TypeName = union.Name.Name,
                });
        }

        return result;
    }
}
