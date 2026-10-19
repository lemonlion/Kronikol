namespace Kronikol.Compression.Zstd.Unsafe
{
    internal struct BlockSummary
    {
        public nuint nbSequences;
        public nuint blockSize;
        public nuint litSize;
    }
}