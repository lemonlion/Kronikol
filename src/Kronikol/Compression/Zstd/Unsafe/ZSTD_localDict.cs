namespace Kronikol.Compression.Zstd.Unsafe
{
    internal unsafe struct ZSTD_localDict
    {
        public void* dictBuffer;
        public void* dict;
        public nuint dictSize;
        public ZSTD_dictContentType_e dictContentType;
        public ZSTD_CDict_s* cdict;
    }
}