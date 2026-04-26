using System.IO;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using BridgeWindowsHost.Models;

namespace BridgeWindowsHost.Services;

public sealed class InputBridgeSocketService(
    InputInjectionService inputInjectionService,
    ILogger<InputBridgeSocketService> logger)
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly InputInjectionService _inputInjectionService = inputInjectionService;
    private readonly ILogger<InputBridgeSocketService> _logger = logger;

    public async Task HandleClientAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        var sessionStarted = false;

        try
        {
            // Keep the input session bound to the lifetime of this socket so disconnects release held state.
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var messageText = await ReceiveTextMessageAsync(socket, cancellationToken);
                if (messageText is null)
                {
                    break;
                }

                var message = JsonSerializer.Deserialize<InputBridgeSocketMessage>(messageText, SerializerOptions);
                if (message is null)
                {
                    continue;
                }

                switch (message.Type)
                {
                    case "input-bridge-begin":
                        _inputInjectionService.BeginSession();
                        sessionStarted = true;
                        _logger.LogInformation("Input Bridge session started.");
                        break;

                    case "input-bridge-end":
                        _inputInjectionService.EndSession();
                        sessionStarted = false;
                        _logger.LogInformation("Input Bridge session ended.");
                        break;

                    case "input-bridge-event":
                        if (message.Payload is null)
                        {
                            break;
                        }

                        if (!sessionStarted)
                        {
                            _inputInjectionService.BeginSession();
                            sessionStarted = true;
                        }

                        _inputInjectionService.Inject(message.Payload);
                        break;
                }
            }
        }
        finally
        {
            if (sessionStarted)
            {
                _inputInjectionService.EndSession();
            }

            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                try
                {
                    await socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Input Bridge closed", CancellationToken.None);
                }
                catch
                {
                    // Ignore cleanup failures during shutdown.
                }
            }
        }
    }

    private static async Task<string?> ReceiveTextMessageAsync(WebSocket socket, CancellationToken cancellationToken)
    {
        using var memoryStream = new MemoryStream();
        var buffer = new byte[4096];

        while (true)
        {
            var result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                continue;
            }

            memoryStream.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(memoryStream.ToArray());
            }
        }
    }
}
