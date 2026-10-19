namespace Kronikol.Compression.Zstd.Unsafe
{
    internal unsafe struct SeqCollector
    {
        public int collectSequences;
        public ZSTD_Sequence* seqStart;
        public nuint seqIndex;
        public nuint maxSequences;
    }
}