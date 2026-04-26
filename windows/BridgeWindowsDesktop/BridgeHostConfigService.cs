using System.Text.Json;

namespace BridgeWindowsDesktop;

internal sealed class BridgeHostConfigService
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true
    };

    public string HostRootDirectory => Path.Combine(AppContext.BaseDirectory, "Host");
    public string HostExecutablePath => Path.Combine(HostRootDirectory, "BridgeWindowsHost.exe");
    public string AppSettingsPath => Path.Combine(HostRootDirectory, "appsettings.json");

    public DesktopBridgeOptions Load()
    {
        if (!File.Exists(AppSettingsPath))
        {
            throw new FileNotFoundException("The copied BridgeWindowsHost appsettings.json file was not found.", AppSettingsPath);
        }

        var settings = JsonSerializer.Deserialize<DesktopBridgeAppSettings>(File.ReadAllText(AppSettingsPath), SerializerOptions);
        return settings?.Bridge ?? new DesktopBridgeOptions();
    }

    public string GetExpandedStorageRoot(DesktopBridgeOptions options)
    {
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.StorageRoot));
    }

    public string GetMaskedSecretStatus(DesktopBridgeOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.SharedSecret))
        {
            return "Missing";
        }

        if (string.Equals(options.SharedSecret, "change-this-secret", StringComparison.Ordinal))
        {
            return "Default placeholder (change before pairing)";
        }

        var suffix = options.SharedSecret.Length <= 4 ? options.SharedSecret : options.SharedSecret[^4..];
        return $"Configured ({options.SharedSecret.Length} chars, ending {suffix})";
    }
}
