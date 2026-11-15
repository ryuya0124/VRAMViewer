# WinUI 3 migration and maintenance — 2026-11-15

Replaced the Windows Forms interface with WinUI 3 on .NET 11 / Windows App SDK 2.6.5. The WinUI window provides GPU selection, process memory and icons, sortable columns, refresh interval, system/light/dark themes, JSON language packs, always-on-top, diagnostics, and process context actions. Release delivery is a portable folder with the Windows App SDK included.

## Correctness and security

- Device-wide fallback totals now come from GPU Adapter Memory counters. DXGI QueryVideoMemoryInfo describes the calling application's budget and usage, so it is no longer used as the GPU's total. See Microsoft's [DXGI_QUERY_VIDEO_MEMORY_INFO documentation](https://learn.microsoft.com/en-us/windows/win32/api/dxgi1_4/ns-dxgi1_4-dxgi_query_video_memory_info).
- Read each performance-counter category once per refresh instead of opening counters for every process. A valid zero Local Usage value is not replaced by a different counter's allocation value.
- Dedicated and shared memory stay separate on integrated GPUs. Adapter shared totals are used where available; process sums are explicitly marked as estimates when totals are unavailable.
- The residual is labelled unattributed memory, without claiming it all belongs to the kernel. NVML and Windows VidMm are different sources; see Microsoft's [Task Manager GPU explanation](https://devblogs.microsoft.com/directx/gpus-in-the-task-manager/).
- NVML is loaded by absolute path from System32 or NVIDIA's standard installation directory. Missing drivers do not fall back to searching the working directory or PATH. Ambiguous matching NVIDIA model names use Windows totals instead of selecting the first GPU.
- Process identity is re-read instead of caching names and paths indefinitely by PID. Ending a process verifies its creation time again after confirmation.
- Collection runs in the background with one active refresh. Changing GPU/language invalidates older samples, and UI updates use the WinUI dispatcher context. Icon caches are bounded and native images are released.
- Official dependency updates, a NuGet lock file, vulnerability failures during restore, pinned GitHub Actions and weekly Dependabot checks were added.

## Validation

`dotnet run --project Tests/CoreRegression -c Release -- --live`: seven checks passed. Tests cover memory-source selection (including valid zero values), dedicated/shared separation, residual accounting, the NVML ABI and growing process lists, live GPU isolation and trusted NVML loading. On this workstation, 20 live counter snapshots took about 4–5 ms total with no increase in handle count. This is a counter-collection measurement, not a complete UI benchmark.

The Release WinUI 3 build passed with zero compiler warnings and errors. The self-contained app started on Windows 11 with an NVIDIA RTX 5080 and AMD Radeon integrated GPU. GUI checks covered switching to AMD, live dedicated/shared totals, sorting by process name, the light theme and English translation. The light-theme caption-button contrast issue found during visual review was fixed.

The final NuGet query reported no outdated packages or known vulnerabilities, including transitive dependencies. GPU models other than the workstation's NVIDIA/AMD pair and destructive process termination were not exercised in the live check.
