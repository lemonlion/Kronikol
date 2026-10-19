namespace Kronikol.Compression.Zstd.Unsafe
{
    internal struct blockProperties_t
    {
        public blockType_e blockType;
        public uint lastBlock;
        public uint origSize;
    }
}