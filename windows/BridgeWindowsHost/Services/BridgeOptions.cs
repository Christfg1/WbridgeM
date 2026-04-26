namespace BridgeWindowsHost.Services;

public sealed class BridgeOptions
{
    public const string SectionName = "Bridge";

    public int Port { get; set; } = 5055;
    public string SharedSecret { get; set; } = "change-this-secret";
    public string StorageRoot { get; set; } = "%USERPROFILE%\\Documents\\BridgeDrop";
    public int StatusBroadcastSeconds { get; set; } = 5;
    public int ClipboardPollSeconds { get; set; } = 2;
    public int CommandTimeoutSeconds { get; set; } = 90;
    public string[] BlockedCommandTokens { get; set; } =
    [
        "format ",
        "diskpart",
        "shutdown",
        "restart-computer",
        "stop-computer",
        "remove-item -recurse",
        "del /s",
        "rmdir /s",
        "cipher /w"
    ];
}
