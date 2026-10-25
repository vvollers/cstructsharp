namespace CStructSharp.Benchmarks.GeneratedLayouts;

/// <summary>The same 24-byte superblock without any constraint: the reference for what a constraint-free read costs.</summary>
[CStructLayout("struct sb { uint32 inodes; uint32 blocks; uint16 magic; uint16 state; uint32 log_block_size; uint32 rev_level; uint32 per_group; };", Root = "sb", PointerSize = 8, Aligned = false, LittleEndian = true)]
public static partial class PlainSuperblockLayout
{
}
