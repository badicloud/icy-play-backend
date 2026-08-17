using IcyPlay.Application.Identity;

namespace IcyPlay.UnitTests.TestData;

public sealed class RegisterCustomerRequestBuilder
{
    private string _fullName = "Juan Dela Cruz";
    private string _email = "customer@example.com";
    private string _password = "StrongPass1!";
    private string? _phoneNumber = "09123456789";
    private bool _acceptedTerms = true;
    private string _captchaToken = "valid-test-captcha-token";

    public RegisterCustomerRequestBuilder WithFullName(string fullName)
    {
        _fullName = fullName;
        return this;
    }

    public RegisterCustomerRequestBuilder WithEmail(string email)
    {
        _email = email;
        return this;
    }

    public RegisterCustomerRequestBuilder WithPassword(string password)
    {
        _password = password;
        return this;
    }

    public RegisterCustomerRequestBuilder WithPhoneNumber(string? phoneNumber)
    {
        _phoneNumber = phoneNumber;
        return this;
    }

    public RegisterCustomerRequestBuilder WithAcceptedTerms(bool acceptedTerms)
    {
        _acceptedTerms = acceptedTerms;
        return this;
    }

    public RegisterCustomerRequestBuilder WithCaptchaToken(string captchaToken)
    {
        _captchaToken = captchaToken;
        return this;
    }

    public RegisterCustomerRequest Build() => new(
        _fullName,
        _email,
        _password,
        _phoneNumber,
        _acceptedTerms,
        _captchaToken);
}
