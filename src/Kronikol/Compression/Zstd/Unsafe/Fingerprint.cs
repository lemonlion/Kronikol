namespace Kronikol.Compression.Zstd.Unsafe
{
    internal unsafe struct Fingerprint
    {
        public fixed uint events[1024];
        public nuint nbEvents;
    }
}