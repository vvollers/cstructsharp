namespace CStructSharp.Tests;

using System.Dynamic;

/// <summary>
///     Verifies the CStruct definitions and sample bytes used by the CStructSharpInspector example app's
///     well-known-format catalog (BMP, WAV, ZIP, PNG, JPG/JFIF, PE/EXE, PE/DLL, TAR, ICO). Each test constructs the
///     exact same definition text and sample bytes the app's <c>src/formats.ts</c> catalog carries, so the app's
///     hand-authored examples have a real correctness signal instead of trusting hand-written bytes/DSL text by
///     inspection alone.
/// </summary>
[TestClass]
public class WellKnownFormatFixtures
{
    /// <summary>A minimal 54-byte BMP header (BITMAPFILEHEADER + BITMAPINFOHEADER) with no pixel data.</summary>
    [TestMethod]
    public void Bmp_FileAndInfoHeaders_DecodeExpectedFields()
    {
        const string definition = """
                                  enum bmp_compression : uint32 {
                                      Rgb = 0,
                                      Rle8 = 1,
                                      Rle4 = 2,
                                      Bitfields = 3
                                  };

                                  struct bitmap_file_header {
                                      char signature[2];
                                      uint32 file_size;
                                      uint16 reserved1;
                                      uint16 reserved2;
                                      uint32 pixel_data_offset;
                                  };

                                  struct bitmap_info_header {
                                      uint32 header_size;
                                      int32 width;
                                      int32 height;
                                      uint16 planes;
                                      uint16 bits_per_pixel;
                                      bmp_compression compression;
                                      uint32 image_size;
                                      int32 x_pixels_per_meter;
                                      int32 y_pixels_per_meter;
                                      uint32 colors_used;
                                      uint32 colors_important;
                                  };

                                  struct root {
                                      bitmap_file_header file_header;
                                      bitmap_info_header info_header;
                                  };
                                  """;
        byte[] bytes =
        [
            0x42, 0x4d, 0x36, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x36, 0x00, 0x00, 0x00, 0x28, 0x00,
            0x00, 0x00, 0x02, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x18, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
        ];
        var cstruct = new CStruct(definition);
        Assert.AreEqual(54, bytes.Length);

        dynamic parsed = cstruct.Parse(bytes, "root");
        Assert.AreEqual("BM", (string)parsed.file_header.signature);
        Assert.AreEqual(54u, (uint)parsed.file_header.file_size);
        Assert.AreEqual(54u, (uint)parsed.file_header.pixel_data_offset);
        Assert.AreEqual(2, (int)parsed.info_header.width);
        Assert.AreEqual(1, (int)parsed.info_header.height);
        Assert.AreEqual((ushort)24, (ushort)parsed.info_header.bits_per_pixel);
        Assert.AreEqual("Rgb", (string)parsed.info_header.compression.Name);
    }

    /// <summary>A minimal 44-byte canonical WAV header: RIFF/WAVE container, fmt subchunk, empty data subchunk.</summary>
    [TestMethod]
    public void Wav_RiffFmtAndDataChunks_DecodeExpectedFields()
    {
        const string definition = """
                                  struct riff_header {
                                      char chunk_id[4];
                                      uint32 chunk_size;
                                      char format[4];
                                  };

                                  struct fmt_chunk {
                                      char chunk_id[4];
                                      uint32 chunk_size;
                                      uint16 audio_format;
                                      uint16 num_channels;
                                      uint32 sample_rate;
                                      uint32 byte_rate;
                                      uint16 block_align;
                                      uint16 bits_per_sample;
                                  };

                                  struct data_chunk_header {
                                      char chunk_id[4];
                                      uint32 chunk_size;
                                  };

                                  struct root {
                                      riff_header riff;
                                      fmt_chunk fmt;
                                      data_chunk_header data;
                                  };
                                  """;
        byte[] bytes =
        [
            0x52, 0x49, 0x46, 0x46, 0x24, 0x00, 0x00, 0x00, 0x57, 0x41, 0x56, 0x45, 0x66, 0x6d, 0x74, 0x20,
            0x10, 0x00, 0x00, 0x00, 0x01, 0x00, 0x02, 0x00, 0x44, 0xac, 0x00, 0x00, 0x10, 0xb1, 0x02, 0x00,
            0x04, 0x00, 0x10, 0x00, 0x64, 0x61, 0x74, 0x61, 0x00, 0x00, 0x00, 0x00,
        ];
        var cstruct = new CStruct(definition);
        Assert.AreEqual(44, bytes.Length);

        dynamic parsed = cstruct.Parse(bytes, "root");
        Assert.AreEqual("RIFF", (string)parsed.riff.chunk_id);
        Assert.AreEqual("WAVE", (string)parsed.riff.format);
        Assert.AreEqual("fmt ", (string)parsed.fmt.chunk_id);
        Assert.AreEqual((ushort)1, (ushort)parsed.fmt.audio_format);
        Assert.AreEqual((ushort)2, (ushort)parsed.fmt.num_channels);
        Assert.AreEqual(44100u, (uint)parsed.fmt.sample_rate);
        Assert.AreEqual((ushort)16, (ushort)parsed.fmt.bits_per_sample);
        Assert.AreEqual("data", (string)parsed.data.chunk_id);
        Assert.AreEqual(0u, (uint)parsed.data.chunk_size);
    }

    /// <summary>
    ///     A 38-byte ZIP local file header for an empty stored entry named "test.txt" - real bitflags via an inline
    ///     anonymous bitfield struct, and a runtime-length trailing filename array sized by an earlier field.
    /// </summary>
    [TestMethod]
    public void Zip_LocalFileHeader_DecodesBitflagsAndRuntimeLengthName()
    {
        const string definition = """
                                  struct root {
                                      uint32 signature;
                                      uint16 version_needed;
                                      struct {
                                          uint16 encrypted : 1;
                                          uint16 compression_option : 2;
                                          uint16 has_data_descriptor : 1;
                                          uint16 reserved : 12;
                                      } general_purpose_flag;
                                      uint16 compression_method;
                                      uint16 last_mod_time;
                                      uint16 last_mod_date;
                                      uint32 crc_32;
                                      uint32 compressed_size;
                                      uint32 uncompressed_size;
                                      uint16 file_name_length;
                                      uint16 extra_field_length;
                                      char file_name[file_name_length];
                                      uint8 extra_field[extra_field_length];
                                  };
                                  """;
        byte[] bytes =
        [
            0x50, 0x4b, 0x03, 0x04, 0x14, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x74, 0x65,
            0x73, 0x74, 0x2e, 0x74, 0x78, 0x74,
        ];
        var cstruct = new CStruct(definition);
        Assert.AreEqual(38, bytes.Length);

        dynamic parsed = cstruct.Parse(bytes, "root");
        Assert.AreEqual(0x04034b50u, (uint)parsed.signature);
        Assert.AreEqual((ushort)20, (ushort)parsed.version_needed);
        Assert.AreEqual((ushort)0, (ushort)parsed.general_purpose_flag.encrypted);
        Assert.AreEqual((ushort)0, (ushort)parsed.general_purpose_flag.has_data_descriptor);
        Assert.AreEqual((ushort)8, (ushort)parsed.file_name_length);
        Assert.AreEqual("test.txt", (string)parsed.file_name);
    }

    /// <summary>A minimal 33-byte PNG file: the 8-byte signature plus a big-endian IHDR chunk.</summary>
    [TestMethod]
    public void Png_SignatureAndIhdrChunk_DecodeBigEndianFields()
    {
        const string definition = """
                                  enum png_color_type : uint8 {
                                      Grayscale = 0,
                                      Rgb = 2,
                                      Palette = 3,
                                      GrayscaleAlpha = 4,
                                      Rgba = 6
                                  };

                                  struct ihdr_chunk {
                                      uint32 length;
                                      char chunk_type[4];
                                      uint32 width;
                                      uint32 height;
                                      uint8 bit_depth;
                                      png_color_type color_type;
                                      uint8 compression_method;
                                      uint8 filter_method;
                                      uint8 interlace_method;
                                      uint32 crc;
                                  };

                                  struct root {
                                      uint8 signature[8];
                                      ihdr_chunk ihdr;
                                  };
                                  """;
        byte[] bytes =
        [
            0x89, 0x50, 0x4e, 0x47, 0x0d, 0x0a, 0x1a, 0x0a, 0x00, 0x00, 0x00, 0x0d, 0x49, 0x48, 0x44, 0x52,
            0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x08, 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00,
        ];
        var cstruct = new CStruct(definition, isLittleEndian: false);
        Assert.AreEqual(33, bytes.Length);

        dynamic parsed = cstruct.Parse(bytes, "root");
        Assert.AreEqual(13u, (uint)parsed.ihdr.length);
        Assert.AreEqual("IHDR", (string)parsed.ihdr.chunk_type);
        Assert.AreEqual(1u, (uint)parsed.ihdr.width);
        Assert.AreEqual(1u, (uint)parsed.ihdr.height);
        Assert.AreEqual((byte)8, (byte)parsed.ihdr.bit_depth);
        Assert.AreEqual("Rgb", (string)parsed.ihdr.color_type.Name);
    }

    /// <summary>
    ///     A 20-byte JPEG SOI marker plus a standard JFIF APP0 segment, using explicit per-field '&gt;' suffixes
    ///     rather than a global big-endian option (the format is only big-endian in these specific fields).
    /// </summary>
    [TestMethod]
    public void Jpg_SoiAndJfifApp0Segment_DecodeExpectedFields()
    {
        const string definition = """
                                  struct app0_segment {
                                      uint16> marker;
                                      uint16> length;
                                      char identifier[5];
                                      uint8 version_major;
                                      uint8 version_minor;
                                      uint8 density_units;
                                      uint16> x_density;
                                      uint16> y_density;
                                      uint8 thumbnail_width;
                                      uint8 thumbnail_height;
                                  };

                                  struct root {
                                      uint16> soi_marker;
                                      app0_segment app0;
                                  };
                                  """;
        byte[] bytes =
        [
            0xff, 0xd8, 0xff, 0xe0, 0x00, 0x10, 0x4a, 0x46, 0x49, 0x46, 0x00, 0x01, 0x01, 0x01, 0x00, 0x48,
            0x00, 0x48, 0x00, 0x00,
        ];
        var cstruct = new CStruct(definition);
        Assert.AreEqual(20, bytes.Length);

        dynamic parsed = cstruct.Parse(bytes, "root");
        Assert.AreEqual((ushort)0xffd8, (ushort)parsed.soi_marker);
        Assert.AreEqual((ushort)0xffe0, (ushort)parsed.app0.marker);
        Assert.AreEqual((ushort)16, (ushort)parsed.app0.length);
        Assert.AreEqual("JFIF\0", (string)parsed.app0.identifier);
        Assert.AreEqual((byte)1, (byte)parsed.app0.version_major);
        Assert.AreEqual((ushort)72, (ushort)parsed.app0.x_density);
        Assert.AreEqual((ushort)72, (ushort)parsed.app0.y_density);
    }

    /// <summary>
    ///     A 90-byte minimal PE image: a real 64-byte DOS header whose <c>e_lfanew</c> is declared as an actual
    ///     CStruct pointer field (absolute addressing) dereferencing to a COFF file header + optional header magic.
    ///     Shared by both the "EXE" and "DLL" catalog entries; only the COFF characteristics flag differs.
    /// </summary>
    [TestMethod]
    [DataRow(false, (ushort)0x0022, DisplayName = "EXE")]
    [DataRow(true, (ushort)0x2022, DisplayName = "DLL")]
    public void Pe_DosHeaderPointerToCoffHeader_DecodesAcrossExeAndDll(bool isDll, ushort characteristics)
    {
        const string definition = """
                                  struct pe_header {
                                      char signature[4];
                                      uint16 machine;
                                      uint16 number_of_sections;
                                      uint32 time_date_stamp;
                                      uint32 pointer_to_symbol_table;
                                      uint32 number_of_symbols;
                                      uint16 size_of_optional_header;
                                      uint16 characteristics;
                                      uint16 optional_header_magic;
                                  };

                                  struct dos_header {
                                      uint16 e_magic;
                                      uint16 e_cblp;
                                      uint16 e_cp;
                                      uint16 e_crlc;
                                      uint16 e_cparhdr;
                                      uint16 e_minalloc;
                                      uint16 e_maxalloc;
                                      uint16 e_ss;
                                      uint16 e_sp;
                                      uint16 e_csum;
                                      uint16 e_ip;
                                      uint16 e_cs;
                                      uint16 e_lfarlc;
                                      uint16 e_ovno;
                                      uint16 e_res[4];
                                      uint16 e_oemid;
                                      uint16 e_oeminfo;
                                      uint16 e_res2[10];
                                      pe_header *e_lfanew;
                                  };

                                  struct root {
                                      dos_header dos;
                                  };
                                  """;
        byte[] tail = isDll
            ? [0x22, 0x20, 0x0b, 0x02]
            : [0x22, 0x00, 0x0b, 0x02];
        byte[] bytes =
        [
            0x4d, 0x5a, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00,
            0x50, 0x45, 0x00, 0x00, 0x64, 0x86, 0x01, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
            0x00, 0x00, 0x00, 0x00, 0x00, 0x00, tail[0], tail[1], tail[2], tail[3],
        ];
        Assert.AreEqual(90, bytes.Length);
        Assert.AreEqual((ushort)characteristics, (ushort)((tail[1] << 8) | tail[0]));

        var cstruct = new CStruct(definition, pointerSize: 4);
        dynamic parsed = cstruct.Parse(
            bytes,
            "root",
            options: new ReadOptions { AddressingMode = PointerAddressingMode.Absolute });

        Assert.AreEqual((ushort)0x5a4d, (ushort)parsed.dos.e_magic);
        var pointer = (Pointer)parsed.dos.e_lfanew;
        Assert.IsTrue(pointer.IsDereferenced);
        Assert.AreEqual(64L, pointer.Address);
        dynamic peHeader = pointer.Value!;
        Assert.AreEqual("PE\0\0", (string)peHeader.signature);
        Assert.AreEqual((ushort)0x8664, (ushort)peHeader.machine);
        Assert.AreEqual((ushort)1, (ushort)peHeader.number_of_sections);
        Assert.AreEqual((ushort)characteristics, (ushort)peHeader.characteristics);
        Assert.AreEqual((ushort)0x020b, (ushort)peHeader.optional_header_magic);
    }

    /// <summary>
    ///     A 38-byte ICO directory: a 2-entry array of icon directory entries whose length is driven by an earlier
    ///     <c>image_count</c> field - a runtime-sized array of a composite (struct) element type.
    /// </summary>
    [TestMethod]
    public void Ico_IconDirectory_DecodesCountDrivenEntryArray()
    {
        const string definition = """
                                  struct icon_dir_entry {
                                      uint8 width;
                                      uint8 height;
                                      uint8 color_count;
                                      uint8 reserved;
                                      uint16 color_planes;
                                      uint16 bits_per_pixel;
                                      uint32 size_in_bytes;
                                      uint32 data_offset;
                                  };

                                  struct root {
                                      uint16 reserved;
                                      uint16 image_type;
                                      uint16 image_count;
                                      icon_dir_entry entries[image_count];
                                  };
                                  """;
        byte[] bytes =
        [
            0x00, 0x00, 0x01, 0x00, 0x02, 0x00, 0x10, 0x10, 0x00, 0x00, 0x01, 0x00, 0x20, 0x00, 0x68, 0x04,
            0x00, 0x00, 0x26, 0x00, 0x00, 0x00, 0x20, 0x20, 0x00, 0x00, 0x01, 0x00, 0x20, 0x00, 0xa8, 0x10,
            0x00, 0x00, 0x8e, 0x04, 0x00, 0x00,
        ];
        var cstruct = new CStruct(definition);
        Assert.AreEqual(38, bytes.Length);

        dynamic parsed = cstruct.Parse(bytes, "root");
        Assert.AreEqual((ushort)1, (ushort)parsed.image_type);
        Assert.AreEqual((ushort)2, (ushort)parsed.image_count);
        Assert.AreEqual(2, parsed.entries.Count);
        Assert.AreEqual((byte)16, (byte)parsed.entries[0].width);
        Assert.AreEqual((byte)32, (byte)parsed.entries[1].width);
        Assert.AreEqual(38u, (uint)parsed.entries[0].data_offset);
    }

    /// <summary>
    ///     A real 512-byte POSIX ustar header (fixed-width ASCII/octal text fields, not binary integers) with a
    ///     correctly computed checksum, for a zero-content file named "hello.txt".
    /// </summary>
    [TestMethod]
    public void Tar_UstarHeader_DecodesFixedWidthTextFields()
    {
        const string definition = """
                                  struct root {
                                      char name[100];
                                      char mode[8];
                                      char uid[8];
                                      char gid[8];
                                      char size[12];
                                      char mtime[12];
                                      char chksum[8];
                                      char typeflag[1];
                                      char linkname[100];
                                      char magic[6];
                                      char version[2];
                                      char uname[32];
                                      char gname[32];
                                      char devmajor[8];
                                      char devminor[8];
                                      char prefix[155];
                                      char padding[12];
                                  };
                                  """;
        byte[] bytes = BuildUstarHeader();
        Assert.AreEqual(512, bytes.Length);

        var cstruct = new CStruct(definition);
        dynamic parsed = cstruct.Parse(bytes, "root");
        Assert.AreEqual("hello.txt" + new string('\0', 91), (string)parsed.name);
        Assert.AreEqual("ustar\0", (string)parsed.magic);
        Assert.AreEqual("00", (string)parsed.version);
        Assert.AreEqual("0" /* typeflag '0' = regular file */, (string)parsed.typeflag);
        Assert.AreEqual("011556\0 ", (string)parsed.chksum);
    }

    /// <summary>Builds the 512-byte ustar header bytes, computing the real POSIX header checksum.</summary>
    private static byte[] BuildUstarHeader()
    {
        static byte[] Field(string text, int length)
        {
            byte[] field = new byte[length];
            System.Text.Encoding.ASCII.GetBytes(text).CopyTo(field, 0);
            return field;
        }

        byte[] name = Field("hello.txt", 100);
        byte[] mode = Field("0000644\0", 8);
        byte[] uid = Field("0000000\0", 8);
        byte[] gid = Field("0000000\0", 8);
        byte[] size = Field("00000000012\0", 12);
        byte[] mtime = Field("00000000000\0", 12);
        byte[] typeflag = Field("0", 1);
        byte[] linkname = new byte[100];
        byte[] magic = Field("ustar", 6);
        byte[] version = System.Text.Encoding.ASCII.GetBytes("00");
        byte[] uname = Field("user", 32);
        byte[] gname = Field("group", 32);
        byte[] devmajor = new byte[8];
        byte[] devminor = new byte[8];
        byte[] prefix = new byte[155];
        byte[] padding = new byte[12];

        byte[] preChksum = [.. name, .. mode, .. uid, .. gid, .. size, .. mtime,];
        byte[] postChksum =
        [
            .. typeflag, .. linkname, .. magic, .. version, .. uname, .. gname, .. devmajor, .. devminor,
            .. prefix, .. padding,
        ];
        byte[] spacesForChecksum = [0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20, 0x20,];
        int sum = 0;
        foreach (byte value in preChksum)
        {
            sum += value;
        }

        foreach (byte value in spacesForChecksum)
        {
            sum += value;
        }

        foreach (byte value in postChksum)
        {
            sum += value;
        }

        string octal = System.Convert.ToString(sum, 8).PadLeft(6, '0');
        byte[] chksum = [.. System.Text.Encoding.ASCII.GetBytes(octal), 0x00, 0x20,];

        return [.. preChksum, .. chksum, .. postChksum,];
    }
}
