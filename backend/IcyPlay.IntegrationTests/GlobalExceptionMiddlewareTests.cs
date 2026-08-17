using System.Text.Json;
using FluentAssertions.Execution;
using IcyPlay.Api.Common;
using IcyPlay.Api.Middleware;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;

namespace IcyPlay.IntegrationTests;

public sealed class GlobalExceptionMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_WhenUnexpectedExceptionIsThrown_ShouldReturnSafeCorrelatedError()
    {
        var context = CreateContext();
        context.Items[ApiConstants.CorrelationIdHeaderName] = "test-correlation";
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new InvalidOperationException("Sensitive internal detail"),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        var response = await ReadResponseAsync(context);
        using (new AssertionScope())
        {
            context.Response.StatusCode.Should().Be(StatusCodes.Status500InternalServerError);
            response.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(ErrorCodes.UnexpectedError);
            response.RootElement.GetProperty("meta").GetProperty("correlationId").GetString().Should().Be("test-correlation");
            response.RootElement.GetRawText().Should().NotContain("Sensitive internal detail");
        }
    }

    [Fact]
    public async Task InvokeAsync_WhenBadHttpRequestExceptionIsThrown_ShouldReturnBadRequestError()
    {
        var context = CreateContext();
        var middleware = new GlobalExceptionMiddleware(
            _ => throw new BadHttpRequestException("Malformed payload"),
            NullLogger<GlobalExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        var response = await ReadResponseAsync(context);
        using (new AssertionScope())
        {
            context.Response.StatusCode.Should().Be(StatusCodes.Status400BadRequest);
            response.RootElement.GetProperty("error").GetProperty("code").GetString().Should().Be(ErrorCodes.BadRequest);
            response.RootElement.GetRawText().Should().NotContain("Malformed payload");
        }
    }

    private static DefaultHttpContext CreateContext()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        return context;
    }

    private static async Task<JsonDocument> ReadResponseAsync(HttpContext context)
    {
        context.Response.Body.Position = 0;
        return await JsonDocument.ParseAsync(context.Response.Body);
    }
}
