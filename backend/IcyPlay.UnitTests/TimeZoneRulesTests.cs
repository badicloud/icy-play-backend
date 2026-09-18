using IcyPlay.Application.Facilities;

namespace IcyPlay.UnitTests;

/// <summary>
/// The venue's clock is what every hour on this platform is sold against, and
/// an unreadable zone does not announce itself — it answers in UTC, which in
/// Manila is eight hours out. These say the zone cannot get that far.
/// </summary>
public sealed class TimeZoneRulesTests
{
    [Theory]
    // What the platform stores by default, and what the rest of the world uses.
    [InlineData("Asia/Manila")]
    [InlineData("Asia/Singapore")]
    // A Windows host carries these as well, and reads them perfectly well.
    [InlineData("Singapore Standard Time")]
    [InlineData("UTC")]
    public void Should_Accept_A_Zone_The_Platform_Can_Read(string timeZone)
    {
        // Assert
        TimeZoneRules.IsReal(timeZone).Should().BeTrue();
    }

    [Theory]
    // The city without the region: the mistake somebody makes by hand.
    [InlineData("Manila")]
    [InlineData("Philippines")]
    // A typo in the right shape, which is the one that survives a glance.
    [InlineData("Asia/Maniia")]
    [InlineData("Asia/Manilla")]
    // An offset is not a zone: it says nothing about when the clocks change.
    [InlineData("+08:00")]
    [InlineData("GMT+8")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void Should_Refuse_Anything_The_Platform_Cannot_Turn_Into_An_Offset(string? timeZone)
    {
        // Assert
        TimeZoneRules.IsReal(timeZone).Should().BeFalse();
    }

    [Fact]
    public void Should_Refuse_A_Bad_Zone_When_A_Facility_Is_First_Registered()
    {
        // Arrange
        var sut = new FacilityInputValidator();

        // Act
        var result = sut.Validate(Facility("Manila"));

        // Assert: caught while the owner is still on the form, rather than
        // eight hours later on somebody's booking.
        result.Errors.Should().Contain(error => error.PropertyName == "TimeZone");
    }

    [Fact]
    public void Should_Refuse_A_Bad_Zone_When_A_Facility_Is_Edited()
    {
        // Arrange: the second way in. A rule written once and applied once is
        // a rule that is only half true.
        var sut = new UpdateFacilityRequestValidator();

        // Act
        var result = sut.Validate(Update("Asia/Maniia"));

        // Assert
        result.Errors.Should().Contain(error => error.PropertyName == "TimeZone");
    }

    [Fact]
    public void Should_Accept_The_Zone_The_Platform_Defaults_To()
    {
        // Arrange
        var sut = new FacilityInputValidator();

        // Act
        var result = sut.Validate(Facility("Asia/Manila"));

        // Assert: the new rule must not refuse what every venue already has.
        result.Errors.Should().NotContain(error => error.PropertyName == "TimeZone");
    }

    private static FacilityInput Facility(string timeZone) => new(
        "Court Ventures",
        null,
        "123 Demo Street",
        null,
        "Cebu City",
        "Cebu",
        "6000",
        "Philippines",
        null,
        null,
        timeZone,
        null,
        null,
        null,
        null,
        [],
        []);

    private static UpdateFacilityRequest Update(string timeZone) => new(
        "Court Ventures",
        null,
        "123 Demo Street",
        null,
        "Cebu City",
        "Cebu",
        "6000",
        "Philippines",
        null,
        null,
        timeZone,
        null,
        null,
        null,
        null,
        [],
        [],
        null);
}
