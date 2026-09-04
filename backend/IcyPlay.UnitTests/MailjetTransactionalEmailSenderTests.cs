using System.Net;
using System.Net.Http.Json;
using FluentAssertions.Execution;
using IcyPlay.Application.Email;
using IcyPlay.Domain.Email;
using IcyPlay.Infrastructure.Email;
using IcyPlay.UnitTests.TestData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace IcyPlay.UnitTests;

public sealed class MailjetTransactionalEmailSenderTests
{
    [Fact]
    public async Task Should_Send_Mailjet_Template_Request_When_Message_Is_Valid()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var templateStore = new Mock<IEmailTemplateStore>(MockBehavior.Strict);
        templateStore
            .Setup(store => store.GetActiveAsync(
                EmailTemplateKey.AccountVerification,
                EmailProviderName.Mailjet,
                cancellationToken))
            .ReturnsAsync(new EmailTemplateDescriptor(
                8278054,
                "Verify your IcyPlay email address"));
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK);
        var sut = CreateSender(handler, templateStore.Object);
        var message = new TransactionalEmailMessageBuilder().Build();

        // Act
        await sut.SendAsync(message, cancellationToken);

        // Assert
        using (new AssertionScope())
        {
            handler.Method.Should().Be(HttpMethod.Post);
            handler.RequestUri.Should().Be("https://api.mailjet.com/v3.1/send");
            handler.AuthorizationScheme.Should().Be("Basic");
            handler.AuthorizationParameter.Should().NotBeNullOrWhiteSpace();
            handler.RequestBody.Should().Contain("\"TemplateID\":8278054");
            handler.RequestBody.Should().Contain("\"TemplateLanguage\":true");
            handler.RequestBody.Should().Contain("\"recipient_name\":\"IcyPlay Customer\"");
        }

        templateStore.VerifyAll();
    }

    [Fact]
    public async Task Should_Reject_Message_When_Active_Template_Does_Not_Exist()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var templateStore = new Mock<IEmailTemplateStore>(MockBehavior.Strict);
        templateStore
            .Setup(store => store.GetActiveAsync(
                EmailTemplateKey.AccountVerification,
                EmailProviderName.Mailjet,
                cancellationToken))
            .ReturnsAsync((EmailTemplateDescriptor?)null);
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK);
        var sut = CreateSender(handler, templateStore.Object);
        var message = new TransactionalEmailMessageBuilder().Build();

        // Act
        var act = () => sut.SendAsync(message, cancellationToken);

        // Assert
        using (new AssertionScope())
        {
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*account-verification*");
            handler.CallCount.Should().Be(0);
        }

        templateStore.VerifyAll();
    }

    [Fact]
    public async Task Should_Reject_Message_When_Mailjet_Credentials_Are_Missing()
    {
        // Arrange
        var templateStore = new Mock<IEmailTemplateStore>(MockBehavior.Strict);
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK);
        var sut = CreateSender(
            handler,
            templateStore.Object,
            new MailjetOptions
            {
                SenderEmail = "icyplaybooking@example.com",
                SenderName = "IcyPlay"
            });
        var message = new TransactionalEmailMessageBuilder().Build();

        // Act
        var act = () => sut.SendAsync(message, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*credentials*");
            handler.CallCount.Should().Be(0);
        }

        templateStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_Reject_Message_Before_Request_When_Mailjet_Credentials_Are_Placeholders()
    {
        // Arrange
        var templateStore = new Mock<IEmailTemplateStore>(MockBehavior.Strict);
        var handler = new CapturingHttpMessageHandler(HttpStatusCode.OK);
        var sut = CreateSender(
            handler,
            templateStore.Object,
            new MailjetOptions
            {
                ApiKey = "REPLACE_WITH_ROTATED_MAILJET_API_KEY",
                ApiSecret = "REPLACE_WITH_ROTATED_MAILJET_API_SECRET",
                SenderEmail = "icyplaybooking@example.com",
                SenderName = "IcyPlay"
            });
        var message = new TransactionalEmailMessageBuilder().Build();

        // Act
        var act = () => sut.SendAsync(message, CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            await act.Should().ThrowAsync<InvalidOperationException>()
                .WithMessage("*credentials*");
            handler.CallCount.Should().Be(0);
        }

        templateStore.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task Should_Return_Structured_Error_Details_When_Mailjet_Rejects_Request()
    {
        // Arrange
        var cancellationToken = CancellationToken.None;
        var templateStore = new Mock<IEmailTemplateStore>(MockBehavior.Strict);
        templateStore
            .Setup(store => store.GetActiveAsync(
                EmailTemplateKey.AccountVerification,
                EmailProviderName.Mailjet,
                cancellationToken))
            .ReturnsAsync(new EmailTemplateDescriptor(
                8278054,
                "Verify your IcyPlay email address"));
        var handler = new CapturingHttpMessageHandler(
            HttpStatusCode.BadRequest,
            new
            {
                Messages = new[]
                {
                    new
                    {
                        Status = "error",
                        Errors = new[]
                        {
                            new
                            {
                                ErrorIdentifier = "mj-test-id",
                                ErrorCode = "send-0001",
                                StatusCode = 400,
                                ErrorMessage = "Invalid sender"
                            }
                        }
                    }
                }
            });
        var sut = CreateSender(handler, templateStore.Object);
        var message = new TransactionalEmailMessageBuilder().Build();

        // Act
        var act = () => sut.SendAsync(message, cancellationToken);

        // Assert
        await act.Should().ThrowAsync<HttpRequestException>()
            .WithMessage("*mj-test-id*send-0001*Invalid sender*");
        templateStore.VerifyAll();
    }

    private static MailjetTransactionalEmailSender CreateSender(
        HttpMessageHandler handler,
        IEmailTemplateStore templateStore,
        MailjetOptions? options = null)
    {
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.mailjet.com/v3.1/")
        };

        return new MailjetTransactionalEmailSender(
            httpClient,
            templateStore,
            Options.Create(options ?? new MailjetOptions
            {
                ApiKey = "test-api-key",
                ApiSecret = "test-api-secret",
                SenderEmail = "icyplaybooking@example.com",
                SenderName = "IcyPlay"
            }),
            NullLogger<MailjetTransactionalEmailSender>.Instance);
    }

    private sealed class CapturingHttpMessageHandler(
        HttpStatusCode statusCode,
        object? responseBody = null) : HttpMessageHandler
    {
        public int CallCount
        {
            get; private set;
        }
        public HttpMethod? Method
        {
            get; private set;
        }
        public string? RequestUri
        {
            get; private set;
        }
        public string? AuthorizationScheme
        {
            get; private set;
        }
        public string? AuthorizationParameter
        {
            get; private set;
        }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            Method = request.Method;
            RequestUri = request.RequestUri?.ToString();
            AuthorizationScheme = request.Headers.Authorization?.Scheme;
            AuthorizationParameter = request.Headers.Authorization?.Parameter;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);

            return new HttpResponseMessage(statusCode)
            {
                Content = JsonContent.Create(responseBody ?? new { status = "accepted" })
            };
        }
    }
}
