namespace Kronikol.Compression.Zstd.Unsafe
{
    internal unsafe struct FSE_DecompressWksp
    {
        public fixed short ncount[256];
    }
}