using FluentValidation;

namespace IcyPlay.Application.Identity;

public sealed class RegisterCustomerRequestValidator : AbstractValidator<RegisterCustomerRequest>
{
    public RegisterCustomerRequestValidator()
    {
        AddCommonRules();
    }

    private void AddCommonRules()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).ApplyPasswordRules();
        RuleFor(x => x.PhoneNumber).NotEmpty().Must(RegistrationValidation.IsValidPhoneNumber)
            .WithMessage("Enter a valid phone number.");
        RuleFor(x => x.AcceptedTerms).Equal(true).WithMessage("You must accept the Terms of Service and Privacy Policy.");
        RuleFor(x => x.CaptchaToken).NotEmpty().WithMessage("Please complete the reCAPTCHA challenge.");
    }
}
public sealed class RegisterFacilityOwnerRequestValidator : AbstractValidator<RegisterFacilityOwnerRequest>
{
    public RegisterFacilityOwnerRequestValidator()
    {
        RuleFor(x => x.FullName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.Password).ApplyPasswordRules();
        RuleFor(x => x.BusinessName).NotEmpty().MaximumLength(200);
        RuleFor(x => x.BillingEmail).NotEmpty().EmailAddress().MaximumLength(256);
        RuleFor(x => x.BillingPhone).NotEmpty().Must(RegistrationValidation.IsValidPhoneNumber)
            .WithMessage("Enter a valid phone number.");
        RuleFor(x => x.AcceptedTerms).Equal(true).WithMessage("You must accept the Terms of Service and Privacy Policy.");
        RuleFor(x => x.CaptchaToken).NotEmpty().WithMessage("Please complete the reCAPTCHA challenge.");
    }
}
public sealed class LoginRequestValidator : AbstractValidator<LoginRequest>
{
    public LoginRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
public sealed class RefreshRequestValidator : AbstractValidator<RefreshRequest>
{
    public RefreshRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}
public sealed class LogoutRequestValidator : AbstractValidator<LogoutRequest>
{
    public LogoutRequestValidator() => RuleFor(x => x.RefreshToken).NotEmpty();
}
public sealed class ResendVerificationEmailRequestValidator : AbstractValidator<ResendVerificationEmailRequest>
{
    public ResendVerificationEmailRequestValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress().MaximumLength(256);
    }
}
public sealed class VerifyEmailRequestValidator : AbstractValidator<VerifyEmailRequest>
{
    public VerifyEmailRequestValidator()
    {
        RuleFor(x => x.Token).NotEmpty().MaximumLength(256);
    }
}

internal static class RegistrationValidation
{
    public static IRuleBuilderOptions<T, string> ApplyPasswordRules<T>(this IRuleBuilder<T, string> rule) => rule
        .NotEmpty().WithMessage("Password is required.")
        .MinimumLength(12).WithMessage("Password must be at least 12 characters.")
        .MaximumLength(128).WithMessage("Password must not exceed 128 characters.")
        .Matches("[A-Z]").WithMessage("Password must contain an uppercase letter.")
        .Matches("[a-z]").WithMessage("Password must contain a lowercase letter.")
        .Matches("[0-9]").WithMessage("Password must contain a number.")
        .Matches("[^A-Za-z0-9]").WithMessage("Password must contain a special character.");

    public static bool IsValidPhoneNumber(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        var normalized = new string(value.Where(character => char.IsDigit(character) || character == '+').ToArray());
        if (normalized.StartsWith("09") && normalized.Length == 11)
        {
            return true;
        }

        if (normalized.StartsWith("+63") && normalized.Length == 13)
        {
            return true;
        }

        return normalized.StartsWith('+')
            ? normalized.Length is >= 9 and <= 16 && normalized.Skip(1).All(char.IsDigit)
            : normalized.Length is >= 8 and <= 15 && normalized.All(char.IsDigit);
    }
}
