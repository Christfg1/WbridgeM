using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace BridgeWindowsHost.Services;

public sealed class BridgeSecretAuthorizer(IOptions<BridgeOptions> options)
{
    private readonly IOptions<BridgeOptions> _options = options;

    public bool IsAuthorized(HttpContext context)
    {
        var configuredSecret = _options.Value.SharedSecret?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(configuredSecret))
        {
            return false;
        }

        var suppliedSecret = context.Request.Headers["X-Bridge-Secret"].FirstOrDefault()
            ?? context.Request.Query["secret"].FirstOrDefault()
            ?? string.Empty;

        suppliedSecret = suppliedSecret.Trim();
        if (string.IsNullOrWhiteSpace(suppliedSecret))
        {
            return false;
        }

        var expectedBytes = Encoding.UTF8.GetBytes(configuredSecret);
        var actualBytes = Encoding.UTF8.GetBytes(suppliedSecret);

        return expectedBytes.Length == actualBytes.Length
            && CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }
}

public sealed class BridgeSecretEndpointFilter(BridgeSecretAuthorizer authorizer) : IEndpointFilter
{
    private readonly BridgeSecretAuthorizer _authorizer = authorizer;

    public async ValueTask<object?> InvokeAsync(EndpointFilterInvocationContext context, EndpointFilterDelegate next)
    {
        if (!_authorizer.IsAuthorized(context.HttpContext))
        {
            return Results.Json(new { error = "Missing or invalid bridge secret." }, statusCode: StatusCodes.Status401Unauthorized);
        }

        return await next(context);
    }
}
