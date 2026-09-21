namespace CStructSharp.Docs.Examples;

using System.Collections.Generic;
using global::CStructSharp;
using global::CStructSharp.Diagnostics;
using global::CStructSharp.Generated;
using global::CStructSharp.Values;

/// <summary>The executable samples of the generated-code lessons (<c>docs/guides/generated/</c>), one region per lesson step.</summary>
internal static partial class Program
{
    #region generated-first-layout-class
    // The layout text lives in the attribute; the generator adds the members of this partial class at build time.
    [CStructLayout("struct header { uint16 kind; uint32 length; };")]
    public static partial class Wire
    {
    }
    #endregion

    #region generated-first-layout
    private static void GeneratedFirstLayout()
    {
        byte[] bytes = [0x02, 0x00, 0x06, 0x00, 0x00, 0x00];

        // Parse returns the generated class; its properties have the C# types the layout implies.
        Wire.Header header = Wire.Parse(bytes);
        Equal((ushort)2, header.Kind);
        Equal(6u, header.Length);

        // Serialize takes the class back to bytes, and the runtime layout is still there for anything else.
        SequenceEqual(bytes, Wire.Serialize(header));
        Equal("header", Wire.RootName);
        Equal(6, Wire.Layout.GetStructSizeInBytes("header"));
        Equal(6, Wire.Sizes.Header);
    }
    #endregion

    #region generated-views
    private static void GeneratedViews()
    {
        byte[] bytes = [0x02, 0x00, 0x06, 0x00, 0x00, 0x00];

        // A view decodes each member straight from the span when it is read; nothing is allocated for the header.
        var view = new Wire.HeaderView(bytes);
        Equal((ushort)2, view.Kind);
        Equal(6u, view.Length);
        Equal(6, view.Bytes.Length);

        // A view can become an object when one is wanted after all.
        Wire.Header header = view.ToObject();
        Equal(6u, header.Length);

        // Too few bytes fail the way the runtime fails: the same message, offset 0, path 'header'.
        try
        {
            _ = new Wire.HeaderView(bytes.AsSpan(0, 4));
            True(false, "a short source must fail");
        }
        catch (CStructReadException error)
        {
            Equal("Not enough bytes: needed 6, available 4 (path 'header', offset 0).", error.Message);
        }
    }
    #endregion

    #region generated-arrays-strings-enums-class
    [CStructLayout("""
        enum color : uint8 { Red = 1, Green = 2, Blue = 3 };
        flag perms : uint8 { Read = 1, Write = 2, Execute = 4 };
        struct sample {
            uint8 count;
            uint16 values[count];
            char name[8];
            utf8 label[6];
            cstring note;
            color colour;
            perms mode;
        };
        """, Root = "sample")]
    public static partial class Samples
    {
    }
    #endregion

    #region generated-arrays-strings-enums
    private static void GeneratedArraysStringsEnums()
    {
        byte[] bytes =
        [
            2, 0x34, 0x12, 0x78, 0x56,                               // count = 2, values = { 0x1234, 0x5678 }
            (byte)'p', (byte)'n', (byte)'g', 0, 0, 0, 0, 0,          // name[8] = "png" + NULs
            0xC3, 0xA9, (byte)'t', (byte)'e', 0, 0,                  // label[6] = "été" in UTF-8 + NULs
            (byte)'o', (byte)'k', 0,                                 // note = "ok" + terminator
            2,                                                       // colour = Green
            7,                                                       // mode = Read | Write | Execute
        ];
        Samples.Sample sample = Samples.Parse(bytes);
        Equal(2, sample.Values.Length);
        Equal((ushort)0x5678, sample.Values[1]);
        Equal("png\0\0\0\0\0", sample.Name);           // fixed text keeps its NULs unless TrimFixedText is set
        Equal("éte\0\0", sample.Label);
        Equal("ok", sample.Note);
        Equal(Samples.Color.Green, sample.Colour);
        Equal(Samples.Perms.Read | Samples.Perms.Write | Samples.Perms.Execute, sample.Mode);

        Samples.Sample trimmed = Samples.Parse(bytes, new ReadOptions { TrimFixedText = true });
        Equal("png", trimmed.Name);

        // A value the enum does not name is kept as the number: the C# enum is a byte underneath.
        bytes[^2] = 9;
        Equal((Samples.Color)9, Samples.Parse(bytes).Colour);
    }
    #endregion

    #region generated-unions-bitfields-nested-class
    [CStructLayout("""
        struct flags { uint8 version : 4; uint8 priority : 3; uint8 urgent : 1; };
        union payload { uint32 word; uint8 octets[4]; };
        struct packet { flags head; payload body; struct { uint16 x; uint16 y; } position; };
        """, Root = "packet")]
    public static partial class Packets
    {
    }
    #endregion

    #region generated-unions-bitfields-nested
    private static void GeneratedUnionsBitfieldsNested()
    {
        byte[] bytes = [0b1_101_0011, 0x01, 0x02, 0x03, 0x04, 0x0A, 0x00, 0x0B, 0x00];
        Packets.Packet packet = Packets.Parse(bytes);

        // Bitfields decode to their declared type; the offsets and shifts were computed at build time.
        Equal((byte)3, packet.Head.Version);
        Equal((byte)5, packet.Head.Priority);
        Equal((byte)1, packet.Head.Urgent);

        // A union reads every member from the same bytes and keeps the raw storage for writing back.
        Equal(0x04030201u, packet.Body.Word);
        SequenceEqual([1, 2, 3, 4], packet.Body.Octets);
        SequenceEqual([1, 2, 3, 4], packet.Body.RawStorage!);

        // An inline struct becomes a nested class named after its parent and member.
        Equal((ushort)10, packet.Position.X);
        Equal((ushort)11, packet.Position.Y);
        Equal(0, Packets.Offsets.Head.Priority);
        Equal(5, Packets.Offsets.Position.X);

        // Writing a union: choose the member to write, or leave SelectedMember null to write RawStorage back.
        packet.Body.SelectedMember = "word";
        packet.Body.Word = 0xAABBCCDD;
        byte[] written = Packets.Serialize(packet);
        SequenceEqual([0xDD, 0xCC, 0xBB, 0xAA], written[1..5]);
    }
    #endregion

    #region generated-pointers-class
    [CStructLayout("struct node { uint8 value; node *next; }; struct list { node *head; };", Root = "list", PointerSize = 1)]
    public static partial class Lists
    {
    }
    #endregion

    #region generated-pointers
    private static void GeneratedPointers()
    {
        // list.head → offset 1; node { value 10, next → 3 }; node { value 20, next null }.
        byte[] bytes = [1, 10, 3, 20, 0];
        Lists.List list = Lists.Parse(bytes);
        Equal(1L, list.Head.Address);
        True(list.Head.IsDereferenced, "the pointer was followed");
        Equal((byte)10, list.Head.Value!.Value);
        Equal((byte)20, list.Head.Value.Next.Value!.Value);
        True(list.Head.Value.Next.Value.Next.IsNull, "the chain ends with a stored zero");

        // Addresses only: the stored numbers, no targets.
        Lists.List addresses = Lists.Parse(bytes, new ReadOptions { DereferencePointers = false });
        True(!addresses.Head.IsDereferenced, "not followed");

        // The budgets are the runtime's, with the runtime's messages: the runtime layout fails the same way.
        var limited = new ReadOptions { MaxPointerDepth = 1 };
        string runtimeMessage = string.Empty;
        try
        {
            _ = Lists.Layout.Parse(bytes, "list", options: limited);
        }
        catch (CStructReadLimitException error)
        {
            runtimeMessage = error.Message;
        }

        try
        {
            _ = Lists.Parse(bytes, limited);
            True(false, "the depth limit must apply");
        }
        catch (CStructReadLimitException error)
        {
            Equal("Maximum pointer dereference depth exceeded (field 'next' (node), in 'list', offset 1).", error.Message);
            Equal(runtimeMessage, error.Message);
        }

        // A cycle is detected, not followed forever.
        byte[] cycle = [1, 10, 1];
        try
        {
            _ = Lists.Parse(cycle);
            True(false, "the cycle must be detected");
        }
        catch (CStructReadException error)
        {
            True(error.Message.StartsWith("Cyclic pointer target detected", StringComparison.Ordinal), error.Message);
        }
    }
    #endregion

    #region generated-conditionals-class
    [CStructLayout("""
        struct message {
            uint8 kind;
            if (kind == 1) { uint32 code; } else { uint16 short_code; }
            switch (kind) { case 1: { uint8 retries; } default: { uint8 padding; } }
            uint8 tail;
        };
        """, Root = "message")]
    public static partial class Messages
    {
    }
    #endregion

    #region generated-conditionals
    private static void GeneratedConditionals()
    {
        Messages.Message coded = Messages.Parse([1, 0x78, 0x56, 0x34, 0x12, 3, 9]);
        True(coded.HasCode && !coded.HasShortCode, "the first arm was selected");
        Equal(0x12345678u, coded.Code);
        Equal((byte)3, coded.Retries);
        True(!coded.HasPadding, "the default case was not selected");

        Messages.Message other = Messages.Parse([2, 0x34, 0x12, 4, 9]);
        True(!other.HasCode && other.HasShortCode, "the else arm was selected");
        Equal((ushort)0x1234, other.ShortCode);
        Equal((byte)4, other.Padding);

        // Writing decides from the values: a member of an inactive arm must not be supplied.
        other.HasCode = true;
        try
        {
            _ = Messages.Serialize(other);
            True(false, "an inactive member must be rejected");
        }
        catch (CStructWriteException error)
        {
            Equal("Inactive conditional field supplied: code (path 'message', offset 1).", error.Message);
        }
    }
    #endregion

    #region generated-writing-updating
    private static void GeneratedWritingUpdating()
    {
        var header = new Wire.Header { Kind = 7, Length = 1024 };

        // Three ways to write: a new array, a caller's span (the length written comes back), a stream.
        byte[] bytes = Wire.Serialize(header);
        SequenceEqual([7, 0, 0, 4, 0, 0], bytes);
        Span<byte> destination = stackalloc byte[16];
        Equal(6, Wire.Serialize(header, destination));
        using var stream = new MemoryStream();
        Wire.Write(stream, header);
        Equal(6L, stream.Length);

        // A typed setter changes one member in place at its build-time offset; a path update goes through the runtime.
        Wire.Update.Kind(bytes, 8);
        Equal((ushort)8, Wire.Parse(bytes).Kind);
        Wire.UpdatePath(bytes, "header.length", 2048u);
        Equal(2048u, Wire.Parse(bytes).Length);

        // A destination that is too small fails with the runtime's capacity text.
        try
        {
            _ = Wire.Serialize(header, destination.Slice(0, 3));
            True(false, "the capacity must be checked");
        }
        catch (CStructWriteException error)
        {
            Equal("The serialized value exceeds the supplied destination capacity (field 'length' (uint32), in 'header', offset 2).", error.Message);
        }
    }
    #endregion

    #region generated-mapped-classes-class
    // The generator writes ReadFrom, WriteTo, and the registration; the properties are matched by name.
    [CStructMapped(Layout = "header")]
    public sealed partial class HeaderRecord
    {
        public ushort Kind { get; set; }

        public uint Length { get; set; }
    }

    [CStructMapped]
    public sealed partial class SampleRecord
    {
        public byte Count { get; set; }

        public List<ushort> Values { get; set; } = [];

        [CStructMember("name")]
        public string FileName { get; set; } = string.Empty;

        public Samples.Color Colour { get; set; }
    }
    #endregion

    #region generated-mapped-classes
    private static void GeneratedMappedClasses()
    {
        byte[] bytes = [0x02, 0x00, 0x06, 0x00, 0x00, 0x00];

        // The layout class reads its root straight into the mapped class; Wire.Layout.ReadValue<T>(bytes, "header")
        // is the same call with the root named, and the generated instance bridges to it as well.
        HeaderRecord record = Wire.ReadValue<HeaderRecord>(bytes);
        Equal(6u, record.Length);
        Equal(6u, Wire.Layout.ReadValue<HeaderRecord>(bytes, "header").Length);
        Equal(6u, Wire.ToMapped<HeaderRecord>(Wire.Parse(bytes)).Length);
        True(!Wire.TryReadValue(bytes[..3], out HeaderRecord? _), "three bytes are not a header");
        SequenceEqual(bytes, Wire.SerializeMapped(record));

        // Names match by exact spelling, then case-insensitively, then ignoring underscores; [CStructMember] overrides.
        byte[] sampleBytes = [2, 0x34, 0x12, 0x78, 0x56, (byte)'p', (byte)'n', (byte)'g', 0, 0, 0, 0, 0, 0xC3, 0xA9, (byte)'t', (byte)'e', 0, 0, (byte)'o', (byte)'k', 0, 2, 7];
        SampleRecord sample = Samples.Layout.ReadValue<SampleRecord>(sampleBytes, "sample");
        Equal(2, sample.Values.Count);
        Equal("png\0\0\0\0\0", sample.FileName);
        Equal(Samples.Color.Green, sample.Colour);
    }
    #endregion
}
