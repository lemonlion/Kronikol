namespace Kronikol.Compression.Zstd.Unsafe
{
    internal unsafe struct ZSTD_fseState
    {
        public nuint state;
        public ZSTD_seqSymbol* table;
    }
}