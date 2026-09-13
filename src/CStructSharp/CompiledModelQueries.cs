namespace CStructSharp;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using CStructSharp.Structure;
using CstructEnum = CStructSharp.Structure.Enum;

/// <summary>
///     Answers runtime lookups against one immutable, fully bound <see cref="CompiledLayoutModel"/>. Every member
///     here is safe only after construction has finished binding the model; the handful of compiled-model queries
///     that are also reachable mid-construction (composite and storage-size resolution, used while a union member's
///     fixed storage is still being validated) remain instance methods on <see cref="CStruct"/> for that reason.
/// </summary>
internal sealed class CompiledModelQueries
{
    private readonly CompiledLayoutModel compiledLayout;
    private ConcurrentDictionary<string, StructShape>? rootShapes;

    public CompiledModelQueries(CompiledLayoutModel compiledLayout)
    {
        this.compiledLayout = compiledLayout;
    }

    /// <summary>
    ///     The single-member shape of the root wrapper object (<c>{ rootName: value }</c>) that every parse of one root
    ///     builds; cached so the wrapper costs one small slot array rather than a dictionary per parse.
    /// </summary>
    public StructShape GetRootShape(string rootName)
    {
        // Created on the first parse rather than at compile time so that compiling a layout stays as cheap as before.
        ConcurrentDictionary<string, StructShape> shapes = this.rootShapes ??
                                                           Interlocked.CompareExchange(ref this.rootShapes, new ConcurrentDictionary<string, StructShape>(StringComparer.Ordinal), null) ??
                                                           this.rootShapes;
        return shapes.TryGetValue(rootName, out StructShape? shape)
                   ? shape
                   : shapes.GetOrAdd(rootName, static name => new StructShape([name]));
    }

    /// <summary>Returns the immutable synthetic field used to execute one exported typedef root.</summary>
    public CompiledField GetCompiledRootField(CStructElement declaration)
    {
        return this.compiledLayout.RootFields.TryGetValue(declaration, out CompiledField? compiled)
                   ? compiled
                   : throw new InvalidOperationException(
                       "Root declaration has no compiled field projection: " + declaration.Name.Name);
    }

    /// <summary>Returns the exact compiled integer model owned by one enum declaration.</summary>
    public CompiledEnumType GetCompiledEnum(CstructEnum enm)
    {
        if (this.compiledLayout.Symbols.TryGetValue(enm.Name.Name, out CompiledTypeReference type) &&
            type.Symbol.Definition is CompiledEnumType compiled)
        {
            return compiled;
        }

        throw new InvalidOperationException("Enum type is not bound: " + enm.Name.Name);
    }

    /// <summary>Returns one exported declaration from the immutable compiled symbol snapshot.</summary>
    public bool TryGetCompiledDeclaration(
        string name,
        [NotNullWhen(true)] out CStructElement? declaration)
    {
        return this.compiledLayout.Declarations.TryGetValue(name, out declaration);
    }

    /// <summary>Returns the first exported struct or union name in source order for convenience overloads.</summary>
    public string GetFirstCompiledStructName()
    {
        foreach (KeyValuePair<string, CStructElement> declaration in this.compiledLayout.OrderedDeclarations)
        {
            if (declaration.Value is Struct)
            {
                return declaration.Key;
            }
        }

        throw new CStructLayoutException("Layout does not contain a root struct declaration.");
    }

    /// <summary>Projects one exported parsed declaration to its already resolved named type, if it has one.</summary>
    public CStructElement? ResolveCompiledNamedElement(CStructElement declaration)
    {
        return declaration switch
        {
            Struct or CstructEnum => declaration,
            Typedef => this.GetCompiledRootField(declaration).NamedElement,
            _ => null,
        };
    }
}
