using FluentAssertions.Execution;
using IcyPlay.Infrastructure.OpenPlays;
using Microsoft.AspNetCore.DataProtection;

namespace IcyPlay.UnitTests;

public sealed class OpenPlayCheckInCodeVaultTests
{
    [Fact]
    public void Should_Give_The_Code_Back_When_It_Was_Kept_By_The_Same_Server()
    {
        // Arrange
        var vault = new OpenPlayCheckInCodeVault(new EphemeralDataProtectionProvider());
        var kept = vault.Protect("482913");

        // Act
        var shown = vault.Reveal(kept);

        // Assert
        using (new AssertionScope())
        {
            kept.Should().NotContain("482913");
            shown.Should().Be("482913");
        }
    }

    [Fact]
    public void Should_Show_Nothing_When_The_Servers_Key_Has_Changed()
    {
        // Arrange
        var kept = new OpenPlayCheckInCodeVault(new EphemeralDataProtectionProvider()).Protect("482913");
        var otherServer = new OpenPlayCheckInCodeVault(new EphemeralDataProtectionProvider());

        // Act
        var shown = otherServer.Reveal(kept);

        // Assert
        shown.Should().BeNull();
    }

    [Fact]
    public void Should_Show_Nothing_When_The_Kept_Copy_Was_Tampered_With()
    {
        // Arrange
        var vault = new OpenPlayCheckInCodeVault(new EphemeralDataProtectionProvider());
        var kept = vault.Protect("482913");

        // Act
        var shown = vault.Reveal(kept[..^4] + "AAAA");

        // Assert
        shown.Should().BeNull();
    }
}
