using System.Diagnostics;
using System.Runtime.InteropServices;
using VramMonitor.Native;
using VramMonitor.Services;
using VramMonitor.Models;

[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.System32)]

int passed = 0, failed = 0;
void Test(string name, Action action)
{
    try { action(); passed++; Console.WriteLine($"PASS {name}"); }
    catch (Exception ex) { failed++; Console.WriteLine($"FAIL {name}: {ex.Message}"); }
}
void Check(bool condition, string message)
{
    if (!condition) throw new Exception(message);
}
Test("NVML native process layout", () =>
{
    Check(Marshal.SizeOf<Nvml.NvmlProcessInfo>() == 24, "v3 ABI size must be 24 bytes");
    Check(Marshal.OffsetOf<Nvml.NvmlProcessInfo>(nameof(Nvml.NvmlProcessInfo.UsedGpuMemory)).ToInt32() == 8, "memory alignment");
    Check(new Nvml.NvmlProcessInfo { UsedGpuMemory = ulong.MaxValue }.UsedGpuMemorySafe == 0, "unavailable sentinel");
});
Test("device totals preserve zero and separate iGPU shared memory", () =>
{
    var adapter = new AdapterInfo { IsIntegrated = true, DedicatedVideoMemory = 512, SharedSystemMemory = 8192 };
    var snapshot = new GpuProcessCollector.MemorySnapshot(new() { new ProcessRow(42, 100, 200) }, 0, 600);
    var summary = GpuMemorySummary.Create(snapshot, adapter, null);
    Check(summary.DedicatedUsed == 0 && summary.SharedUsed == 600 && summary.DedicatedCapacity == 512 && !summary.IsEstimate, "zero or shared memory incorrectly substituted for dedicated total");
});
Test("unattributed memory is not double counted", () =>
{
    var snapshot = new GpuProcessCollector.MemorySnapshot(new() { new ProcessRow(42, 100, 200), new ProcessRow(0, 400, 0, true) }, 500, 700);
    var summary = GpuMemorySummary.Create(snapshot, new AdapterInfo(), null);
    Check(summary.Unattributed == 400 && summary.DedicatedUsed == 500 && summary.SharedUsed == 700, "residual included as a second process allocation");
    var estimate = GpuMemorySummary.Create(snapshot with { DedicatedBytes = null, SharedBytes = null }, new AdapterInfo(), null);
    Check(estimate.IsEstimate && estimate.DedicatedUsed == 100 && estimate.SharedUsed == 200, "fallback must be labelled estimate and exclude synthetic rows");
});
Test("NVML growing process list retries", () =>
{
    int calls = 0;
    Nvml.NvmlReturn Growing(IntPtr _, ref uint count, Nvml.NvmlProcessInfo[]? rows)
    {
        calls++;
        if (rows == null) { count = 1; return Nvml.NvmlReturn.ErrorInsufficientSize; }
        if (calls == 2) { count = 20; return Nvml.NvmlReturn.ErrorInsufficientSize; }
        rows[0] = new() { Pid = 42, UsedGpuMemory = 1024 };
        count = 1;
        return Nvml.NvmlReturn.Success;
    }
    var actual = Nvml.GetRunningProcesses(Growing, IntPtr.Zero);
    Check(calls == 3 && actual.Length == 1 && actual[0].Pid == 42, "resized native list corrupted");
});
if (args.Contains("--live"))
{
Test("live adapters have separate GPU totals", () =>
{
    var adapters = DxgiHelper.GetAllAdapters();
    Check(adapters.Count >= 2, "this machine should expose NVIDIA and AMD adapters");
    foreach (var adapter in adapters)
    {
        var snapshot = GpuProcessCollector.CollectSnapshot(adapter);
        Check(snapshot.DedicatedBytes.HasValue && snapshot.SharedBytes.HasValue, "missing adapter memory counters: " + adapter.Name);
        Check(snapshot.Processes.Select(row => row.Pid).Distinct().Count() == snapshot.Processes.Count, "duplicate PID rows");
        Console.WriteLine($"  {adapter.Name}: dedicated={snapshot.DedicatedBytes}, shared={snapshot.SharedBytes}, processes={snapshot.Processes.Count}");
    }
    var absent = GpuProcessCollector.CollectSnapshot(new AdapterInfo { LuidHigh = int.MaxValue, LuidLow = uint.MaxValue });
    Check(absent.Processes.Count == 0 && !absent.DedicatedBytes.HasValue, "LUID filter included a different GPU");
});
Test("live counter refresh has bounded handles", () =>
{
    var adapter = DxgiHelper.GetAllAdapters().First();
    using var process = Process.GetCurrentProcess();
    GpuProcessCollector.CollectSnapshot(adapter);
    process.Refresh(); int before = process.HandleCount;
    var stopwatch = Stopwatch.StartNew();
    for (int i = 0; i < 20; i++) GpuProcessCollector.CollectSnapshot(adapter);
    stopwatch.Stop(); process.Refresh();
    Console.WriteLine($"  20 refreshes: {stopwatch.ElapsedMilliseconds} ms, handles {before} -> {process.HandleCount}");
    Check(process.HandleCount - before < 20, "native counter handles are leaking");
});
Test("NVML uses a trusted driver library", () =>
{
    var status = Nvml.TryInitialize();
    try
    {
        Check(status.Status == NvmlStatus.Ready, status.Message);
        using var process = Process.GetCurrentProcess();
        var loaded = process.Modules.Cast<ProcessModule>().Where(module => module.ModuleName.Equals("nvml.dll", StringComparison.OrdinalIgnoreCase)).Select(module => module.FileName).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        string system = Path.Combine(Environment.SystemDirectory, "nvml.dll");
        string nvsmi = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "NVIDIA Corporation", "NVSMI", "nvml.dll");
        Check(loaded.Length > 0, "no NVML module loaded");
        foreach (string actual in loaded)
        {
            Console.WriteLine("  " + actual);
            // The System32 NVML forwarder also loads the signed DriverStore copy.
            string driverStore = Path.Combine(Environment.SystemDirectory, "DriverStore", "FileRepository") + Path.DirectorySeparatorChar;
            Check(actual.Equals(system, StringComparison.OrdinalIgnoreCase) || actual.Equals(nvsmi, StringComparison.OrdinalIgnoreCase) || actual.StartsWith(driverStore, StringComparison.OrdinalIgnoreCase), "loaded untrusted DLL: " + actual);
        }
    }
    finally { if (status.Status == NvmlStatus.Ready) Nvml.Shutdown(); }
});
}
Console.WriteLine($"{passed} passed, {failed} failed");
return failed == 0 ? 0 : 1;
