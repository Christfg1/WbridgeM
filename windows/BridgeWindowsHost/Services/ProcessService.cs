using System.Diagnostics;
using System.Text;
using BridgeWindowsHost.Models;

namespace BridgeWindowsHost.Services;

public sealed class ProcessService(ILogger<ProcessService> logger)
{
    private readonly ILogger<ProcessService> _logger = logger;

    public Task<ProcessExecutionResult> RunPowerShellCaptureAsync(string script, CancellationToken cancellationToken)
    {
        var encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(script));
        return RunAsync("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -EncodedCommand {encodedCommand}", cancellationToken);
    }

    public Task<ProcessExecutionResult> RunUserCommandAsync(string command, CommandShell shell, CancellationToken cancellationToken)
    {
        return shell switch
        {
            CommandShell.Cmd => RunAsync("cmd.exe", $"/c {command}", cancellationToken),
            _ => RunPowerShellCaptureAsync(command, cancellationToken)
        };
    }

    private async Task<ProcessExecutionResult> RunAsync(string fileName, string arguments, CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = new Process
        {
            StartInfo = startInfo,
            EnableRaisingEvents = true
        };

        _logger.LogDebug("Starting process {FileName} {Arguments}", fileName, arguments);

        if (!process.Start())
        {
            throw new InvalidOperationException($"Failed to start process: {fileName}");
        }

        var outputTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var errorTask = process.StandardError.ReadToEndAsync(cancellationToken);

        try
        {
            await process.WaitForExitAsync(cancellationToken);
        }
        catch
        {
            TryKill(process);
            throw;
        }

        var standardOutput = await outputTask;
        var standardError = await errorTask;
        return new ProcessExecutionResult(process.ExitCode, standardOutput, standardError);
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch
        {
            // Best effort cleanup for timed-out or canceled commands.
        }
    }
}

public sealed record ProcessExecutionResult(int ExitCode, string StandardOutput, string StandardError);
