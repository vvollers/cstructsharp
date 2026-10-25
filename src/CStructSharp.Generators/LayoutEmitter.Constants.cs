namespace CStructSharp.Generators;

using System.Collections.Generic;
using System.Globalization;
using CStructSharp.Syntax;

/// <summary>
///     The integer <c>#define</c>s of a layout, emitted as <c>public const</c> members of the generated class so code that
///     reads a header can name the layout's constants (<c>Ext.EXT_MAGIC</c>) instead of repeating their values.
/// </summary>
internal sealed partial class LayoutEmitter
{
    private List<DefineConstant>? defineConstants;

    /// <summary>One integer <c>#define</c> that becomes a constant.</summary>
    /// <param name="LayoutName">The name in the layout.</param>
    /// <param name="Name">The C# member name: the layout spelling, escaped when it is a keyword.</param>
    /// <param name="Type">The C# type: <c>int</c> when the value fits one, otherwise <c>long</c>.</param>
    /// <param name="Value">The value as a C# literal.</param>
    private sealed record DefineConstant(string LayoutName, string Name, string Type, string Value);

    /// <summary>
    ///     The <c>#define</c>s that have a fixed integer value, in declaration order: a define whose body names a caller variable has no
    ///     fixed value, a text define is not an integer, and a value outside the 64-bit range has no constant type.
    /// </summary>
    /// <returns>The constants; empty for most layouts.</returns>
    private List<DefineConstant> DefineConstants()
    {
        if (this.defineConstants is not null)
        {
            return this.defineConstants;
        }

        var constants = new List<DefineConstant>();
        foreach (KeyValuePair<string, CStructElement> element in this.compilation.CStructElements)
        {
            if (element.Value is not Defines define || !this.StaticVariables.TryGetValue(element.Key, out Expr? value) || value is not Literal { IsInDomain: true } literal)
            {
                continue;
            }

            System.Int128 number = literal.Value;
            if (number < long.MinValue || number > long.MaxValue)
            {
                continue;
            }

            bool fitsInt = number >= int.MinValue && number <= int.MaxValue;
            constants.Add(new DefineConstant(define.Name.Name, Naming.ToCSharp(define.Name.Name, keepNames: true), fitsInt ? "int" : "long", ((long)number).ToString(CultureInfo.InvariantCulture)));
        }

        this.defineConstants = constants;
        return constants;
    }

    /// <summary>Emits the constants of the layout's integer <c>#define</c>s.</summary>
    /// <param name="writer">The generated source destination, inside the class.</param>
    private void EmitDefineConstants(SourceWriter writer)
    {
        if (this.request.Shape == GenerationShape.Scalar)
        {
            return;
        }

        foreach (DefineConstant constant in this.DefineConstants())
        {
            writer.Line("/// <summary>The layout's <c>#define " + constant.LayoutName + "</c>.</summary>");
            writer.Line("public const " + constant.Type + " " + constant.Name + " = " + constant.Value + ";");
            writer.Line();
        }
    }
}
