namespace Kronikol.Compression.Zstd.Unsafe
{
    /*!
     * @internal
     * @brief Enum to indicate whether a pointer is aligned.
     */
    internal enum XXH_alignment
    {
        /*!< Aligned */
        XXH_aligned,
        /*!< Possibly unaligned */
        XXH_unaligned
    }
}