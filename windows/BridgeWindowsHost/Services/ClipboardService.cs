using System.Text;
using BridgeWindowsHost.Models;

namespace BridgeWindowsHost.Services;

public sealed class ClipboardService(ProcessService processService)
{
    private readonly ProcessService _processService = processService;

    public async Task<ClipboardContentDto> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var text = await GetClipboardTextAsync(cancellationToken);
        return new ClipboardContentDto
        {
            Text = text,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public async Task<string> GetClipboardTextAsync(CancellationToken cancellationToken)
    {
        EnsureWindows();

        const string script = """
            $value = Get-Clipboard -Raw -Format Text -ErrorAction SilentlyContinue
            if ($null -eq $value) {
                [Console]::Out.Write("")
            } else {
                [Console]::Out.Write($value)
            }
            """;

        var result = await _processService.RunPowerShellCaptureAsync(script, cancellationToken);
        return result.ExitCode == 0 ? result.StandardOutput : string.Empty;
    }

    public async Task SetClipboardTextAsync(string text, CancellationToken cancellationToken)
    {
        EnsureWindows();

        var encoded = Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
        var script = $$"""
            $value = [System.Text.Encoding]::UTF8.GetString([System.Convert]::FromBase64String("{{encoded}}"))
            Set-Clipboard -Value $value
            [Console]::Out.Write("ok")
            """;

        var result = await _processService.RunPowerShellCaptureAsync(script, cancellationToken);
        if (result.ExitCode != 0)
        {
            throw new InvalidOperationException(result.StandardError);
        }
    }

    private static void EnsureWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Clipboard access is only supported on Windows.");
        }
    }
}
