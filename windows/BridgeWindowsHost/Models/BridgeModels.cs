namespace BridgeWindowsHost.Models;

public sealed record BridgeStateDto
{
    public string HostName { get; init; } = string.Empty;
    public string AppVersion { get; init; } = "1.0.0";
    public IReadOnlyList<string> LocalAddresses { get; init; } = Array.Empty<string>();
    public int Port { get; init; }
    public string StorageRoot { get; init; } = string.Empty;
    public string WebSocketPath { get; init; } = "/ws";
    public int ConnectedMacClients { get; init; }
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

public enum InputBridgeEventKind
{
    MouseMove,
    MouseButton,
    Scroll,
    Key
}

public enum InputBridgeMouseButton
{
    Left,
    Right,
    Middle
}

public sealed record InputBridgeEventDto
{
    public InputBridgeEventKind Kind { get; init; }
    public int DeltaX { get; init; }
    public int DeltaY { get; init; }
    public InputBridgeMouseButton? Button { get; init; }
    public bool? IsDown { get; init; }
    public int ScrollX { get; init; }
    public int ScrollY { get; init; }
    public ushort? WindowsVirtualKey { get; init; }
}

public sealed record InputBridgeSocketMessage
{
    public string Type { get; init; } = string.Empty;
    public InputBridgeEventDto? Payload { get; init; }
}

public enum ControlMacBridgePhase
{
    Off,
    Armed,
    Active
}

public sealed record ControlMacFromWindowsRequest
{
    public bool Enabled { get; init; }
}

public sealed record ControlMacFromWindowsStateDto
{
    public bool Enabled { get; init; }
    public ControlMacBridgePhase Phase { get; init; }
    public string ActivationEdge { get; init; } = "Right";
    public string EscapeHotkey { get; init; } = "Ctrl + Alt + Windows + Esc";
    public bool RequiresMacAccessibilityPermission { get; init; } = true;
}
