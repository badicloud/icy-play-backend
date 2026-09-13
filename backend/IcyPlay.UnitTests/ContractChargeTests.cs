using FluentAssertions.Execution;
using IcyPlay.Domain.Facilities;

namespace IcyPlay.UnitTests;

public sealed class ContractChargeTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Should_Bill_The_Owner_By_The_Hour_And_Take_Commission_Out_Of_That_Bill()
    {
        // Arrange: fifty hours booked across the owner's courts in a period.
        var sut = CreateContract();

        // Act
        var charges = sut.ChargesFor(bookedHours: 50m);

        // Assert
        using (new AssertionScope())
        {
            charges.PlatformBill.Should().Be(750m);
            // Of the bill, not on top of it.
            charges.Commission.Should().Be(22.50m);
            charges.NetAfterCommission.Should().Be(727.50m);
        }
    }

    [Fact]
    public void Should_Start_On_The_Platform_Standard_When_A_Term_Says_Nothing()
    {
        // Act
        var sut = CreateContract();

        // Assert
        using (new AssertionScope())
        {
            sut.PlatformHourlyRate.Should().Be(15.00m);
            sut.CommissionPercentage.Should().Be(3.00m);
        }
    }

    [Fact]
    public void Should_Bill_Nothing_When_A_Term_Is_Negotiated_To_Zero()
    {
        // Arrange: a venue onboarded as a favour. Refusing to record that would
        // only push it into a side agreement nobody can see.
        var sut = CreateContract();
        sut.SetRates(0m, 0m, Now);

        // Act
        var charges = sut.ChargesFor(bookedHours: 50m);

        // Assert
        using (new AssertionScope())
        {
            charges.PlatformBill.Should().Be(0m);
            charges.Commission.Should().Be(0m);
        }
    }

    [Fact]
    public void Should_Round_A_Commission_To_Centavos()
    {
        // Arrange
        var sut = CreateContract();
        sut.SetRates(15m, 3.33m, Now);

        // Act: 3.33 per cent of 761.25 is 25.349 and a bit.
        var charges = sut.ChargesFor(bookedHours: 50.75m);

        // Assert: a figure carried to more than two places is one no bank
        // statement can agree with.
        using (new AssertionScope())
        {
            charges.PlatformBill.Should().Be(761.25m);
            charges.Commission.Should().Be(25.35m);
            charges.NetAfterCommission.Should().Be(735.90m);
        }
    }

    [Fact]
    public void Should_Bill_A_Negotiated_Rate_Rather_Than_The_Standard()
    {
        // Arrange
        var sut = CreateContract();
        sut.SetRates(25m, 5m, Now);

        // Act
        var charges = sut.ChargesFor(bookedHours: 50m);

        // Assert
        using (new AssertionScope())
        {
            charges.PlatformBill.Should().Be(1250m);
            charges.Commission.Should().Be(62.50m);
        }
    }

    private static FacilityOwnerContract CreateContract() => new(
        Guid.NewGuid(),
        new DateOnly(2026, 1, 1),
        new DateOnly(2026, 12, 31),
        Guid.NewGuid(),
        null,
        Now);
}
