using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Identity;

namespace IcyPlay.UnitTests;

/// <summary>
/// Status is derived from contract dates rather than stored, so these dates are
/// the whole specification of who is bookable.
/// </summary>
public sealed class FacilityOwnerStatusTests
{
    private static readonly DateOnly Today = new(2026, 9, 8);

    [Fact]
    public void Should_Be_Pending_When_No_Contract_Has_Been_Signed()
    {
        // Act
        var status = FacilityOwner.DeriveStatus(isActive: true, [], Today);

        // Assert
        status.Should().Be(FacilityOwnerStatus.Pending);
    }

    [Fact]
    public void Should_Be_Pending_When_The_Only_Contract_Has_Not_Started_Yet()
    {
        // Arrange: encoded today against next month's term.
        ContractTerm[] terms = [new(Today.AddMonths(1), Today.AddMonths(13))];

        // Act
        var status = FacilityOwner.DeriveStatus(isActive: true, terms, Today);

        // Assert: not started is not the same as run out.
        status.Should().Be(FacilityOwnerStatus.Pending);
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(-1, 1)]
    [InlineData(-365, 0)]
    public void Should_Be_Commenced_When_A_Term_Covers_Today(int startOffset, int endOffset)
    {
        // Arrange
        ContractTerm[] terms = [new(Today.AddDays(startOffset), Today.AddDays(endOffset))];

        // Act
        var status = FacilityOwner.DeriveStatus(isActive: true, terms, Today);

        // Assert: both boundaries are inclusive.
        status.Should().Be(FacilityOwnerStatus.Commenced);
    }

    [Fact]
    public void Should_Be_Expired_When_Every_Term_Has_Run_Out()
    {
        // Arrange
        ContractTerm[] terms = [new(Today.AddYears(-2), Today.AddDays(-1))];

        // Act
        var status = FacilityOwner.DeriveStatus(isActive: true, terms, Today);

        // Assert
        status.Should().Be(FacilityOwnerStatus.Expired);
    }

    [Fact]
    public void Should_Be_Commenced_When_A_Lapsed_Term_Sits_Beside_A_Renewed_One()
    {
        // Arrange: last year's term and this year's coexist, which is the reason
        // contracts are their own table.
        ContractTerm[] terms =
        [
            new(Today.AddYears(-2), Today.AddYears(-1)),
            new(Today.AddDays(-30), Today.AddMonths(11))
        ];

        // Act
        var status = FacilityOwner.DeriveStatus(isActive: true, terms, Today);

        // Assert
        status.Should().Be(FacilityOwnerStatus.Commenced);
    }

    [Fact]
    public void Should_Be_Suspended_Whatever_The_Contracts_Say()
    {
        // Arrange
        ContractTerm[] terms = [new(Today.AddDays(-1), Today.AddDays(1))];

        // Act
        var status = FacilityOwner.DeriveStatus(isActive: false, terms, Today);

        // Assert: the suspend switch overrides a live term.
        status.Should().Be(FacilityOwnerStatus.Suspended);
    }
}
