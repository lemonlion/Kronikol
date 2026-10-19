using System.Runtime.InteropServices;

namespace Kronikol.Compression.Zstd.Unsafe
{
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    internal unsafe delegate nuint ZSTD_BlockCompressor_f(ZSTD_MatchState_t* bs, SeqStore_t* seqStore, uint* rep, void* src, nuint srcSize);
}