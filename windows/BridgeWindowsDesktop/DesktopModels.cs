using System.Text.Json.Serialization;

namespace BridgeWindowsDesktop;

internal sealed record DesktopBridgeAppSettings
{
    public DesktopBridgeOptions Bridge { get; init; } = new();
}

internal sealed record DesktopBridgeOptions
{
    public int Port { get; init; } = 5055;
    public string SharedSecret { get; init; } = "change-this-secret";
    public string StorageRoot { get; init; } = "%USERPROFILE%\\Documents\\BridgeDrop";
}

internal enum MacScreenPosition
{
    LeftOfWindowsMonitor,
    RightOfWindowsMonitor,
    AboveWindowsMonitor,
    BelowWindowsMonitor
}

internal sealed record DesktopLocalSettings
{
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MacScreenPosition MacScreenPosition { get; init; } = MacScreenPosition.LeftOfWindowsMonitor;
}

internal sealed record HealthResponse
{
    public string Status { get; init; } = string.Empty;
}

internal sealed record BridgeStateResponse
{
    public string HostName { get; init; } = string.Empty;
    public string AppVersion { get; init; } = "1.0.0";
    public IReadOnlyList<string> LocalAddresses { get; init; } = Array.Empty<string>();
    public int Port { get; init; }
    public string StorageRoot { get; init; } = string.Empty;
    public string WebSocketPath { get; init; } = "/ws";
    public int ConnectedMacClients { get; init; }
}

internal enum ControlMacBridgePhase
{
    Off,
    Armed,
    Active
}

internal sealed record ControlMacFromWindowsStateResponse
{
    public bool Enabled { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public ControlMacBridgePhase Phase { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MacScreenPosition ScreenPosition { get; init; } = MacScreenPosition.LeftOfWindowsMonitor;

    public string ActivationEdge { get; init; } = "Left";
    public string EscapeHotkey { get; init; } = "Ctrl + Alt + Windows + Esc";
    public bool RequiresMacAccessibilityPermission { get; init; } = true;
}

internal sealed record ControlMacFromWindowsRequest
{
    public bool Enabled { get; init; }

    [JsonConverter(typeof(JsonStringEnumConverter))]
    public MacScreenPosition? ScreenPosition { get; init; }
}

internal sealed record DesktopBridgeRuntimeSnapshot(
    BridgeStateResponse BridgeState,
    ControlMacFromWindowsStateResponse ControlState);
