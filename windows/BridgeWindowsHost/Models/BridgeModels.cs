namespace BridgeWindowsHost.Models;

public sealed record BridgeStateDto
{
    public string HostName { get; init; } = string.Empty;
    public string AppVersion { get; init; } = "1.0.0";
    public IReadOnlyList<string> LocalAddresses { get; init; } = Array.Empty<string>();
    public string StorageRoot { get; init; } = string.Empty;
    public string WebSocketPath { get; init; } = "/ws";
}

public sealed record StatusSnapshotDto
{
    public string HostName { get; init; } = string.Empty;
    public string OperatingSystem { get; init; } = string.Empty;
    public DateTimeOffset SampledAt { get; init; }
    public double CpuLoadPercent { get; init; }
    public double MemoryUsedGb { get; init; }
    public double MemoryTotalGb { get; init; }
    public double DiskUsedGb { get; init; }
    public double DiskTotalGb { get; init; }
    public double DiskFreeGb { get; init; }
    public GpuSnapshotDto? Gpu { get; init; }
}

public sealed record GpuSnapshotDto
{
    public string Name { get; init; } = string.Empty;
    public double? MemoryGb { get; init; }
    public double? LoadPercent { get; init; }
}

public sealed record ClipboardContentDto
{
    public string Text { get; init; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; init; }
    public string? SourceDevice { get; init; }
}

public sealed record SetClipboardRequest
{
    public string Text { get; init; } = string.Empty;
    public string? SourceDevice { get; init; }
}

public sealed record FileEntryDto
{
    public string RelativePath { get; init; } = string.Empty;
    public bool IsDirectory { get; init; }
    public long? SizeBytes { get; init; }
    public DateTimeOffset LastModifiedAt { get; init; }
}

public sealed record FileListDto
{
    public string RootDirectory { get; init; } = string.Empty;
    public IReadOnlyList<FileEntryDto> Entries { get; init; } = Array.Empty<FileEntryDto>();
}

public sealed record UploadFileResponse
{
    public string RelativePath { get; init; } = string.Empty;
    public long SizeBytes { get; init; }
    public DateTimeOffset UploadedAt { get; init; }
}

public enum CommandShell
{
    PowerShell,
    Cmd
}

public sealed record CommandPreviewRequest
{
    public string Command { get; init; } = string.Empty;
    public CommandShell Shell { get; init; } = CommandShell.PowerShell;
}

public sealed record CommandPreviewResponse
{
    public string NormalizedCommand { get; init; } = string.Empty;
    public CommandShell Shell { get; init; } = CommandShell.PowerShell;
    public bool Blocked { get; init; }
    public string? BlockedReason { get; init; }
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}

public sealed record RunCommandRequest
{
    public string Command { get; init; } = string.Empty;
    public CommandShell Shell { get; init; } = CommandShell.PowerShell;
    public bool Confirmed { get; init; }
}

public sealed record RunCommandResponse
{
    public string Command { get; init; } = string.Empty;
    public CommandShell Shell { get; init; } = CommandShell.PowerShell;
    public DateTimeOffset StartedAt { get; init; }
    public DateTimeOffset FinishedAt { get; init; }
    public int ExitCode { get; init; }
    public string StandardOutput { get; init; } = string.Empty;
    public string StandardError { get; init; } = string.Empty;
    public IReadOnlyList<string> Warnings { get; init; } = Array.Empty<string>();
}
