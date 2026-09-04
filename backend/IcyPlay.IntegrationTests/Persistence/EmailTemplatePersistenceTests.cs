using FluentAssertions.Execution;
using IcyPlay.Domain.Email;
using IcyPlay.Infrastructure.Email;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class EmailTemplatePersistenceTests(SqlServerDatabaseFixture database)
{
    [Fact]
    public async Task Migration_WhenApplied_ShouldCreateAndSeedAccountVerificationTemplate()
    {
        // Arrange
        await using var context = database.CreateContext();

        // Act
        var appliedMigrations = await context.Database.GetAppliedMigrationsAsync();
        var template = await context.EmailTemplates
            .AsNoTracking()
            .SingleAsync(item =>
                item.Provider == EmailProviderName.Mailjet &&
                item.Key == EmailTemplateKey.AccountVerification);

        // Assert
        using (new AssertionScope())
        {
            appliedMigrations.Should().Contain(migration =>
                migration.EndsWith("_AddEmailTemplates", StringComparison.Ordinal));
            template.ExternalTemplateId.Should().Be(8278054);
            template.Subject.Should().Be("Verify your IcyPlay email address");
            template.IsActive.Should().BeTrue();
        }
    }

    [Fact]
    public async Task EmailTemplateStore_WhenSeededTemplateIsActive_ShouldReturnMailjetDescriptor()
    {
        // Arrange
        await using var context = database.CreateContext();
        var store = new EmailTemplateStore(context);

        // Act
        var result = await store.GetActiveAsync(
            EmailTemplateKey.AccountVerification,
            EmailProviderName.Mailjet,
            CancellationToken.None);

        // Assert
        result.Should().BeEquivalentTo(new
        {
            ExternalTemplateId = 8278054L,
            Subject = "Verify your IcyPlay email address"
        });
    }

    [Fact]
    public async Task SaveChangesAsync_WhenProviderAndKeyAreDuplicated_ShouldEnforceUniqueConstraint()
    {
        // Arrange
        await using var context = database.CreateContext();
        context.EmailTemplates.Add(new EmailTemplate(
            EmailTemplateKey.AccountVerification,
            EmailProviderName.Mailjet,
            9999999,
            "Duplicate template"));

        // Act
        var act = () => context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }
}
