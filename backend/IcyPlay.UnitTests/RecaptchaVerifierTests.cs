using System.Net;
using System.Text;
using FluentAssertions.Execution;
using IcyPlay.Infrastructure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq.Protected;

namespace IcyPlay.UnitTests;

public sealed class RecaptchaVerifierTests
{
    [Fact]
    public async Task Should_Return_True_When_Response_Has_Valid_Score_Action_And_Hostname()
    {
        // Arrange
        var verifier = CreateVerifier(
            score: 0.9,
            action: "register",
            hostname: "localhost");

        // Act
        var result = await verifier.VerifyAsync(
            "valid-token",
            "register",
            "127.0.0.1",
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeTrue();
        }
    }

    [Theory]
    [InlineData(0.4, "register", "localhost")]
    [InlineData(0.9, "login", "localhost")]
    [InlineData(0.9, "register", "unapproved.example.com")]
    public async Task Should_Return_False_When_Score_Action_Or_Hostname_Is_Invalid(
        double score,
        string action,
        string hostname)
    {
        // Arrange
        var verifier = CreateVerifier(score, action, hostname);

        // Act
        var result = await verifier.VerifyAsync(
            "valid-token",
            "register",
            "127.0.0.1",
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFalse();
        }
    }

    [Fact]
    public async Task Should_Return_False_When_Token_Is_Invalid()
    {
        // Arrange
        var verifier = CreateVerifier(
            score: 0,
            action: null,
            hostname: null,
            tokenValid: false);

        // Act
        var result = await verifier.VerifyAsync(
            "invalid-token",
            "register",
            null,
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            result.Should().BeFalse();
        }
    }

    private static RecaptchaVerifier CreateVerifier(
        double score,
        string? action,
        string? hostname,
        bool tokenValid = true)
    {
        var responseJson = $$"""
            {
              "success": {{tokenValid.ToString().ToLowerInvariant()}},
              "score": {{score}},
              "action": {{(action is null ? "null" : $"\"{action}\"")}},
              "hostname": {{(hostname is null ? "null" : $"\"{hostname}\"")}},
              "error-codes": {{(tokenValid ? "[]" : "[\"invalid-input-response\"]")}}
            }
            """;
        var handler = new Mock<HttpMessageHandler>(MockBehavior.Strict);
        handler.Protected()
            .Setup<Task<HttpResponseMessage>>(
                "SendAsync",
                ItExpr.IsAny<HttpRequestMessage>(),
                ItExpr.IsAny<CancellationToken>())
            .ReturnsAsync(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            });

        var settings = new Dictionary<string, string?>
        {
            ["Recaptcha:SecretKey"] = "test-secret",
            ["Recaptcha:MinimumScore"] = "0.5",
            ["Recaptcha:AllowedHostnames:0"] = "localhost"
        };
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();

        return new RecaptchaVerifier(
            new HttpClient(handler.Object),
            configuration,
            NullLogger<RecaptchaVerifier>.Instance);
    }
}
