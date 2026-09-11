using System.Data.Common;
using FluentAssertions.Execution;
using IcyPlay.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class IdentityPersistenceTests(SqlServerDatabaseFixture database)
{
    [Fact]
    public async Task Schema_WhenMigrationsAreApplied_ShouldContainAllMappedTables()
    {
        // Arrange
        await using var context = database.CreateContext();

        // Act
        var tableNames = await ReadTableNamesAsync(context.Database.GetDbConnection());

        // Assert
        tableNames.Should().BeEquivalentTo(
            "Users",
            "UserRoles",
            "Customers",
            "FacilityOwners",
            "PlatformAdmins",
            "RefreshTokens",
            "EmailVerificationTokens",
            "PasswordResetTokens",
            "AccountInvitationTokens",
            "AuditLogs",
            "FacilityOwnerDocuments",
            "FacilityOwnerContracts",
            "Facilities",
            "FacilityOperatingHours",
            "FacilityAmenities",
            "Amenities",
            "EmailTemplates",
            "__EFMigrationsHistory");
    }

    [Fact]
    public async Task SaveChangesAsync_WhenIdentityGraphIsValid_ShouldPersistAllIdentityEntities()
    {
        // Arrange
        await using var context = database.CreateContext();
        var customerUser = CreateUser("persistence-customer@example.com", "Persistence Customer");
        var facilityOwnerUser = CreateUser("persistence-owner@example.com", "Persistence Owner");
        var platformAdminUser = CreateUser("persistence-admin@example.com", "Persistence Admin");
        context.Users.AddRange(customerUser, facilityOwnerUser, platformAdminUser);
        context.UserRoles.AddRange(
            new UserRole(customerUser.Id, UserRoleName.Customer),
            new UserRole(facilityOwnerUser.Id, UserRoleName.FacilityOwner),
            new UserRole(platformAdminUser.Id, UserRoleName.PlatformAdmin));
        context.Customers.Add(new Customer(customerUser.Id));
        context.FacilityOwners.Add(new FacilityOwner(
            facilityOwnerUser.Id,
            "IcyPlay Sports Center",
            "billing@example.com",
            "+639171234567"));
        context.PlatformAdmins.Add(new PlatformAdmin(platformAdminUser.Id));
        context.RefreshTokens.Add(new RefreshToken(
            customerUser.Id,
            "persistence-refresh-token-hash",
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        context.EmailVerificationTokens.Add(new EmailVerificationToken(
            customerUser.Id,
            "persistence-email-verification-token-hash",
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));

        // Act
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
        var persistedCustomerUser = await context.Users
            .AsNoTracking()
            .Include(user => user.Roles)
            .Include(user => user.RefreshTokens)
            .SingleAsync(user => user.Id == customerUser.Id);
        var persistedCustomer = await context.Customers
            .AsNoTracking()
            .SingleAsync(customer => customer.UserId == customerUser.Id);
        var persistedEmailVerificationToken = await context.EmailVerificationTokens
            .AsNoTracking()
            .SingleAsync(token => token.UserId == customerUser.Id);
        var persistedFacilityOwner = await context.FacilityOwners
            .AsNoTracking()
            .SingleAsync(owner => owner.UserId == facilityOwnerUser.Id);
        var persistedPlatformAdmin = await context.PlatformAdmins
            .AsNoTracking()
            .SingleAsync(admin => admin.UserId == platformAdminUser.Id);

        // Assert
        using (new AssertionScope())
        {
            persistedCustomerUser.Roles.Should().ContainSingle(role =>
                role.Role == UserRoleName.Customer);
            persistedCustomerUser.RefreshTokens.Should().ContainSingle(token =>
                token.TokenHash == "persistence-refresh-token-hash");
            persistedCustomer.UserId.Should().Be(customerUser.Id);
            persistedEmailVerificationToken.TokenHash.Should()
                .Be("persistence-email-verification-token-hash");
            persistedFacilityOwner.BusinessName.Should().Be("IcyPlay Sports Center");
            persistedFacilityOwner.BillingEmail.Should().Be("billing@example.com");
            persistedPlatformAdmin.UserId.Should().Be(platformAdminUser.Id);
        }
    }

    [Fact]
    public async Task SaveChangesAsync_WhenUserEmailIsDuplicated_ShouldEnforceUniqueConstraint()
    {
        // Arrange
        await using var context = database.CreateContext();
        context.Users.AddRange(
            CreateUser("duplicate@example.com", "First User"),
            CreateUser("duplicate@example.com", "Second User"));

        // Act
        var act = () => context.SaveChangesAsync();

        // Assert
        await act.Should().ThrowAsync<DbUpdateException>();
    }

    [Fact]
    public async Task Remove_WhenUserHasIdentityDependents_ShouldCascadeDeleteRelatedEntities()
    {
        // Arrange
        await using var context = database.CreateContext();
        var user = CreateUser("cascade@example.com", "Cascade User");
        context.Users.Add(user);
        context.UserRoles.Add(new UserRole(user.Id, UserRoleName.Customer));
        context.Customers.Add(new Customer(user.Id));
        context.RefreshTokens.Add(new RefreshToken(
            user.Id,
            "cascade-refresh-token-hash",
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        context.EmailVerificationTokens.Add(new EmailVerificationToken(
            user.Id,
            "cascade-email-verification-token-hash",
            new DateTimeOffset(2030, 1, 1, 0, 0, 0, TimeSpan.Zero)));
        await context.SaveChangesAsync();

        // Act
        context.Users.Remove(user);
        await context.SaveChangesAsync();

        // Assert
        using (new AssertionScope())
        {
            (await context.Users.CountAsync(item => item.Id == user.Id)).Should().Be(0);
            (await context.UserRoles.CountAsync(item => item.UserId == user.Id)).Should().Be(0);
            (await context.Customers.CountAsync(item => item.UserId == user.Id)).Should().Be(0);
            (await context.RefreshTokens.CountAsync(item => item.UserId == user.Id)).Should().Be(0);
            (await context.EmailVerificationTokens.CountAsync(item => item.UserId == user.Id)).Should().Be(0);
        }
    }

    private static User CreateUser(string email, string fullName)
    {
        var user = new User(email, fullName, "+639171234567");
        user.SetPasswordHash("integration-test-password-hash");
        return user;
    }

    private static async Task<IReadOnlyCollection<string>> ReadTableNamesAsync(
        DbConnection connection)
    {
        await connection.OpenAsync();

        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT TABLE_NAME
            FROM INFORMATION_SCHEMA.TABLES
            WHERE TABLE_SCHEMA = 'dbo' AND TABLE_TYPE = 'BASE TABLE';
            """;

        var tableNames = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            tableNames.Add(reader.GetString(0));
        }

        return tableNames;
    }
}
