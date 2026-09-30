namespace CStructSharp.Tests;

/// <summary>
///     Golden dumps of small read programs (<see cref="ReadProgramDump"/>), so a change to the step format or to what a
///     layout compiles to shows up as a readable diff. When a change is intended, replace the expected text with the
///     dump the failure prints and check each changed step against the golden outcomes and the layout's specification.
/// </summary>
[TestClass]
public class ReadProgramDumpTests
{
    /// <summary>
    ///     A <c>packet</c> layout that exercises the main step kinds: fixed scalars, count-sized arrays, text, an
    ///     if/else on a field value and a terminated string.
    /// </summary>
    private const string Packet = "struct packet { uint32 id; uint16 count; int32 samples[count]; uint8 name_length; char name[name_length]; uint8 kind; " +
                                  "if (kind == 1) { float64 value; } else { uint32 code; } cstring note; };";

    /// <summary>The <c>packet</c> layout, packed: no placement steps, counts before their arrays, a lazy if/else.</summary>
    [TestMethod]
    public void Packet_Packed()
    {
        const string expected = """
                                root packet
                                    0  ReadRootStruct               -            packet

                                struct packet
                                    0  EnterConditionalScope        -            clear count, name_length, kind
                                    1  ReadUInt32Le                 id           UInt32 le
                                    2  ReadUInt16Le                 count        UInt16 le
                                    3  CaptureInteger               count        -> count
                                    4  CompleteMember               count        save [count] restore []
                                    5  EvaluateCount                samples      count = count
                                    6  ReadNumericArray             samples      Int32 le
                                    7  ReadUInt8                    name_length  UInt8
                                    8  CaptureInteger               name_length  -> name_length
                                    9  CompleteMember               name_length  save [name_length] restore []
                                   10  EvaluateCount                name         count = name_length
                                   11  ReadCharArray                name         Char
                                   12  ReadUInt8                    kind         UInt8
                                   13  CaptureInteger               kind         -> kind
                                   14  CompleteMember               kind         save [kind] restore []
                                   15  SelectArm                    -            group 0 ((kind == 1)) arm 1, else -> 17
                                   16  ReadFloat64Le                value        Float64 le
                                   17  SelectArm                    -            group 0 ((kind == 1)) arm 0, else -> 19
                                   18  ReadUInt32Le                 code         UInt32 le
                                   19  ReadTerminatedText           note         TerminatedAscii
                                   20  FinishComposite              -            tail +0

                                """;
        AssertDump(expected, new CStruct(Packet), "packet");
    }

    /// <summary>
    ///     The packet's debug program: the ordinary steps, each value read between a mark and a record, the arrays read
    ///     element by element with a record each, every member named first, and each conditional member traced around
    ///     its selection.
    /// </summary>
    [TestMethod]
    public void Packet_Debug()
    {
        const string expected = """
                                root packet
                                    0  DebugRootStruct              -            packet

                                struct packet
                                    0  EnterConditionalScope        -            clear count, name_length, kind
                                    1  DebugMember                  id
                                    2  DebugMark                    id
                                    3  ReadUInt32Le                 id           UInt32 le
                                    4  DebugRecord                  id           Value
                                    5  DebugMember                  count
                                    6  DebugMark                    count
                                    7  ReadUInt16Le                 count        UInt16 le
                                    8  DebugRecord                  count        Value
                                    9  CaptureInteger               count        -> count
                                   10  CompleteMember               count        save [count] restore []
                                   11  DebugMember                  samples
                                   12  EvaluateCount                samples      count = count
                                   13  DebugNumericElements         samples      Int32 le
                                   14  DebugMember                  name_length
                                   15  DebugMark                    name_length
                                   16  ReadUInt8                    name_length  UInt8
                                   17  DebugRecord                  name_length  Value
                                   18  CaptureInteger               name_length  -> name_length
                                   19  CompleteMember               name_length  save [name_length] restore []
                                   20  DebugMember                  name
                                   21  EvaluateCount                name         count = name_length
                                   22  DebugCharArray               name         Char
                                   23  DebugMember                  kind
                                   24  DebugMark                    kind
                                   25  ReadUInt8                    kind         UInt8
                                   26  DebugRecord                  kind         Value
                                   27  CaptureInteger               kind         -> kind
                                   28  CompleteMember               kind         save [kind] restore []
                                   29  DebugCondition               value
                                   30  SelectArm                    -            group 0 ((kind == 1)) arm 1, else -> 36
                                   31  DebugConditionActive         value
                                   32  DebugMember                  value
                                   33  DebugMark                    value
                                   34  ReadFloat64Le                value        Float64 le
                                   35  DebugRecord                  value        Value
                                   36  DebugCondition               code
                                   37  SelectArm                    -            group 0 ((kind == 1)) arm 0, else -> 43
                                   38  DebugConditionActive         code
                                   39  DebugMember                  code
                                   40  DebugMark                    code
                                   41  ReadUInt32Le                 code         UInt32 le
                                   42  DebugRecord                  code         Value
                                   43  DebugMember                  note
                                   44  DebugMark                    note
                                   45  ReadTerminatedText           note         TerminatedAscii
                                   46  DebugRecord                  note         Value
                                   47  FinishComposite              -            tail +0

                                """;
        string actual = ReadProgramDump.RenderDebugRoot(new CStruct(Packet), "packet");
        Assert.AreEqual(expected.ReplaceLineEndings("\n"), actual, "actual dump:\n" + actual);
    }

    /// <summary>The same layout aligned: known padding is a seek, padding after data-sized members an align, and so is the tail.</summary>
    [TestMethod]
    public void Packet_Aligned()
    {
        const string expected = """
                                root packet
                                    0  ReadRootStruct               -            packet

                                struct packet
                                    0  EnterConditionalScope        -            clear count, name_length, kind
                                    1  ReadUInt32Le                 id           UInt32 le
                                    2  ReadUInt16Le                 count        UInt16 le
                                    3  CaptureInteger               count        -> count
                                    4  CompleteMember               count        save [count] restore []
                                    5  EvaluateCount                samples      count = count
                                    6  Seek                         samples      +2
                                    7  ReadNumericArray             samples      Int32 le
                                    8  ReadUInt8                    name_length  UInt8
                                    9  CaptureInteger               name_length  -> name_length
                                   10  CompleteMember               name_length  save [name_length] restore []
                                   11  EvaluateCount                name         count = name_length
                                   12  ReadCharArray                name         Char
                                   13  ReadUInt8                    kind         UInt8
                                   14  CaptureInteger               kind         -> kind
                                   15  CompleteMember               kind         save [kind] restore []
                                   16  SelectArm                    -            group 0 ((kind == 1)) arm 1, else -> 19
                                   17  Align                        value        to 8
                                   18  ReadFloat64Le                value        Float64 le
                                   19  SelectArm                    -            group 0 ((kind == 1)) arm 0, else -> 22
                                   20  Align                        code         to 4
                                   21  ReadUInt32Le                 code         UInt32 le
                                   22  ReadTerminatedText           note         TerminatedAscii
                                   23  FinishComposite              -            tail to 8

                                """;
        AssertDump(expected, new CStruct(Packet, aligned: true), "packet");
    }

    /// <summary>
    ///     Nested structs: a shared named program with its qualified prefix and publication, an inline named struct, an
    ///     anonymous promoted member compiled into its parent, and a struct array.
    /// </summary>
    [TestMethod]
    public void Nested_Promoted_AndQualified()
    {
        const string definition = """
                                  struct h { uint8 n; };
                                  struct root { h hdr; struct { uint8 m; uint16 k; } inner; struct { uint8 p; }; uint8 v[hdr.n]; h copies[2]; char tail[]; };
                                  """;
        const string expected = """
                                root root
                                    0  ReadRootStruct               -            root

                                struct root
                                    0  ReadStruct                   hdr          h, prefix hdr.
                                    1  Seek                         inner        +1
                                    2  ReadStruct                   inner        inner
                                    3  ReadPromotedStruct           (anonymous)  (anonymous)
                                    4  EvaluateCount                v            count = hdr.n
                                    5  ReadNumericArray             v            UInt8
                                    6  CheckFixedCount              copies       count 2
                                    7  ReadStructArray              copies       h
                                    8  ReadTerminatedText           tail         TerminatedAscii
                                    9  FinishComposite              -            tail to 2

                                struct h
                                    0  ReadUInt8                    n            UInt8
                                    1  CaptureInteger               n            -> n
                                    2  PublishQualified             n            n -> hdr.*=hdr.n
                                    3  FinishComposite              -            tail +0

                                struct inner
                                    0  ReadUInt8                    m            UInt8
                                    1  Seek                         k            +1
                                    2  ReadUInt16Le                 k            UInt16 le
                                    3  FinishComposite              -            tail +0

                                promoted struct (anonymous)
                                    0  ReadUInt8                    p            UInt8
                                    1  FinishComposite              -            tail +0

                                """;
        AssertDump(expected, new CStruct(definition, aligned: true), "root");
    }

    /// <summary>A switch with a nested <c>if</c> and a conditional scope that restores a name a nested struct may replace.</summary>
    [TestMethod]
    public void Switch_WithNestedGroup_AndScope()
    {
        const string definition = """
                                  enum kind_t : uint8 { small = 1, large = 2 };
                                  struct body { uint8 size; };
                                  struct root {
                                      kind_t kind; body b; uint8 size;
                                      switch (kind) { case kind_t.small: { uint8 a[size]; } case kind_t.large: { uint16 count; if (count > 4) { uint32 big; } } default: { } }
                                  };
                                  """;
        const string expected = """
                                root root
                                    0  ReadRootStruct               -            root

                                struct root
                                    0  EnterConditionalScope        -            clear kind, size, count
                                    1  ReadEnum                     kind         UInt8 as kind_t
                                    2  CaptureEnum                  kind         -> kind
                                    3  CompleteMember               kind         save [kind] restore []
                                    4  ReadStruct                   b            body
                                    5  CompleteMember               b            save [] restore [size]
                                    6  ReadUInt8                    size         UInt8
                                    7  CaptureInteger               size         -> size
                                    8  CompleteMember               size         save [size] restore []
                                    9  SelectArm                    -            group 0 (kind) arm 0, else -> 12
                                   10  EvaluateCount                a            count = size
                                   11  ReadNumericArray             a            UInt8
                                   12  SelectArm                    -            group 0 (kind) arm 1, else -> 16
                                   13  ReadUInt16Le                 count        UInt16 le
                                   14  CaptureInteger               count        -> count
                                   15  CompleteMember               count        save [count] restore []
                                   16  SelectArm                    -            group 0 (kind) arm 1, else -> 19
                                   17  SelectArm                    -            group 1 ((count > 4)) arm 1, else -> 19
                                   18  ReadUInt32Le                 big          UInt32 le
                                   19  FinishComposite              -            tail +0

                                struct body
                                    0  ReadUInt8                    size         UInt8
                                    1  CaptureInteger               size         -> size
                                    2  FinishComposite              -            tail +0

                                """;
        AssertDump(expected, new CStruct(definition), "root");
    }

    /// <summary>
    ///     Data-sized arrays, aligned: a terminated array of structs counted by a scan after its placement and its
    ///     terminator skipped after the elements, then an <c>[EOF]</c> array counted to the end; the element sizes carry the
    ///     alignment past both, so every padding is a known seek and nothing is aligned at run time.
    /// </summary>
    [TestMethod]
    public void DataSizedArrays_Aligned()
    {
        const string definition = "struct e { uint8 a; uint16 b; }; struct root { uint8 tag; e items[]; uint8 n; uint16 values[EOF]; };";
        const string expected = """
                                root root
                                    0  ReadRootStruct               -            root

                                struct root
                                    0  ReadUInt8                    tag          UInt8
                                    1  Seek                         items        +1
                                    2  CountTerminated              items        element size 4
                                    3  ReadStructArray              items        e
                                    4  SkipTerminator               items        +4
                                    5  ReadUInt8                    n            UInt8
                                    6  Seek                         values       +1
                                    7  CountToEnd                   values       element size 2
                                    8  ReadNumericArray             values       UInt16 le
                                    9  FinishComposite              -            tail +0

                                struct e
                                    0  ReadUInt8                    a            UInt8
                                    1  Seek                         b            +1
                                    2  ReadUInt16Le                 b            UInt16 le
                                    3  FinishComposite              -            tail +0

                                """;
        AssertDump(expected, new CStruct(definition, aligned: true), "root");
    }

    /// <summary>
    ///     Multidimensional arrays: numbers read as one flat list and nested, character rows, a struct table read element
    ///     by element, an enum table, and a table root of a typedef read element by element.
    /// </summary>
    [TestMethod]
    public void MultidimensionalArrays()
    {
        const string definition = """
                                  enum kind : uint8 { A = 1 };
                                  struct p { uint8 x; uint8 y; };
                                  struct root { uint16 grid[2][3]; char names[2][4]; p pts[2][2]; kind kinds[3][1]; };
                                  typedef uint8 table[2][2];
                                  """;
        const string expected = """
                                root root
                                    0  ReadRootStruct               -            root

                                struct root
                                    0  CheckFixedCount              grid         count 6
                                    1  ReadNumericList              grid         UInt16 le
                                    2  ReshapeTable                 grid         dimensions 2x3
                                    3  CheckFixedCount              names        count 8
                                    4  ReadCharTable                names        Char
                                    5  CheckFixedCount              pts          count 4
                                    6  ReadStructElements           pts          p
                                    7  ReshapeTable                 pts          dimensions 2x2
                                    8  CheckFixedCount              kinds        count 3
                                    9  ReadEnumArray                kinds        UInt8 as kind
                                   10  ReshapeTable                 kinds        dimensions 3x1
                                   11  FinishComposite              -            tail +0

                                struct p
                                    0  ReadUInt8                    x            UInt8
                                    1  ReadUInt8                    y            UInt8
                                    2  FinishComposite              -            tail +0

                                """;
        var layout = new CStruct(definition);
        AssertDump(expected, layout, "root");
        const string table = """
                                root table
                                    0  CheckFixedCount              table        count 4
                                    1  ReadNumericElementList       table        UInt8
                                    2  ReshapeTable                 table        dimensions 2x2

                                """;
        AssertDump(table, layout, "table");
    }

    /// <summary>
    ///     Caller-supplied codecs: a variable-length value that counts a later array of them, after which a member is aligned
    ///     at run time, and a fixed-size one in an aligned layout, which occupies its declared size like any fixed member.
    /// </summary>
    [TestMethod]
    public void CustomCodecs()
    {
        const string definition = "struct root { vlq count; vlq values[count]; word4 w; uint32 after; };";
        const string expected = """
                                root root
                                    0  ReadRootStruct               -            root

                                struct root
                                    0  ReadCustom                   count        Custom
                                    1  CaptureInteger               count        -> count
                                    2  EvaluateCount                values       count = count
                                    3  ReadCustomArray              values       Custom
                                    4  Align                        w            to 4
                                    5  ReadCustom                   w            Custom
                                    6  ReadUInt32Le                 after        UInt32 le
                                    7  FinishComposite              -            tail +0

                                """;
        var options = new CStructCompilationOptions { Codecs = [VlqCodec.Instance, FixedWordCodec.Instance,], };
        AssertDump(expected, new CStruct(definition, aligned: true, compilationOptions: options), "root");
    }

    /// <summary>
    ///     A struct with bitfields, aligned: every member is placed by the runtime cursor (a bitfield opens or continues a
    ///     storage unit, a separator closes it), ordinary members complete their placement, and the cursor ends the struct.
    ///     An enum bitfield is captured for the count that follows.
    /// </summary>
    [TestMethod]
    public void Bitfields_PlacedAtRunTime()
    {
        const string definition = "enum kind : uint8 { A = 1 }; struct root { uint8 tag; uint16 lo : 4; kind k : 3; uint8 : 0; uint8 items[k]; };";
        const string expected = """
                                root root
                                    0  ReadRootStruct               -            root

                                struct root
                                    0  PlaceMember                  tag
                                    1  ReadUInt8                    tag          UInt8
                                    2  CompletePlacement            tag
                                    3  PlaceBitfield                lo
                                    4  ReadBitfield                 lo           UInt16 le
                                    5  PlaceBitfield                k
                                    6  ReadBitfield                 k            UInt8
                                    7  CaptureEnum                  k            -> k
                                    8  PlaceSeparator               (unnamed)
                                    9  EvaluateCount                items        count = k
                                   10  PlaceMember                  items
                                   11  ReadNumericArray             items        UInt8
                                   12  CompletePlacement            items
                                   13  FinishPlaced                 -            tail to 2

                                """;
        AssertDump(expected, new CStruct(definition, aligned: true), "root");
    }

    /// <summary>
    ///     Unions: a named member union and an anonymous promoted one, each a program of member views that restores the
    ///     variables and rewinds to the union's start before each member (a bitfield view opens its own unit), and a union
    ///     root.
    /// </summary>
    [TestMethod]
    public void Unions()
    {
        const string definition = """
                                  union u { uint8 n; uint16 w : 12; struct { uint8 a; uint8 b; } pair; };
                                  struct root { uint8 n; u value; union { uint16 half; uint8 bytes[2]; }; uint8 items[n]; };
                                  """;
        const string expected = """
                                root root
                                    0  ReadRootStruct               -            root

                                struct root
                                    0  ReadUInt8                    n            UInt8
                                    1  CaptureInteger               n            -> n
                                    2  ReadUnion                    value        u
                                    3  ReadPromotedUnion            (anonymous)  (anonymous)
                                    4  EvaluateCount                items        count = n
                                    5  ReadNumericArray             items        UInt8
                                    6  FinishComposite              -            tail +0

                                union u
                                    0  RestoreUnionSlots            -
                                    1  RewindToUnionStart           n
                                    2  ReadUInt8                    n            UInt8
                                    3  CaptureInteger               n            -> n
                                    4  RestoreUnionSlots            -
                                    5  RewindToUnionStart           w
                                    6  OpenBitfieldUnit             w
                                    7  ReadBitfield                 w            UInt16 le
                                    8  RestoreUnionSlots            -
                                    9  RewindToUnionStart           pair
                                   10  ReadStruct                   pair         pair

                                union (anonymous)
                                    0  RestoreUnionSlots            -
                                    1  RewindToUnionStart           half
                                    2  ReadUInt16Le                 half         UInt16 le
                                    3  RestoreUnionSlots            -
                                    4  CheckFixedCount              bytes        count 2
                                    5  RewindToUnionStart           bytes
                                    6  ReadNumericElements          bytes        UInt8

                                struct pair
                                    0  ReadUInt8                    a            UInt8
                                    1  ReadUInt8                    b            UInt8
                                    2  FinishComposite              -            tail +0

                                """;
        var layout = new CStruct(definition);
        AssertDump(expected, layout, "root");
        const string union = """
                                root u
                                    0  ReadRootUnion                -            u

                                union u
                                    0  RestoreUnionSlots            -
                                    1  RewindToUnionStart           n
                                    2  ReadUInt8                    n            UInt8
                                    3  CaptureInteger               n            -> n
                                    4  RestoreUnionSlots            -
                                    5  RewindToUnionStart           w
                                    6  OpenBitfieldUnit             w
                                    7  ReadBitfield                 w            UInt16 le
                                    8  RestoreUnionSlots            -
                                    9  RewindToUnionStart           pair
                                   10  ReadStruct                   pair         pair

                                struct pair
                                    0  ReadUInt8                    a            UInt8
                                    1  ReadUInt8                    b            UInt8
                                    2  FinishComposite              -            tail +0

                                """;
        AssertDump(union, layout, "u");
    }

    /// <summary>
    ///     Pointers: deferred scalars and arrays whose targets a struct follows after its last member (a counted target
    ///     whose count is a later field, list nodes, whose program is taken from the cache when followed rather than nested),
    ///     a promoted member's <c>void *</c>, read in place and never followed, and a pointer in a union view, read in place.
    /// </summary>
    [TestMethod]
    public void Pointers()
    {
        const string definition = """
                                  struct node { uint8 v; node *next; };
                                  union view { uint8 raw; char *text; };
                                  struct root { uint8 *bytes @count(n); node *items[2]; struct { void *opaque; }; view v; uint8 n; };
                                  """;
        const string expected = """
                                root root
                                    0  ReadRootStruct               -            root

                                struct root
                                    0  ReadPointer                  bytes        deferred CountedNumbers
                                    1  CheckFixedCount              items        count 2
                                    2  ReadPointerArray             items        deferred Composite
                                    3  ReadPromotedStruct           (anonymous)  (anonymous)
                                    4  ReadUnion                    v            view
                                    5  ReadUInt8                    n            UInt8
                                    6  CaptureInteger               n            -> n
                                    7  FollowPendingPointers        -
                                    8  FinishComposite              -            tail +0

                                promoted struct (anonymous)
                                    0  ReadPointer                  opaque       in place NoReader
                                    1  FinishComposite              -            tail +0

                                union view
                                    0  RestoreUnionSlots            -
                                    1  RewindToUnionStart           raw
                                    2  ReadUInt8                    raw          UInt8
                                    3  RestoreUnionSlots            -
                                    4  RewindToUnionStart           text
                                    5  ReadPointer                  text         in place Terminated

                                """;
        AssertDump(expected, new CStruct(definition, 1), "root");
    }

    /// <summary>Asserts a root's dump, showing the actual dump on failure so an intended change can be pasted in.</summary>
    /// <param name="expected">The expected dump.</param>
    /// <param name="layout">The layout.</param>
    /// <param name="root">The root.</param>
    private static void AssertDump(string expected, CStruct layout, string root)
    {
        string actual = ReadProgramDump.RenderRoot(layout, root);
        Assert.AreEqual(expected.ReplaceLineEndings("\n"), actual, "actual dump:\n" + actual);
    }

    /// <summary>
    ///     A conditional member that occupies no bytes leaves the offset known whether it is selected or not, so the next
    ///     member is placed by a fixed <c>Seek</c> rather than a run-time alignment, and the tail is known too.
    /// </summary>
    [TestMethod]
    public void ZeroSizeConditionalMember_KeepsTheKnownOffset()
    {
        const string expected = """
                                root r
                                    0  ReadRootStruct               -            r

                                struct r
                                    0  EnterConditionalScope        -            clear k
                                    1  ReadUInt8                    k            UInt8
                                    2  CaptureInteger               k            -> k
                                    3  CompleteMember               k            save [k] restore []
                                    4  SelectArm                    -            group 0 ((k == 1)) arm 1, else -> 7
                                    5  CheckFixedCount              z            count 0
                                    6  ReadNumericArray             z            UInt8
                                    7  Seek                         b            +1
                                    8  ReadUInt16Le                 b            UInt16 le
                                    9  FinishComposite              -            tail +0

                                """;
        AssertDump(expected, new CStruct("struct r { uint8 k; if (k == 1) { uint8 z[0]; } uint16 b; };", aligned: true), "r");
    }
}
