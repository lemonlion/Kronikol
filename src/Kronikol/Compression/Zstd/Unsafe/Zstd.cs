namespace Kronikol.Compression.Zstd.Unsafe
{
    internal static unsafe partial class Methods
    {
        private static readonly ZSTD_customMem ZSTD_defaultCMem = new ZSTD_customMem(customAlloc: null, customFree: null, opaque: null);
    }
}