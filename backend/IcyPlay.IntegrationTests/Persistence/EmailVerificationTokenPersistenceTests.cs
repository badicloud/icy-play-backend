using FluentAssertions.Execution;
using IcyPlay.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class EmailVerificationTokenPersistenceTests(SqlServerDatabaseFixture database)
{
    [Fact]
    public async Task Migration_WhenApplied_ShouldCreateEmailVerificationTokensTable()
    {
        // Arrange
        await using var context = database.CreateContext();

        // Act
        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
        var tokenCount = await context.EmailVerificationTokens.CountAsync();

        // Assert
        using (new AssertionScope())
        {
            appliedMigrations.Should().Contain(migration =>
                migration.EndsWith("_AddEmailVerificationTokens", StringComparison.Ordinal));
            tokenCount.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_WhenVerificationTokenIsValid_ShouldPersistTokenState()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = CreateUser("token-persistence@example.com");
        var expiresAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var token = new EmailVerificationToken(
            user.Id,
            "email-verification-persistence-hash",
            expiresAt);
        context.Users.Add(user);
        context.EmailVerificationTokens.Add(token);

        // Act
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var persistedToken = await context.EmailVerificationTokens
            .AsNoTracking()
            .SingleAsync(item => item.Id == token.Id);

        // Assert
        using (new AssertionScope())
        {
            persistedToken.UserId.Should().Be(user.Id);
            persistedToken.TokenHash.Should().Be("email-verification-persistence-hash");
            persistedToken.ExpiresAt.Should().Be(expiresAt);
            persistedToken.UsedAt.Should().BeNull();
        }
    }

    [Fact]
    public async Task SaveChangesAsync_WhenTokenHashIsDuplicated_ShouldEnforceUniqueConstraint()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = CreateUser("token-duplicate@example.com");
        var expiresAt = new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);
        context.Users.Add(user);
        context.EmailVerificationTokens.AddRange(
            new EmailVerificationToken(
                user.Id,
                "duplicate-email-verification-hash",
                expiresAt),
            new EmailVerificationToken(
                user.Id,
                "duplicate-email-verification-hash",
                expiresAt.AddHours(1)));

        // Act
        var act = () => context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    private static User CreateUser(string email)
    {
        var user = new User(email, "Verification Token User", "+639171234567");
        user.SetPasswordHash("integration-test-password-hash");
        return user;
    }
}
