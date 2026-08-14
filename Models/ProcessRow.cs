namespace VramMonitor.Models
{
    public readonly struct ProcessRow
    {
        public ProcessRow(uint pid, ulong localBytes, ulong nonLocalBytes, bool isSystem = false)
        {
            Pid           = pid;
            LocalBytes    = localBytes;
            NonLocalBytes = nonLocalBytes;
            IsSystem      = isSystem;
        }

        public uint  Pid           { get; }
        public ulong LocalBytes    { get; }
        public ulong NonLocalBytes { get; }
        public bool  IsSystem      { get; }
        public ulong TotalBytes    => LocalBytes + NonLocalBytes;
    }
}
