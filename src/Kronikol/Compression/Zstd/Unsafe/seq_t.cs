namespace Kronikol.Compression.Zstd.Unsafe
{
    internal struct seq_t
    {
        public nuint litLength;
        public nuint matchLength;
        public nuint offset;
    }
}