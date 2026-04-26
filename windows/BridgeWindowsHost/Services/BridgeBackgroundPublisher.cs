using BridgeWindowsHost.Models;
using Microsoft.Extensions.Options;

namespace BridgeWindowsHost.Services;

public sealed class BridgeBackgroundPublisher(
    SystemStatusService systemStatusService,
    ClipboardService clipboardService,
    BridgeEventHub eventHub,
    IOptions<BridgeOptions> options,
    ILogger<BridgeBackgroundPublisher> logger) : BackgroundService
{
    private readonly SystemStatusService _systemStatusService = systemStatusService;
    private readonly ClipboardService _clipboardService = clipboardService;
    private readonly BridgeEventHub _eventHub = eventHub;
    private readonly IOptions<BridgeOptions> _options = options;
    private readonly ILogger<BridgeBackgroundPublisher> _logger = logger;
    private string _lastClipboardText = string.Empty;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.WhenAll(
            RunStatusLoopAsync(stoppingToken),
            RunClipboardLoopAsync(stoppingToken));
    }

    private async Task RunStatusLoopAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.Value.StatusBroadcastSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var status = await _systemStatusService.GetStatusAsync(stoppingToken);
                await _eventHub.BroadcastAsync("status-updated", status, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Failed to publish status update.");
            }
        }
    }

    private async Task RunClipboardLoopAsync(CancellationToken stoppingToken)
    {
        try
        {
            _lastClipboardText = await _clipboardService.GetClipboardTextAsync(stoppingToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _logger.LogWarning(exception, "Failed to read the initial clipboard snapshot.");
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(_options.Value.ClipboardPollSeconds));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                var currentClipboard = await _clipboardService.GetClipboardTextAsync(stoppingToken);
                // Only publish clipboard events when the text actually changed to keep the stream quiet.
                if (currentClipboard == _lastClipboardText)
                {
                    continue;
                }

                _lastClipboardText = currentClipboard;
                var payload = new ClipboardContentDto
                {
                    Text = currentClipboard,
                    UpdatedAt = DateTimeOffset.UtcNow,
                    SourceDevice = "windows"
                };

                await _eventHub.BroadcastAsync("clipboard-updated", payload, stoppingToken);
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                _logger.LogWarning(exception, "Failed to publish clipboard update.");
            }
        }
    }
}
