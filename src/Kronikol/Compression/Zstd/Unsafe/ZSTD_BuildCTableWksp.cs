namespace Kronikol.Compression.Zstd.Unsafe
{
    internal unsafe struct ZSTD_BuildCTableWksp
    {
        public fixed short norm[53];
        public fixed uint wksp[285];
    }
}