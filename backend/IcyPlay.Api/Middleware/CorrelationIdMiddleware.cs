using IcyPlay.Api.Common;

namespace IcyPlay.Api.Middleware;

public sealed class CorrelationIdMiddleware
{
    private readonly RequestDelegate _next;

    public CorrelationIdMiddleware(RequestDelegate next)
    {
        _next = next;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        var correlationId = ResolveCorrelationId(context);

        context.Items[ApiConstants.CorrelationIdHeaderName] = correlationId;
        context.Response.Headers[ApiConstants.CorrelationIdHeaderName] = correlationId;

        using var scope = context.RequestServices
            .GetRequiredService<ILogger<CorrelationIdMiddleware>>()
            .BeginScope("{CorrelationId}", correlationId);

        await _next(context);
    }

    private static string ResolveCorrelationId(HttpContext context)
    {
        if (context.Request.Headers.TryGetValue(ApiConstants.CorrelationIdHeaderName, out var value) &&
            !string.IsNullOrWhiteSpace(value))
        {
            return value.ToString();
        }

        return Guid.NewGuid().ToString("N");
    }
}
