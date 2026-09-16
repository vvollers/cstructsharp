namespace CStructSharp.Tests.Dissect.Corpus;

/// <summary>
///     Reduced, hand-written shapes taken from the dissect ecosystem's definitions (NTFS, PE, utmp, lastlog,
///     ISO 9660, ETL, ...): each test pastes the header vocabulary as it appears there and checks bytes, offsets,
///     and values. They are the executable form of the corpus sweep's most common failure causes.
/// </summary>
[TestClass]
public class CorpusShapeTests
{
    /// <summary>dissect.ntfs <c>FILE_NAME</c>: Windows spellings, a tagged typedef, and an anonymous union holding an anonymous struct.</summary>
    [TestMethod]
    public void Ntfs_FileNameAttribute()
    {
        const string source = """
                              typedef struct _FILE_NAME {
                                  ULONGLONG   ParentDirectory;
                                  LONGLONG    CreationTime;
                                  LONGLONG    LastModificationTime;
                                  LONGLONG    LastChangeTime;
                                  LONGLONG    LastAccessTime;
                                  LONGLONG    AllocatedLength;
                                  LONGLONG    FileSize;
                                  ULONG       FileAttributes;
                                  union {
                                      struct {
                                          USHORT  EaSize;
                                          USHORT  _;
                                      };
                                      ULONG   ReparsePointTag;
                                  };
                                  UCHAR       FileNameLength;
                                  UCHAR       Flags;
                                  WCHAR       FileName[FileNameLength];
                              } FILE_NAME;
                              """;
        var layout = new CStruct(source);
        byte[] bytes = new byte[66 + 6];
        bytes[56] = 0x20;
        bytes[60] = 0x34;
        bytes[61] = 0x12;
        bytes[64] = 3;
        bytes[65] = 1;
        bytes[66] = (byte)'a';
        bytes[68] = (byte)'b';
        bytes[70] = (byte)'c';

        dynamic value = layout.Parse(bytes.AsSpan(), "FILE_NAME");
        Assert.AreEqual(0x20U, (uint)value.FileAttributes);
        Assert.AreEqual((ushort)0x1234, (ushort)value.EaSize);
        Assert.AreEqual(0x1234U, (uint)value.ReparsePointTag);
        Assert.AreEqual((byte)3, (byte)value.FileNameLength);
        Assert.AreEqual("abc", (string)value.FileName);
        Assert.AreEqual(66, layout.ResolveAddress(new MemoryStream(bytes), "_FILE_NAME.FileName"));
    }

    /// <summary>dissect.executable PE headers: typedef declarator lists with pointer aliases, <c>WORD</c>/<c>DWORD</c>/<c>LONG</c>, and a fixed array.</summary>
    [TestMethod]
    public void Pe_DosHeader()
    {
        const string source = """
                              typedef struct _IMAGE_DOS_HEADER {
                                  WORD   e_magic;
                                  WORD   e_cblp;
                                  WORD   e_cp;
                                  WORD   e_crlc;
                                  WORD   e_cparhdr;
                                  WORD   e_minalloc;
                                  WORD   e_maxalloc;
                                  WORD   e_ss;
                                  WORD   e_sp;
                                  WORD   e_csum;
                                  WORD   e_ip;
                                  WORD   e_cs;
                                  WORD   e_lfarlc;
                                  WORD   e_ovno;
                                  WORD   e_res[4];
                                  WORD   e_oemid;
                                  WORD   e_oeminfo;
                                  WORD   e_res2[10];
                                  LONG   e_lfanew;
                              } IMAGE_DOS_HEADER, *PIMAGE_DOS_HEADER;
                              """;
        var layout = new CStruct(source);
        Assert.AreEqual(64, layout.GetStructSizeInBytes("IMAGE_DOS_HEADER"));
        Assert.AreEqual(64, layout.GetStructSizeInBytes("_IMAGE_DOS_HEADER"));

        byte[] bytes = new byte[64];
        bytes[0] = (byte)'M';
        bytes[1] = (byte)'Z';
        bytes[60] = 0x80;
        dynamic value = layout.Parse(bytes.AsSpan(), "IMAGE_DOS_HEADER");
        Assert.AreEqual((ushort)0x5A4D, (ushort)value.e_magic);
        Assert.AreEqual(0x80, (int)value.e_lfanew);
        Assert.AreEqual(60, layout.ResolveAddress(new MemoryStream(bytes), "IMAGE_DOS_HEADER.e_lfanew"));
    }

    /// <summary>dissect.target utmp/lastlog: anonymous top-level bodies naming <c>timeval</c> and <c>time_t</c>, <c>char[]</c> buffers, and <c>pid_t</c>-style typedefs.</summary>
    [TestMethod]
    public void Utmp_AnonymousTopLevelTypes()
    {
        const string source = """
                              #define UT_LINESIZE 32
                              #define UT_NAMESIZE 32
                              typedef uint32 pid_t;
                              struct exit_status {
                                  short e_termination;
                                  short e_exit;
                              };
                              struct {
                                  int32 tv_sec;
                                  int32 tv_usec;
                              } timeval;
                              struct entry {
                                  short   ut_type;
                                  pid_t   ut_pid;
                                  char    ut_line[UT_LINESIZE];
                                  char    ut_id[4];
                                  char    ut_user[UT_NAMESIZE];
                                  struct exit_status ut_exit;
                                  timeval ut_tv;
                              };
                              struct { uint32 tv_sec; } time_t;
                              struct lastlog { time_t ll_time; };
                              """;
        var layout = new CStruct(source);
        Assert.AreEqual(2 + 4 + 32 + 4 + 32 + 4 + 8, layout.GetStructSizeInBytes("entry"));
        Assert.AreEqual(4, layout.GetStructSizeInBytes("lastlog"));
        Assert.AreEqual(78, layout.ResolveAddress(new MemoryStream(new byte[86]), "entry.ut_tv"));
    }

    /// <summary>dissect.disc ISO 9660: byte-string defines next to integer ones, and a <c>#pragma</c>-free packed layout.</summary>
    [TestMethod]
    public void Iso9660_ByteConstants()
    {
        const string source = """
                              #define ISO_STANDARD_ID b"CD001"
                              #define ISO_VD_PRIMARY 1
                              struct iso_volume_descriptor {
                                  uint8 type;
                                  char  id[5];
                                  uint8 version;
                              };
                              """;
        var layout = new CStruct(source);
        CollectionAssert.AreEqual("CD001"u8.ToArray(), (byte[])layout.Constants["ISO_STANDARD_ID"].Value!);
        Assert.AreEqual(LayoutConstantKind.Integer, layout.Constants["ISO_VD_PRIMARY"].Kind);

        dynamic value = layout.Parse("CD001"u8.ToArray().AsSpan(), "iso_volume_descriptor");
        Assert.AreEqual("CD001", (string)value.id);
    }

    /// <summary>dissect.xfs: a named body followed by an object name (<c>} xfs_dir2_sf_hdr_t;</c>) and kernel spellings.</summary>
    [TestMethod]
    public void Xfs_NamedBodyWithObjectName()
    {
        const string source = """
                              typedef __u8 xfs_dir2_sf_off_t[2];
                              struct xfs_dir2_sf_hdr {
                                  __u8 count;
                                  __u8 i8count;
                                  __u8 parent[8];
                              } xfs_dir2_sf_hdr_t;
                              struct xfs_dir2_sf_entry {
                                  __u8 namelen;
                                  xfs_dir2_sf_off_t offset;
                                  __u8 name[namelen];
                              };
                              """;
        var layout = new CStruct(source, isLittleEndian: false);
        Assert.AreEqual(10, layout.GetStructSizeInBytes("xfs_dir2_sf_hdr"));
        dynamic entry = layout.Parse(new byte[] { 2, 0x12, 0x34, (byte)'a', (byte)'b', }.AsSpan(), "xfs_dir2_sf_entry");
        Assert.AreEqual((byte)0x34, (byte)entry.offset[1]);
        Assert.AreEqual((byte)'b', (byte)entry.name[1]);
    }
}
