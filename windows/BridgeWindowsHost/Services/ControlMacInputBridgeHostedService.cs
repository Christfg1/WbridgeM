namespace BridgeWindowsHost.Services;

public sealed class ControlMacInputBridgeHostedService(ControlMacInputBridgeService service) : IHostedService
{
    private readonly ControlMacInputBridgeService _service = service;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        return _service.StartAsync(cancellationToken);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return _service.StopAsync(cancellationToken);
    }
}
