using BridgeWindowsHost.Models;
using Microsoft.Extensions.Options;

namespace BridgeWindowsHost.Services;

public sealed class CommandRunnerService(ProcessService processService, IOptions<BridgeOptions> options)
{
    private readonly ProcessService _processService = processService;
    private readonly IOptions<BridgeOptions> _options = options;

    public CommandPreviewResponse Preview(CommandPreviewRequest request)
    {
        var normalizedCommand = request.Command.Trim();
        if (string.IsNullOrWhiteSpace(normalizedCommand))
        {
            return new CommandPreviewResponse
            {
                NormalizedCommand = string.Empty,
                Shell = request.Shell,
                Blocked = true,
                BlockedReason = "Command cannot be empty."
            };
        }

        var warnings = new List<string>();
        if (normalizedCommand.Contains(';') || normalizedCommand.Contains("&&") || normalizedCommand.Contains('|'))
        {
            warnings.Add("This command chains multiple operations together.");
        }

        if (normalizedCommand.Contains('>'))
        {
            warnings.Add("This command redirects output and may write to disk.");
        }

        if (normalizedCommand.Contains('*'))
        {
            warnings.Add("This command uses a wildcard and may affect more files than expected.");
        }

        if (normalizedCommand.Contains("curl", StringComparison.OrdinalIgnoreCase)
            || normalizedCommand.Contains("wget", StringComparison.OrdinalIgnoreCase)
            || normalizedCommand.Contains("Invoke-WebRequest", StringComparison.OrdinalIgnoreCase))
        {
            warnings.Add("This command reaches out to the network.");
        }

        if (normalizedCommand.Length > 200)
        {
            warnings.Add("This command is long enough that it deserves an extra review before running.");
        }

        var blockedToken = _options.Value.BlockedCommandTokens
            .FirstOrDefault(token => normalizedCommand.Contains(token, StringComparison.OrdinalIgnoreCase));

        return new CommandPreviewResponse
        {
            NormalizedCommand = normalizedCommand,
            Shell = request.Shell,
            Blocked = blockedToken is not null,
            BlockedReason = blockedToken is null ? null : $"Blocked by safety rule: \"{blockedToken}\"",
            Warnings = warnings
        };
    }

    public async Task<RunCommandResponse> RunAsync(RunCommandRequest request, CancellationToken cancellationToken)
    {
        if (!request.Confirmed)
        {
            throw new InvalidOperationException("Command execution requires an explicit confirmation.");
        }

        var preview = Preview(new CommandPreviewRequest
        {
            Command = request.Command,
            Shell = request.Shell
        });

        if (preview.Blocked)
        {
            throw new InvalidOperationException(preview.BlockedReason ?? "Command was blocked by the safety policy.");
        }

        var startedAt = DateTimeOffset.UtcNow;
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromSeconds(_options.Value.CommandTimeoutSeconds));

        var result = await _processService.RunUserCommandAsync(preview.NormalizedCommand, request.Shell, timeout.Token);
        return new RunCommandResponse
        {
            Command = preview.NormalizedCommand,
            Shell = request.Shell,
            StartedAt = startedAt,
            FinishedAt = DateTimeOffset.UtcNow,
            ExitCode = result.ExitCode,
            StandardOutput = result.StandardOutput,
            StandardError = result.StandardError,
            Warnings = preview.Warnings
        };
    }
}
