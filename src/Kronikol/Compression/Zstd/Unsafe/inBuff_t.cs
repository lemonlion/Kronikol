namespace Kronikol.Compression.Zstd.Unsafe
{
    /* ------------------------------------------ */
    /* =====   Multi-threaded compression   ===== */
    /* ------------------------------------------ */
    internal struct InBuff_t
    {
        /* read-only non-owned prefix buffer */
        public Range prefix;
        public buffer_s buffer;
        public nuint filled;
    }
}