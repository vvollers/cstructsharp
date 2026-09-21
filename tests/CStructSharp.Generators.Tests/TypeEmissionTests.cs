namespace CStructSharp.Generators.Tests;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CStructSharp;
using CStructSharp.Generated;
using Microsoft.CodeAnalysis;

/// <summary>The types the generator emits: enums, classes per struct and union, properties typed per the §1.4 table, promoted members, typedef aliases, naming.</summary>
[TestClass]
public class TypeEmissionTests
{
    private const string Header = """
        using CStructSharp;

        namespace Demo;

        """;

    /// <summary>Every accepted declaration shape of the language contract generates, compiles, and has a snapshot.</summary>
    [TestMethod]
    public void ManualFixtures_GenerateCompileAndMatchSnapshots()
    {
        var failures = new List<string>();
        foreach (ManualFixture fixture in ManualFixtures.Load())
        {
            string className = "Fixture" + string.Concat(fixture.Id.Split('-').Select(part => char.ToUpperInvariant(part[0]) + part.Substring(1)));
            string source = Header + "[CStructLayout(" + SourceLiteral(fixture.Definition) + ", " + ManualFixtures.AttributeArguments(fixture) + ")]\npublic static partial class " + className + " { }\n";
            GeneratorResult result = GeneratorRunner.Run(source);
            try
            {
                result.AssertClean();
                Snapshot.Match("Fixture." + fixture.Id, result.Source);
            }
            catch (Exception exception) when (exception is AssertFailedException or InvalidOperationException)
            {
                failures.Add(fixture.Id + ": " + exception.Message);
            }
        }

        Assert.IsEmpty(failures, string.Join("\n\n", failures));
    }

    [TestMethod]
    public void PropertyTypes_FollowTheMappingTable()
    {
        GeneratorResult result = GeneratorRunner.Run(Header + """"
            [CStructLayout("""
                enum color : uint8 { Red = 1, Green };
                flag perms : uint16 { READ = 1, WRITE = 2 };
                struct inner { uint8 z; };
                struct root {
                    uint8 a; int8 b; bool c; char d; wchar e; int16 f; uint16 g; int24 h; uint24 i; int32 j; uint32 k;
                    int48 l; uint48 m; int64 n; uint64 o; int128 p; uint128 q; float16 r; float32 s; float64 t;
                    uleb128_32 u; uleb128_64 v; sleb128_32 w; sleb128_64 x; fixed16_16 y; uuid z1; guid z2;
                    cstring z3; utf8_string_zero z4; string z5;
                    char name[8]; wchar wide[4]; utf8 label[3]; char table[2][3];
                    uint8 bytes[4]; uint16 words[2][2]; uint8 count; uint8 items[count]; uint8 tail[]; uint16 rest[EOF];
                    color colour; perms mode; inner child; inner children[2]; uint8 bits:3; color cbits:2; uint16 wbits:12;
                    uint8 *ptr; uint16 **pp; char *text; void *raw; inner *link; color *pc;
                };
                """)]
            public static partial class Types { }
            """").AssertClean();

        Snapshot.Match("Types.PropertyTypes", result.Source);
        Assembly assembly = result.Load();
        Type root = assembly.GetType("Demo.Types+Root")!;
        var expected = new Dictionary<string, Type>
        {
            ["A"] = typeof(byte),
            ["B"] = typeof(sbyte),
            ["C"] = typeof(bool),
            ["D"] = typeof(char),
            ["E"] = typeof(char),
            ["F"] = typeof(short),
            ["G"] = typeof(ushort),
            ["H"] = typeof(int),
            ["I"] = typeof(uint),
            ["J"] = typeof(int),
            ["K"] = typeof(uint),
            ["L"] = typeof(long),
            ["M"] = typeof(ulong),
            ["N"] = typeof(long),
            ["O"] = typeof(ulong),
            ["P"] = typeof(Int128),
            ["Q"] = typeof(UInt128),
            ["R"] = typeof(Half),
            ["S"] = typeof(float),
            ["T"] = typeof(double),
            ["U"] = typeof(uint),
            ["V"] = typeof(ulong),
            ["W"] = typeof(int),
            ["X"] = typeof(long),
            ["Y"] = typeof(double),
            ["Z1"] = typeof(Guid),
            ["Z2"] = typeof(Guid),
            ["Z3"] = typeof(string),
            ["Z4"] = typeof(string),
            ["Z5"] = typeof(string),
            ["Name"] = typeof(string),
            ["Wide"] = typeof(string),
            ["Label"] = typeof(string),
            ["Table"] = typeof(string[]),
            ["Bytes"] = typeof(byte[]),
            ["Words"] = typeof(ushort[][]),
            ["Count"] = typeof(byte),
            ["Items"] = typeof(byte[]),
            ["Tail"] = typeof(byte[]),
            ["Rest"] = typeof(ushort[]),
            ["Bits"] = typeof(byte),
            ["Wbits"] = typeof(ushort),
            ["Ptr"] = typeof(Pointer<byte>),
            ["Pp"] = typeof(Pointer<Pointer<ushort>>),
            ["Text"] = typeof(Pointer<string>),
            ["Raw"] = typeof(Pointer<object>),
        };
        foreach ((string name, Type type) in expected)
        {
            PropertyInfo? property = root.GetProperty(name);
            Assert.IsNotNull(property, name);
            Assert.AreEqual(type, property.PropertyType, name);
        }

        Type color = assembly.GetType("Demo.Types+Color")!;
        Assert.IsTrue(color.IsEnum);
        Assert.AreEqual(typeof(byte), Enum.GetUnderlyingType(color));
        CollectionAssert.AreEqual(new[] { "Red", "Green" }, Enum.GetNames(color));
        Assert.AreEqual(2, Convert.ToInt32(Enum.Parse(color, "Green")));
        Type perms = assembly.GetType("Demo.Types+Perms")!;
        Assert.IsNotNull(perms.GetCustomAttribute<FlagsAttribute>());
        Assert.AreEqual(typeof(ushort), Enum.GetUnderlyingType(perms));
        CollectionAssert.AreEqual(new[] { "Read", "Write" }, Enum.GetNames(perms));
        Assert.AreEqual(color, root.GetProperty("Colour")!.PropertyType);
        Assert.AreEqual(color, root.GetProperty("Cbits")!.PropertyType);
        Assert.AreEqual(perms, root.GetProperty("Mode")!.PropertyType);
        Type inner = assembly.GetType("Demo.Types+Inner")!;
        Assert.AreEqual(inner, root.GetProperty("Child")!.PropertyType);
        Assert.AreEqual(inner.MakeArrayType(), root.GetProperty("Children")!.PropertyType);
        Assert.AreEqual(typeof(Pointer<>).MakeGenericType(inner), root.GetProperty("Link")!.PropertyType);
        Assert.AreEqual(typeof(Pointer<>).MakeGenericType(color), root.GetProperty("Pc")!.PropertyType);

        object instance = Activator.CreateInstance(root)!;
        Assert.AreEqual(string.Empty, root.GetProperty("Name")!.GetValue(instance));
        Assert.IsNotNull(root.GetProperty("Child")!.GetValue(instance));
        Assert.AreEqual(0, ((Array)root.GetProperty("Bytes")!.GetValue(instance)!).Length);
    }

    [TestMethod]
    public void UnionsPromotedMembersAndInlineTypes_AreSplicedAndNamed()
    {
        GeneratorResult result = GeneratorRunner.Run(Header + """"
            [CStructLayout("""
                struct root {
                    uint32 attributes;
                    union { struct { uint16 ea_size; uint16 reserved; }; uint32 reparse_tag; };
                    union { uint8 narrow; uint16 wide; } choice;
                    struct { uint8 x; uint8 y; } pos;
                    struct { uint8 q; };
                    uint8 name_length;
                };
                """)]
            public static partial class Shapes { }
            """").AssertClean();

        Snapshot.Match("Types.Unions", result.Source);
        Assembly assembly = result.Load();
        Type root = assembly.GetType("Demo.Shapes+Root")!;
        CollectionAssert.AreEquivalent(
            new[] { "Attributes", "EaSize", "Reserved", "ReparseTag", "Choice", "Pos", "Q", "NameLength" },
            root.GetProperties().Select(property => property.Name).ToArray());
        Type choice = assembly.GetType("Demo.Shapes+RootChoice")!;
        CollectionAssert.AreEquivalent(new[] { "SelectedMember", "RawStorage", "Narrow", "Wide" }, choice.GetProperties().Select(property => property.Name).ToArray());
        Assert.AreEqual(typeof(string), choice.GetProperty("SelectedMember")!.PropertyType);
        Assert.AreEqual(typeof(byte[]), choice.GetProperty("RawStorage")!.PropertyType);
        Type pos = assembly.GetType("Demo.Shapes+RootPos")!;
        CollectionAssert.AreEquivalent(new[] { "X", "Y" }, pos.GetProperties().Select(property => property.Name).ToArray());
        Assert.AreEqual(choice, root.GetProperty("Choice")!.PropertyType);
        Assert.AreEqual(pos, root.GetProperty("Pos")!.PropertyType);
    }

    [TestMethod]
    public void TypedefAliases_NameTheClassAndPointerAliasesGetNoType()
    {
        GeneratorResult result = GeneratorRunner.Run(Header + """
            [CStructLayout("typedef struct _X { uint8 a; } X, *PX; typedef struct { uint16 v; } Anon; typedef uint16 word; typedef uint8 pair[2]; struct root { X x; PX p; _X raw; Anon an; word w; pair pr; };")]
            public static partial class Aliases { }
            """).AssertClean();

        Snapshot.Match("Types.Typedefs", result.Source);
        Assembly assembly = result.Load();
        Assert.IsNotNull(assembly.GetType("Demo.Aliases+X"));
        Assert.IsNotNull(assembly.GetType("Demo.Aliases+Anon"));
        Assert.IsNull(assembly.GetType("Demo.Aliases+_X"), "the tag is the alias's class");
        Assert.IsNull(assembly.GetType("Demo.Aliases+PX"), "a pointer alias is not a type");
        Assert.IsNull(assembly.GetType("Demo.Aliases+Word"), "a scalar typedef is not a type");
        Type root = assembly.GetType("Demo.Aliases+Root")!;
        Type x = assembly.GetType("Demo.Aliases+X")!;
        Assert.AreEqual(x, root.GetProperty("X")!.PropertyType);
        Assert.AreEqual(x, root.GetProperty("Raw")!.PropertyType);
        Assert.AreEqual(typeof(Pointer<>).MakeGenericType(x), root.GetProperty("P")!.PropertyType);
        Assert.AreEqual(typeof(ushort), root.GetProperty("W")!.PropertyType);
        Assert.AreEqual(typeof(byte[]), root.GetProperty("Pr")!.PropertyType);
    }

    [TestMethod]
    public void KeepNames_KeepsTheLayoutSpelling_AndMemberCollisionsAreCSG003()
    {
        GeneratorResult kept = GeneratorRunner.Run(Header + """
            [CStructLayout("enum color : uint8 { RED = 1 }; struct my_root { uint8 chunk_type; color event; };", KeepNames = true)]
            public static partial class Kept { }
            """).AssertClean();
        Assembly assembly = kept.Load();
        Type root = assembly.GetType("Demo.Kept+my_root")!;
        Assert.IsNotNull(root.GetProperty("chunk_type"));
        Assert.IsNotNull(root.GetProperty("event"), "the keyword is escaped, not renamed");
        CollectionAssert.AreEqual(new[] { "RED" }, Enum.GetNames(assembly.GetType("Demo.Kept+color")!));

        GeneratorResult members = GeneratorRunner.Run(Header + """
            [CStructLayout("struct root { uint8 a_b; uint8 aB; };")]
            public static partial class Members { }
            """);
        StringAssert.Contains(members.DiagnosticsWithId("CSG003").Single().GetMessage(), "'a_b' and 'aB'");

        GeneratorResult enumMembers = GeneratorRunner.Run(Header + """
            [CStructLayout("enum e : uint8 { a_b = 1, A_b = 2 }; struct root { e v; };")]
            public static partial class EnumMembers { }
            """);
        StringAssert.Contains(enumMembers.DiagnosticsWithId("CSG003").Single().GetMessage(), "Enum members");

        GeneratorResult selfNamed = GeneratorRunner.Run(Header + """
            [CStructLayout("struct root { uint8 root; };")]
            public static partial class SelfNamed { }
            """);
        StringAssert.Contains(selfNamed.DiagnosticsWithId("CSG003").Single().GetMessage(), "the class itself");

        // A composite named like one of the layout class's own members (ReadValue, TryParse, ...) cannot be generated.
        GeneratorResult reservedName = GeneratorRunner.Run(Header + """
            [CStructLayout("struct read_value { uint8 x; }; struct root { read_value v; };")]
            public static partial class Reserved { }
            """);
        StringAssert.Contains(reservedName.DiagnosticsWithId("CSG003").Single().GetMessage(), "ReadValue");

        // The view's own members (Bytes, ToObject, and a fixed array's <Member>Bytes slice) are reserved while views are generated.
        GeneratorResult viewBytes = GeneratorRunner.Run(Header + """
            [CStructLayout("union payload { uint32 word; uint8 bytes[4]; };")]
            public static partial class ViewBytes { }
            """);
        string viewMessage = viewBytes.DiagnosticsWithId("CSG003").Single().GetMessage();
        StringAssert.Contains(viewMessage, "the view's Bytes member");
        StringAssert.Contains(viewMessage, "Views = false");

        GeneratorResult viewSlice = GeneratorRunner.Run(Header + """
            [CStructLayout("struct root { uint8 raw[4]; uint16 raw_bytes; };")]
            public static partial class ViewSlice { }
            """);
        StringAssert.Contains(viewSlice.DiagnosticsWithId("CSG003").Single().GetMessage(), "view slice 'RawBytes'");

        GeneratorResult noViews = GeneratorRunner.Run(Header + """
            [CStructLayout("union payload { uint32 word; uint8 bytes[4]; }; struct root { uint8 raw[4]; uint16 raw_bytes; };", Views = false)]
            public static partial class NoViews { }
            """).AssertClean();
        Assert.IsEmpty(noViews.GeneratorDiagnostics);

        // A member the view cannot expose (after a runtime-sized member) keeps its name: the view has nothing to collide with.
        GeneratorResult unplaced = GeneratorRunner.Run(Header + """
            [CStructLayout("struct root { uint8 count; uint8 items[count]; uint8 bytes[4]; };")]
            public static partial class Unplaced { }
            """).AssertClean();
        Assert.IsEmpty(unplaced.GeneratorDiagnostics);
    }

    private static string SourceLiteral(string definition)
    {
        return "\"" + definition.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    }
}
