using FluentAssertions.Execution;
using FluentValidation.TestHelper;
using IcyPlay.Application.Identity;
using IcyPlay.UnitTests.TestData;

namespace IcyPlay.UnitTests;

public sealed class RegisterCustomerRequestValidatorTests
{
    private readonly RegisterCustomerRequestValidator _validator = new();

    [Fact]
    public void Should_Return_Password_Error_When_Password_Is_Weak()
    {
        // Arrange
        var request = new RegisterCustomerRequestBuilder()
            .WithPassword("weak")
            .Build();

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldHaveValidationErrorFor(candidate => candidate.Password);
    }

    [Fact]
    public void Should_Return_No_Errors_When_Request_And_Philippine_Phone_Are_Valid()
    {
        // Arrange
        var request = new RegisterCustomerRequestBuilder()
            .WithPhoneNumber("0912 345 6789")
            .Build();

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        result.ShouldNotHaveAnyValidationErrors();
    }

    [Fact]
    public void Should_Return_Agreement_And_Captcha_Errors_When_Both_Are_Missing()
    {
        // Arrange
        var request = new RegisterCustomerRequestBuilder()
            .WithAcceptedTerms(false)
            .WithCaptchaToken(string.Empty)
            .Build();

        // Act
        var result = _validator.TestValidate(request);

        // Assert
        using (new AssertionScope())
        {
            result.ShouldHaveValidationErrorFor(candidate => candidate.AcceptedTerms);
            result.ShouldHaveValidationErrorFor(candidate => candidate.CaptchaToken);
        }
    }
}
