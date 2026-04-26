using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BridgeWindowsHost.Services;

public sealed class BridgeEventHub
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() },
        WriteIndented = false
    };

    private readonly ConcurrentDictionary<Guid, SocketConnection> _connections = new();

    public bool HasConnections => !_connections.IsEmpty;

    public async Task HandleClientAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var connection = new SocketConnection(socket);
        var connectionId = Guid.NewGuid();
        _connections[connectionId] = connection;

        try
        {
            await connection.ReceiveUntilClosedAsync(cancellationToken);
        }
        finally
        {
            _connections.TryRemove(connectionId, out _);
            await connection.DisposeAsync();
        }
    }

    public async Task BroadcastAsync(string eventType, object payload, CancellationToken cancellationToken)
    {
        if (_connections.IsEmpty)
        {
            return;
        }

        var envelope = new
        {
            type = eventType,
            occurredAt = DateTimeOffset.UtcNow,
            payload
        };

        var message = JsonSerializer.Serialize(envelope, SerializerOptions);
        var staleConnections = new List<Guid>();

        foreach (var (connectionId, connection) in _connections)
        {
            if (!await connection.TrySendAsync(message, cancellationToken))
            {
                staleConnections.Add(connectionId);
            }
        }

        foreach (var staleConnectionId in staleConnections)
        {
            if (_connections.TryRemove(staleConnectionId, out var staleConnection))
            {
                await staleConnection.DisposeAsync();
            }
        }
    }

    private sealed class SocketConnection(WebSocket socket) : IAsyncDisposable
    {
        private readonly WebSocket _socket = socket;
        private readonly SemaphoreSlim _sendLock = new(1, 1);

        public async Task<bool> TrySendAsync(string message, CancellationToken cancellationToken)
        {
            if (_socket.State != WebSocketState.Open)
            {
                return false;
            }

            var messageBytes = Encoding.UTF8.GetBytes(message);
            await _sendLock.WaitAsync(cancellationToken);
            try
            {
                await _socket.SendAsync(messageBytes, WebSocketMessageType.Text, true, cancellationToken);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                _sendLock.Release();
            }
        }

        public async Task ReceiveUntilClosedAsync(CancellationToken cancellationToken)
        {
            var buffer = new byte[1024];
            while (!cancellationToken.IsCancellationRequested && _socket.State == WebSocketState.Open)
            {
                var result = await _socket.ReceiveAsync(buffer, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }
            }
        }

        public async ValueTask DisposeAsync()
        {
            _sendLock.Dispose();

            if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Bridge closed", CancellationToken.None);
                }
                catch
                {
                    // Ignore socket cleanup errors during shutdown.
                }
            }

            _socket.Dispose();
        }
    }
}
