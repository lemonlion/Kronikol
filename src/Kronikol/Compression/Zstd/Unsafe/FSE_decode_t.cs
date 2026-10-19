namespace Kronikol.Compression.Zstd.Unsafe
{
    internal struct FSE_decode_t
    {
        public ushort newState;
        public byte symbol;
        public byte nbBits;
    }
}