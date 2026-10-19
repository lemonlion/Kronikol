namespace Kronikol.Compression.Zstd.Unsafe
{
    /* ======    Decompression    ====== */
    internal struct FSE_DTableHeader
    {
        public ushort tableLog;
        public ushort fastMode;
    }
}