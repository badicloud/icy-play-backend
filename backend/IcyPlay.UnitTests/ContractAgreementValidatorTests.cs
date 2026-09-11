using IcyPlay.Application.Facilities;

namespace IcyPlay.UnitTests;

/// <summary>
/// A term without the signed agreement is a claim, not a record, so both the
/// path that commences one and the path that renews one insist on it.
/// </summary>
public sealed class ContractAgreementValidatorTests
{
    private static readonly DateOnly Start = new(2026, 9, 11);

    private static readonly UploadedFileInput Agreement = new(
        "icyplay/facility-owners/contracts/agreement",
        "https://res.cloudinary.com/icyplay-test/image/upload/v1/agreement.pdf",
        "agreement.pdf",
        "application/pdf",
        4096);

    [Fact]
    public void Should_Reject_A_Commencement_With_No_Agreement_Attached()
    {
        // Arrange
        var sut = new ContractInputValidator();

        // Act
        var result = sut.Validate(new ContractInput(Start, Start.AddYears(1), null, null!));

        // Assert
        result.Errors.Should().Contain(error => error.PropertyName == "Document");
    }

    [Fact]
    public void Should_Reject_A_Renewal_With_No_Agreement_Attached()
    {
        // Arrange
        var sut = new RenewContractRequestValidator();

        // Act
        var result = sut.Validate(
            new RenewContractRequest(Start, Start.AddYears(1), null, null!, null));

        // Assert
        result.Errors.Should().Contain(error => error.PropertyName == "Document");
    }

    [Fact]
    public void Should_Accept_A_Commencement_That_Carries_One()
    {
        // Arrange
        var sut = new ContractInputValidator();

        // Act
        var result = sut.Validate(new ContractInput(Start, Start.AddYears(1), "Signed", Agreement));

        // Assert
        result.IsValid.Should().BeTrue();
    }

    [Theory]
    // No file name, so nothing to show the reader.
    [InlineData("", "application/pdf", 4096)]
    // Zero bytes is an upload that did not happen.
    [InlineData("agreement.pdf", "application/pdf", 0)]
    // Past the fifty megabyte ceiling the uploader enforces.
    [InlineData("agreement.pdf", "application/pdf", 51L * 1024 * 1024)]
    // A photo of a contract is not a record of one.
    [InlineData("agreement.jpg", "image/jpeg", 4096)]
    public void Should_Reject_An_Agreement_That_Is_Not_A_Usable_File(
        string fileName,
        string contentType,
        long sizeInBytes)
    {
        // Arrange
        var sut = new UploadedFileInputValidator();

        // Act
        var result = sut.Validate(Agreement with
        {
            FileName = fileName,
            ContentType = contentType,
            SizeInBytes = sizeInBytes
        });

        // Assert
        result.IsValid.Should().BeFalse();
    }
}
