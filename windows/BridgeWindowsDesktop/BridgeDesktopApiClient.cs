using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BridgeWindowsDesktop;

internal sealed class BridgeDesktopApiClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly HttpClient _httpClient = new()
    {
        Timeout = TimeSpan.FromSeconds(2)
    };

    public async Task<bool> IsHealthyAsync(DesktopBridgeOptions options, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _httpClient.GetAsync(BuildUri(options, "/api/health"), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<DesktopBridgeRuntimeSnapshot> GetRuntimeAsync(DesktopBridgeOptions options, CancellationToken cancellationToken)
    {
        var bridgeState = await GetAuthorizedJsonAsync<BridgeStateResponse>(options, "/api/bridge", cancellationToken);
        var controlState = await GetAuthorizedJsonAsync<ControlMacFromWindowsStateResponse>(options, "/api/input/control-mac", cancellationToken);
        return new DesktopBridgeRuntimeSnapshot(bridgeState, controlState);
    }

    public async Task SetControlMacFromWindowsAsync(DesktopBridgeOptions options, bool enabled, CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Post, BuildUri(options, "/api/input/control-mac"), options);
        request.Content = JsonContent.Create(new ControlMacFromWindowsRequest(enabled));

        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);
    }

    private async Task<T> GetAuthorizedJsonAsync<T>(DesktopBridgeOptions options, string path, CancellationToken cancellationToken)
    {
        using var request = CreateAuthorizedRequest(HttpMethod.Get, BuildUri(options, path), options);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        await EnsureSuccessAsync(response, cancellationToken);

        var payload = await response.Content.ReadFromJsonAsync<T>(SerializerOptions, cancellationToken);
        return payload ?? throw new InvalidOperationException($"The bridge returned an empty payload for {path}.");
    }

    private static HttpRequestMessage CreateAuthorizedRequest(HttpMethod method, Uri uri, DesktopBridgeOptions options)
    {
        var request = new HttpRequestMessage(method, uri);
        request.Headers.Add("X-Bridge-Secret", options.SharedSecret);
        return request;
    }

    private static async Task EnsureSuccessAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        if (response.IsSuccessStatusCode)
        {
            return;
        }

        var details = await response.Content.ReadAsStringAsync(cancellationToken);
        var message = string.IsNullOrWhiteSpace(details)
            ? $"The bridge returned HTTP {(int)response.StatusCode}."
            : details;

        throw new InvalidOperationException(message);
    }

    private static Uri BuildUri(DesktopBridgeOptions options, string path)
    {
        return new Uri($"http://127.0.0.1:{options.Port}{path}");
    }
}
