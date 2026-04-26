using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using BridgeWindowsHost.Models;

namespace BridgeWindowsHost.Services;

public sealed class LocalNetworkService
{
    public BridgeStateDto GetState(BridgeOptions options, int connectedMacClients = 0)
    {
        return new BridgeStateDto
        {
            HostName = Dns.GetHostName(),
            AppVersion = "1.0.0",
            LocalAddresses = GetLocalAddresses(),
            Port = options.Port,
            StorageRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(options.StorageRoot)),
            WebSocketPath = "/ws",
            ConnectedMacClients = connectedMacClients
        };
    }

    private static IReadOnlyList<string> GetLocalAddresses()
    {
        return NetworkInterface.GetAllNetworkInterfaces()
            .Where(networkInterface =>
                networkInterface.OperationalStatus == OperationalStatus.Up
                && networkInterface.NetworkInterfaceType != NetworkInterfaceType.Loopback)
            .SelectMany(networkInterface => networkInterface.GetIPProperties().UnicastAddresses)
            .Where(unicastAddress => unicastAddress.Address.AddressFamily == AddressFamily.InterNetwork)
            .Select(unicastAddress => unicastAddress.Address.ToString())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(address => address, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }
}
