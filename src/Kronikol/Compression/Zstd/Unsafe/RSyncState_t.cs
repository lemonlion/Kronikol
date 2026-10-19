namespace Kronikol.Compression.Zstd.Unsafe
{
    internal struct RSyncState_t
    {
        public ulong hash;
        public ulong hitMask;
        public ulong primePower;
    }
}