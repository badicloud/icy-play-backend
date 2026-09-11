using IcyPlay.Application.Facilities;

namespace IcyPlay.UnitTests;

public sealed class OwnerAccountInputValidatorTests
{
    private readonly OwnerAccountInputValidator sut = new();

    [Theory]
    [InlineData("09953979930")]
    [InlineData("0995 3979930")]
    [InlineData("0995-397-9930")]
    [InlineData("+639953979930")]
    [InlineData("+63 995 3979930")]
    public void Should_Accept_A_Philippine_Mobile_However_It_Is_Punctuated(string phoneNumber)
    {
        // Act
        var result = sut.Validate(CreateInput(phoneNumber));

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    // Empty: the owner is reached on this number, so it is not optional.
    [InlineData("")]
    [InlineData("   ")]
    // A landline, which is not a mobile.
    [InlineData("082 234 5678")]
    // Right prefix, wrong length.
    [InlineData("0995397993")]
    [InlineData("099539799301")]
    // A foreign number.
    [InlineData("+14155552671")]
    // The national prefix without the mobile 9 after the country code.
    [InlineData("+638953979930")]
    [InlineData("not a number")]
    public void Should_Reject_Anything_That_Is_Not_A_Philippine_Mobile(string phoneNumber)
    {
        // Act
        var result = sut.Validate(CreateInput(phoneNumber));

        // Assert
        result.Errors.Should().Contain(error => error.PropertyName == "PhoneNumber");
    }

    private static OwnerAccountInput CreateInput(string phoneNumber) =>
        new("Juan Dela Cruz", "juan@example.com", phoneNumber);
}
