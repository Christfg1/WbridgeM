using System.Diagnostics;

namespace BridgeWindowsDesktop;

internal sealed class BridgeHostProcessManager(BridgeHostConfigService configService)
{
    private readonly BridgeHostConfigService _configService = configService;
    private Process? _process;

    public string? LastProcessMessage { get; private set; }

    public bool IsManagedHostRunning
    {
        get
        {
            EnsureProcessState();
            return _process is { HasExited: false };
        }
    }

    public async Task StartAsync(CancellationToken cancellationToken = default)
    {
        if (IsManagedHostRunning)
        {
            return;
        }

        if (!File.Exists(_configService.HostExecutablePath))
        {
            throw new FileNotFoundException("BridgeWindowsHost.exe was not found next to the desktop UI output.", _configService.HostExecutablePath);
        }

        LastProcessMessage = null;

        var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _configService.HostExecutablePath,
                WorkingDirectory = _configService.HostRootDirectory,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            },
            EnableRaisingEvents = true
        };

        process.OutputDataReceived += HandleOutput;
        process.ErrorDataReceived += HandleOutput;

        if (!process.Start())
        {
            process.Dispose();
            throw new InvalidOperationException("BridgeWindowsHost could not be started.");
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        _process = process;

        try
        {
            await Task.Delay(350, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            await StopAsync(CancellationToken.None);
            throw;
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        EnsureProcessState();
        var process = _process;
        if (process is null)
        {
            return;
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(cancellationToken);
            }
        }
        finally
        {
            process.Dispose();
            if (ReferenceEquals(_process, process))
            {
                _process = null;
            }
        }
    }

    private void EnsureProcessState()
    {
        if (_process is not { HasExited: true })
        {
            return;
        }

        LastProcessMessage ??= $"BridgeWindowsHost exited with code {_process.ExitCode}.";
        _process.Dispose();
        _process = null;
    }

    private void HandleOutput(object sender, DataReceivedEventArgs eventArgs)
    {
        if (!string.IsNullOrWhiteSpace(eventArgs.Data))
        {
            LastProcessMessage = eventArgs.Data;
        }
    }
}
