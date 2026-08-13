using System.Text.Json;
using System.Runtime.ExceptionServices;
using IcyPlay.Api.Common;

namespace IcyPlay.Api.Middleware;

public sealed class GlobalExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionMiddleware> _logger;

    public GlobalExceptionMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.Items.TryGetValue(ApiConstants.CorrelationIdHeaderName, out var value)
            ? value?.ToString()
            : null;

        _logger.LogError(
            exception,
            "Unhandled API exception. CorrelationId: {CorrelationId}",
            correlationId);

        if (context.Response.HasStarted)
        {
            ExceptionDispatchInfo.Capture(exception).Throw();
        }

        context.Response.Clear();
        context.Response.StatusCode = StatusCodes.Status500InternalServerError;
        context.Response.ContentType = "application/json";

        var response = new ApiErrorEnvelope(
            new ApiError(
                ErrorCodes.UnexpectedError,
                "An unexpected error occurred."),
            new
            {
                correlationId
            });

        await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonSerializerOptions.Web));
    }
}
