namespace CStructSharp.Benchmarks.GeneratedLayouts;

/// <summary>An ext-style superblock: a fixed 24-byte struct whose header rules are field constraints.</summary>
[CStructLayout("struct sb { uint32 inodes @expect(inodes > 0); uint32 blocks; uint16 magic @expect(magic == 0xEF53, \"ext.magic\"); uint16 state; uint32 log_block_size @expect(log_block_size <= 6, \"ext.block_size\"); uint32 rev_level @supports(rev_level <= 1, \"ext.revision\"); uint32 per_group @expect(per_group > 0); };", Root = "sb", PointerSize = 8, Aligned = false, LittleEndian = true)]
public static partial class ExtSuperblockLayout
{
}
