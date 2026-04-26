using System.Text.Json.Serialization;
using BridgeWindowsHost.Models;
using BridgeWindowsHost.Services;
using Microsoft.Extensions.Options;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.json", optional: false, reloadOnChange: true);

var bootstrapOptions = builder.Configuration.GetSection(BridgeOptions.SectionName).Get<BridgeOptions>() ?? new BridgeOptions();
builder.WebHost.UseUrls($"http://0.0.0.0:{bootstrapOptions.Port}");

builder.Services.Configure<BridgeOptions>(builder.Configuration.GetSection(BridgeOptions.SectionName));
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

builder.Services.AddSingleton<ProcessService>();
builder.Services.AddSingleton<BridgeSecretAuthorizer>();
builder.Services.AddSingleton<ClipboardService>();
builder.Services.AddSingleton<FileTransferService>();
builder.Services.AddSingleton<CommandRunnerService>();
builder.Services.AddSingleton<BridgeEventHub>();
builder.Services.AddSingleton<SystemStatusService>();
builder.Services.AddSingleton<LocalNetworkService>();
builder.Services.AddHostedService<BridgeBackgroundPublisher>();

var app = builder.Build();

app.UseWebSockets(new WebSocketOptions
{
    KeepAliveInterval = TimeSpan.FromSeconds(30)
});

app.MapGet("/api/health", () => Results.Ok(new
{
    status = "ok",
    appVersion = "1.0.0"
}));

// Everything under /api except the health check requires the shared secret header.
var api = app.MapGroup("/api");
api.AddEndpointFilter<BridgeSecretEndpointFilter>();

api.MapGet("/bridge", (IOptions<BridgeOptions> options, LocalNetworkService network) =>
{
    return Results.Ok(network.GetState(options.Value));
});

api.MapGet("/status", async (SystemStatusService statusService, CancellationToken cancellationToken) =>
{
    var status = await statusService.GetStatusAsync(cancellationToken);
    return Results.Ok(status);
});

api.MapGet("/clipboard", async (ClipboardService clipboardService, CancellationToken cancellationToken) =>
{
    var clipboard = await clipboardService.GetSnapshotAsync(cancellationToken);
    return Results.Ok(clipboard);
});

api.MapPost("/clipboard", async (SetClipboardRequest request, ClipboardService clipboardService, BridgeEventHub eventHub, CancellationToken cancellationToken) =>
{
    await clipboardService.SetClipboardTextAsync(request.Text, cancellationToken);

    var snapshot = await clipboardService.GetSnapshotAsync(cancellationToken) with
    {
        SourceDevice = request.SourceDevice ?? "mac"
    };

    await eventHub.BroadcastAsync("clipboard-updated", snapshot, cancellationToken);
    return Results.Ok(snapshot);
});

api.MapGet("/files", (FileTransferService fileTransferService) =>
{
    return Results.Ok(fileTransferService.ListFiles());
});

api.MapPost("/files/upload", async (HttpRequest request, FileTransferService fileTransferService, CancellationToken cancellationToken) =>
{
    if (!request.HasFormContentType)
    {
        return Results.BadRequest(new { error = "Expected multipart/form-data." });
    }

    var form = await request.ReadFormAsync(cancellationToken);
    var file = form.Files["file"];
    if (file is null)
    {
        return Results.BadRequest(new { error = "Missing file field." });
    }

    var saved = await fileTransferService.SaveUploadAsync(file, form["subdirectory"].ToString(), cancellationToken);
    return Results.Ok(saved);
});

api.MapGet("/files/download", (string relativePath, FileTransferService fileTransferService) =>
{
    try
    {
        var download = fileTransferService.OpenDownload(relativePath);
        return Results.File(download.Stream, "application/octet-stream", download.DownloadName);
    }
    catch (FileNotFoundException)
    {
        return Results.NotFound(new { error = "File not found." });
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
});

api.MapPost("/commands/preview", (CommandPreviewRequest request, CommandRunnerService commandRunner) =>
{
    return Results.Ok(commandRunner.Preview(request));
});

api.MapPost("/commands/run", async (RunCommandRequest request, CommandRunnerService commandRunner, BridgeEventHub eventHub, CancellationToken cancellationToken) =>
{
    try
    {
        var result = await commandRunner.RunAsync(request, cancellationToken);
        await eventHub.BroadcastAsync("command-completed", result, cancellationToken);
        return Results.Ok(result);
    }
    catch (InvalidOperationException exception)
    {
        return Results.BadRequest(new { error = exception.Message });
    }
    catch (OperationCanceledException)
    {
        return Results.StatusCode(StatusCodes.Status504GatewayTimeout);
    }
});

// The WebSocket is used for live status and clipboard events so the Mac app does not need to poll aggressively.
app.Map("/ws", async (HttpContext context, BridgeSecretAuthorizer authorizer, BridgeEventHub eventHub) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        await context.Response.WriteAsJsonAsync(new { error = "Expected a WebSocket upgrade request." });
        return;
    }

    if (!authorizer.IsAuthorized(context))
    {
        context.Response.StatusCode = StatusCodes.Status401Unauthorized;
        await context.Response.WriteAsJsonAsync(new { error = "Missing or invalid bridge secret." });
        return;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    await eventHub.HandleClientAsync(webSocket, context.RequestAborted);
});

app.Lifetime.ApplicationStarted.Register(() =>
{
    using var scope = app.Services.CreateScope();
    var options = scope.ServiceProvider.GetRequiredService<IOptions<BridgeOptions>>().Value;
    var network = scope.ServiceProvider.GetRequiredService<LocalNetworkService>();
    var bridgeState = network.GetState(options);

    Console.WriteLine("BridgeWindowsHost is listening on:");
    foreach (var address in bridgeState.LocalAddresses)
    {
        Console.WriteLine($"  http://{address}:{options.Port}");
    }

    Console.WriteLine($"Shared folder: {bridgeState.StorageRoot}");
    Console.WriteLine("Change the shared secret in appsettings.json before connecting from the Mac client.");
});

await app.RunAsync();
