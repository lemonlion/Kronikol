namespace Kronikol.Compression.Zstd.Unsafe
{
    /*-***************************/
    /*  generic DTableDesc       */
    /*-***************************/
    internal struct DTableDesc
    {
        public byte maxTableLog;
        public byte tableType;
        public byte tableLog;
        public byte reserved;
    }
}