namespace CStructSharp.Generators.Tests;

using System;
using System.Reflection;
using Microsoft.CodeAnalysis;

/// <summary>
///     Integer <c>#define</c>s of a layout become <c>public const</c> members of the generated class: <c>int</c> when the value fits
///     one, otherwise <c>long</c>; a define that depends on a caller variable has no value and is not emitted; a name that a generated
///     member already uses is the CSG003 error.
/// </summary>
[TestClass]
public class DefineConstantTests
{
    private const string Header = "using CStructSharp;\nnamespace Demo;\n";

    /// <summary>Fixed integer defines are constants with the right type and value, in every surface that has a frame.</summary>
    [TestMethod]
    public void IntegerDefines_BecomeConstants()
    {
        string source = Header + """"
            [CStructLayout("#define EXT_MAGIC 0xEF53\n#define BIG 5000000000\n#define NEGATIVE (-2)\n#define SIZE (4 * 8)\n#define PER (SIZE / 2)\n#define DYNAMIC (COUNT * 2)\n#define HUGE 0xFFFFFFFFFFFFFFFFFF\n#define class 7\nstruct s { uint8 a[SIZE]; uint8 b[COUNT]; };", Root = "s")]
            public static partial class Ext { }

            [CStructLayout("#define LIMIT 9\nstruct s { uint8 a[LIMIT]; };", Root = "s", Surface = GeneratedSurface.Parser)]
            public static partial class Parsing { }
            """";
        Assembly assembly = GeneratorRunner.Run(source).AssertClean().Load();

        Type ext = assembly.GetType("Demo.Ext")!;
        Assert.AreEqual(0xEF53, Constant(ext, "EXT_MAGIC", typeof(int)));
        Assert.AreEqual(5000000000L, Constant(ext, "BIG", typeof(long)));
        Assert.AreEqual(-2, Constant(ext, "NEGATIVE", typeof(int)));
        Assert.AreEqual(32, Constant(ext, "SIZE", typeof(int)));
        Assert.AreEqual(16, Constant(ext, "PER", typeof(int)));
        Assert.AreEqual(7, Constant(ext, "class", typeof(int)));
        Assert.IsNull(ext.GetField("DYNAMIC"), "a define that names a caller variable has no fixed value");
        Assert.IsNull(ext.GetField("HUGE"), "a value beyond 64 bits has no constant type");
        Assert.AreEqual(9, Constant(assembly.GetType("Demo.Parsing")!, "LIMIT", typeof(int)));
    }

    /// <summary>A define named like a generated member is the CSG003 error, which names both owners.</summary>
    [TestMethod]
    public void DefineNamedLikeAGeneratedMember_IsReported()
    {
        string source = Header + """"
            [CStructLayout("#define Parse 3\nstruct s { uint8 a; };", Root = "s")]
            public static partial class Clash { }
            """";
        GeneratorResult result = GeneratorRunner.Run(source);
        Diagnostic diagnostic = Assert.ContainsSingle(result.DiagnosticsWithId("CSG003"));
        StringAssert.Contains(diagnostic.GetMessage(), "#define 'Parse'");
    }

    /// <summary>Reads the value of a public constant, checking its type.</summary>
    /// <param name="type">The generated class.</param>
    /// <param name="name">The constant's name.</param>
    /// <param name="expected">The expected C# type.</param>
    /// <returns>The constant's value.</returns>
    private static object? Constant(Type type, string name, Type expected)
    {
        FieldInfo field = type.GetField(name, BindingFlags.Public | BindingFlags.Static) ?? throw new AssertFailedException("No constant " + name);
        Assert.IsTrue(field.IsLiteral, name);
        Assert.AreEqual(expected, field.FieldType, name);
        return field.GetRawConstantValue();
    }
}
