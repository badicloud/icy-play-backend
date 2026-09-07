using System.Security.Cryptography;
using System.Text;
using FluentAssertions.Execution;
using IcyPlay.Infrastructure.Storage;
using Microsoft.Extensions.Options;

namespace IcyPlay.UnitTests;

public sealed class CloudinaryAssetServiceTests
{
    private const string CloudName = "icyplay-test";
    private const string ApiKey = "123456789012345";
    private const string ApiSecret = "test-api-secret";

    private static readonly DateTimeOffset Now =
        new(2026, 9, 7, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Should_Sign_Sorted_Parameters_With_The_Api_Secret_When_Upload_Is_Requested()
    {
        // Arrange
        var sut = CreateService();
        var timestamp = Now.ToUnixTimeSeconds();
        var expected = Convert.ToHexString(
                SHA1.HashData(
                    Encoding.UTF8.GetBytes($"folder=icyplay/documents&timestamp={timestamp}{ApiSecret}")))
            .ToLowerInvariant();

        // Act
        var signature = sut.CreateUploadSignature("icyplay/documents");

        // Assert
        using (new AssertionScope())
        {
            signature.Signature.Should().Be(expected);
            signature.Timestamp.Should().Be(timestamp);
            signature.CloudName.Should().Be(CloudName);
            signature.ApiKey.Should().Be(ApiKey);
            // The secret signs the request but must never travel to the browser.
            signature.Should().NotBeEquivalentTo(new { ApiSecret });
        }
    }

    [Fact]
    public void Should_Build_An_Https_Url_When_A_Public_Id_Is_Given()
    {
        // Arrange
        var sut = CreateService();

        // Act
        var url = sut.BuildSecureUrl("icyplay/facilities/court-1");

        // Assert
        url.Should().Be($"https://res.cloudinary.com/{CloudName}/image/upload/icyplay/facilities/court-1");
    }

    [Fact]
    public void Should_Apply_The_Transformation_When_One_Is_Given()
    {
        // Arrange
        var sut = CreateService();

        // Act
        var url = sut.BuildSecureUrl("icyplay/facilities/court-1", "w_400,h_300,c_fill");

        // Assert
        url.Should().Be(
            $"https://res.cloudinary.com/{CloudName}/image/upload/w_400,h_300,c_fill/icyplay/facilities/court-1");
    }

    [Theory]
    // Cloudinary's own "url" field, which would be mixed content on an https page.
    [InlineData("http://res.cloudinary.com/icyplay-test/image/upload/a.png")]
    // Another Cloudinary account.
    [InlineData("https://res.cloudinary.com/someone-else/image/upload/a.png")]
    // A host that only looks like Cloudinary.
    [InlineData("https://res.cloudinary.com.attacker.example/icyplay-test/image/upload/a.png")]
    [InlineData("https://attacker.example/icyplay-test/image/upload/a.png")]
    [InlineData("not-a-url")]
    [InlineData("")]
    [InlineData(null)]
    public void Should_Reject_The_Url_When_It_Is_Not_Https_On_Our_Own_Cloud(string? url)
    {
        // Arrange
        var sut = CreateService();

        // Act
        var trusted = sut.IsTrustedSecureUrl(url);

        // Assert
        trusted.Should().BeFalse();
    }

    [Fact]
    public void Should_Accept_The_Url_When_It_Is_Https_On_Our_Own_Cloud()
    {
        // Arrange
        var sut = CreateService();

        // Act
        var trusted = sut.IsTrustedSecureUrl(
            $"https://res.cloudinary.com/{CloudName}/image/upload/v1/icyplay/permit.pdf");

        // Assert
        trusted.Should().BeTrue();
    }

    [Fact]
    public void Should_Refuse_To_Sign_When_Cloudinary_Is_Not_Configured()
    {
        // Arrange
        var sut = new CloudinaryAssetService(
            Options.Create(new CloudinaryOptions()),
            new FixedTimeProvider(Now));

        // Act
        var act = () => sut.CreateUploadSignature("icyplay/documents");

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    private static CloudinaryAssetService CreateService() => new(
        Options.Create(new CloudinaryOptions
        {
            CloudName = CloudName,
            ApiKey = ApiKey,
            ApiSecret = ApiSecret
        }),
        new FixedTimeProvider(Now));

    private sealed class FixedTimeProvider(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }
}
