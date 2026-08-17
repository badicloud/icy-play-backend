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
            if (exception is OperationCanceledException && context.RequestAborted.IsCancellationRequested)
            {
                _logger.LogInformation(
                    "Request was cancelled by the client. Method: {Method}, Path: {Path}",
                    context.Request.Method,
                    context.Request.Path.Value);
                return;
            }

            await HandleExceptionAsync(context, exception);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        var correlationId = context.Items.TryGetValue(ApiConstants.CorrelationIdHeaderName, out var value)
            ? value?.ToString()
            : null;

        if (context.Response.HasStarted)
        {
            _logger.LogError(
                exception,
                "Unhandled exception after the response started. Method: {Method}, Path: {Path}",
                context.Request.Method,
                context.Request.Path.Value);
            ExceptionDispatchInfo.Capture(exception).Throw();
            return;
        }

        var (statusCode, errorCode, message, logLevel) = exception switch
        {
            BadHttpRequestException => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "The request could not be processed.",
                LogLevel.Warning),
            UnauthorizedAccessException => (
                StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden,
                "You are not allowed to perform this action.",
                LogLevel.Warning),
            KeyNotFoundException => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "The requested resource was not found.",
                LogLevel.Warning),
            _ => (
                StatusCodes.Status500InternalServerError,
                ErrorCodes.UnexpectedError,
                "An unexpected error occurred.",
                LogLevel.Error)
        };

        _logger.Log(
            logLevel,
            exception,
            "API request failed with {StatusCode}. Method: {Method}, Path: {Path}, CorrelationId: {CorrelationId}, TraceId: {TraceId}",
            statusCode,
            context.Request.Method,
            context.Request.Path.Value,
            correlationId,
            context.TraceIdentifier);

        context.Response.Clear();
        context.Response.StatusCode = statusCode;

        var response = new ApiErrorEnvelope(
            new ApiError(errorCode, message),
            new
            {
                correlationId,
                traceId = context.TraceIdentifier
            });

        await context.Response.WriteAsJsonAsync(response, context.RequestAborted);
    }
}
