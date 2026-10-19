using System;
using Kronikol.Compression.Zstd.Unsafe;

namespace Kronikol.Compression.Zstd
{
    internal class ZstdException : Exception
    {
        public ZstdException(ZSTD_ErrorCode code, string message) : base(message)
            => Code = code;

        public ZSTD_ErrorCode Code { get; }
    }
}
