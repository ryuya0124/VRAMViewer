using VramMonitor.Native;

namespace VramMonitor.Services;

internal sealed record GpuMemorySummary(ulong DedicatedUsed, ulong DedicatedCapacity, ulong SharedUsed, ulong Unattributed, bool IsEstimate)
{
    internal static GpuMemorySummary Create(GpuProcessCollector.MemorySnapshot snapshot, AdapterInfo adapter, Nvml.NvmlMemory? nvidia)
    {
        ulong processDedicated = 0, processShared = 0;
        foreach (var row in snapshot.Processes.Where(row => !row.IsSystem))
        {
            processDedicated += row.LocalBytes;
            processShared += row.NonLocalBytes;
        }
        ulong used = nvidia?.Used ?? snapshot.DedicatedBytes ?? processDedicated;
        return new(used, nvidia?.Total ?? adapter.DedicatedVideoMemory,
            snapshot.SharedBytes ?? processShared, used > processDedicated ? used - processDedicated : 0,
            !nvidia.HasValue && !snapshot.DedicatedBytes.HasValue);
    }
}
