using System.Globalization;
using System.Runtime.InteropServices;
using System.Text.Json;
using BridgeWindowsHost.Models;

namespace BridgeWindowsHost.Services;

public sealed class SystemStatusService(ProcessService processService)
{
    private readonly ProcessService _processService = processService;

    public async Task<StatusSnapshotDto> GetStatusAsync(CancellationToken cancellationToken)
    {
        EnsureWindows();

        var cpuLoad = await GetCpuLoadPercentAsync(cancellationToken);
        var memory = await GetMemoryAsync(cancellationToken);
        var gpu = await GetGpuAsync(cancellationToken);
        var disk = GetPrimaryDisk();

        return new StatusSnapshotDto
        {
            HostName = Environment.MachineName,
            OperatingSystem = RuntimeInformation.OSDescription,
            SampledAt = DateTimeOffset.UtcNow,
            CpuLoadPercent = cpuLoad,
            MemoryUsedGb = memory.UsedGb,
            MemoryTotalGb = memory.TotalGb,
            DiskUsedGb = disk.UsedGb,
            DiskTotalGb = disk.TotalGb,
            DiskFreeGb = disk.FreeGb,
            Gpu = gpu
        };
    }

    private async Task<double> GetCpuLoadPercentAsync(CancellationToken cancellationToken)
    {
        const string script = """
            $value = (Get-CimInstance Win32_Processor | Measure-Object -Property LoadPercentage -Average).Average
            if ($null -eq $value) { $value = 0 }
            [Console]::Out.Write([math]::Round($value, 2))
            """;

        var result = await _processService.RunPowerShellCaptureAsync(script, cancellationToken);
        return ParseDouble(result.StandardOutput);
    }

    private async Task<MemorySnapshot> GetMemoryAsync(CancellationToken cancellationToken)
    {
        const string script = """
            $os = Get-CimInstance Win32_OperatingSystem
            @{
              totalGb = [math]::Round($os.TotalVisibleMemorySize / 1MB, 2)
              freeGb = [math]::Round($os.FreePhysicalMemory / 1MB, 2)
            } | ConvertTo-Json -Compress
            """;

        var result = await _processService.RunPowerShellCaptureAsync(script, cancellationToken);
        var memory = JsonSerializer.Deserialize<MemoryPowerShellDto>(result.StandardOutput) ?? new MemoryPowerShellDto();

        return new MemorySnapshot(memory.TotalGb - memory.FreeGb, memory.TotalGb);
    }

    private async Task<GpuSnapshotDto?> GetGpuAsync(CancellationToken cancellationToken)
    {
        const string script = """
            $gpu = Get-CimInstance Win32_VideoController | Select-Object -First 1 Name, AdapterRAM
            if ($null -eq $gpu) {
              [Console]::Out.Write("")
            } else {
              @{
                name = $gpu.Name
                memoryGb = if ($gpu.AdapterRAM) { [math]::Round($gpu.AdapterRAM / 1GB, 2) } else { $null }
              } | ConvertTo-Json -Compress
            }
            """;

        var result = await _processService.RunPowerShellCaptureAsync(script, cancellationToken);
        if (string.IsNullOrWhiteSpace(result.StandardOutput))
        {
            return null;
        }

        var gpu = JsonSerializer.Deserialize<GpuPowerShellDto>(result.StandardOutput);
        if (gpu is null || string.IsNullOrWhiteSpace(gpu.Name))
        {
            return null;
        }

        return new GpuSnapshotDto
        {
            Name = gpu.Name,
            MemoryGb = gpu.MemoryGb,
            LoadPercent = null
        };
    }

    private static DiskSnapshot GetPrimaryDisk()
    {
        var systemRoot = Path.GetPathRoot(Environment.SystemDirectory) ?? "C:\\";
        var driveInfo = new DriveInfo(systemRoot);
        var totalGb = BytesToGigabytes(driveInfo.TotalSize);
        var freeGb = BytesToGigabytes(driveInfo.AvailableFreeSpace);
        return new DiskSnapshot(totalGb - freeGb, totalGb, freeGb);
    }

    private static double BytesToGigabytes(long value)
    {
        return Math.Round(value / 1024d / 1024d / 1024d, 2);
    }

    private static double ParseDouble(string value)
    {
        return double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
            ? parsed
            : 0;
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("System metrics are only supported on Windows.");
        }
    }

    private sealed record MemoryPowerShellDto
    {
        public double TotalGb { get; init; }
        public double FreeGb { get; init; }
    }

    private sealed record GpuPowerShellDto
    {
        public string Name { get; init; } = string.Empty;
        public double? MemoryGb { get; init; }
    }

    private sealed record MemorySnapshot(double UsedGb, double TotalGb);
    private sealed record DiskSnapshot(double UsedGb, double TotalGb, double FreeGb);
}
