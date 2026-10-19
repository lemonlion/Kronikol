namespace Kronikol.Compression.Zstd.Unsafe
{
    internal unsafe struct ZSTD_compressedBlockState_t
    {
        public ZSTD_entropyCTables_t entropy;
        public fixed uint rep[3];
    }
}