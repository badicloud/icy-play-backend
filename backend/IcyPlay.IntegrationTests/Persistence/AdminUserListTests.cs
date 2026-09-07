using FluentAssertions.Execution;
using IcyPlay.Application.Identity;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Identity;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.IntegrationTests.Persistence;

[Collection(DatabaseCollection.Name)]
public sealed class AdminUserListTests(SqlServerDatabaseFixture database)
{
    private static readonly DateTimeOffset Now =
        new(2026, 9, 7, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ListAsync_ShouldRejectASortFieldThatIsNotOnTheWhitelist()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = new AdminUserService(context);

        // Act
        var result = await sut.ListAsync(
            new AdminUserQuery(SortBy: "passwordHash"),
            CancellationToken.None);

        // Assert: null tells the API to answer INVALID_SORT_FIELD rather than
        // silently sorting by something the caller never asked for.
        result.Should().BeNull();
    }

    [Fact]
    public async Task ListAsync_ShouldMatchOnEmailAndOnName()
    {
        // Arrange
        await using var context = database.CreateContext();
        var marker = $"list-{Guid.NewGuid():N}";
        await AddUserAsync(context, $"{marker}-search@example.com", "Wilma Searchable");
        var sut = new AdminUserService(context);

        // Act
        var byEmail = await sut.ListAsync(new AdminUserQuery(Search: marker), CancellationToken.None);
        var byName = await sut.ListAsync(
            new AdminUserQuery(Search: "Wilma Searchable"),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            byEmail!.Items.Should().ContainSingle();
            byName!.Items.Should().Contain(user => user.FullName == "Wilma Searchable");
        }
    }

    [Fact]
    public async Task ListAsync_ShouldReturnRolesAndVerificationState()
    {
        // Arrange
        await using var context = database.CreateContext();
        var email = $"list-roles-{Guid.NewGuid():N}@example.com";
        var user = await AddUserAsync(context, email, "Roled Customer");
        context.UserRoles.Add(new UserRole(user.Id, UserRoleName.Customer));
        context.UserRoles.Add(new UserRole(user.Id, UserRoleName.PlatformAdmin));
        var tracked = await context.Users.SingleAsync(candidate => candidate.Id == user.Id);
        tracked.MarkEmailVerified(Now);
        await context.SaveChangesAsync();
        var sut = new AdminUserService(context);

        // Act
        var result = await sut.ListAsync(new AdminUserQuery(Search: email), CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            var listed = result!.Items.Single();
            listed.Roles.Should().BeEquivalentTo([UserRoleName.Customer, UserRoleName.PlatformAdmin]);
            listed.IsEmailVerified.Should().BeTrue();
            listed.EmailVerifiedAt.Should().Be(Now);
            listed.IsActive.Should().BeTrue();
        }
    }

    [Fact]
    public async Task ListAsync_ShouldNarrowToOneRoleWhenAskedTo()
    {
        // Arrange
        await using var context = database.CreateContext();
        var marker = $"list-role-filter-{Guid.NewGuid():N}";
        var owner = await AddUserAsync(context, $"{marker}-owner@example.com", "Filtered Owner");
        await AddUserAsync(context, $"{marker}-plain@example.com", "Filtered Plain");
        context.UserRoles.Add(new UserRole(owner.Id, UserRoleName.FacilityOwner));
        await context.SaveChangesAsync();
        var sut = new AdminUserService(context);

        // Act
        var result = await sut.ListAsync(
            new AdminUserQuery(Search: marker, Role: UserRoleName.FacilityOwner),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            result!.Items.Should().ContainSingle();
            result.Items.Single().Email.Should().Be($"{marker}-owner@example.com");
        }
    }

    [Fact]
    public async Task ListAsync_ShouldPageAndReportTheTotal()
    {
        // Arrange
        await using var context = database.CreateContext();
        var marker = $"list-page-{Guid.NewGuid():N}";
        for (var index = 0; index < 5; index++)
        {
            await AddUserAsync(context, $"{marker}-{index}@example.com", $"Paged {index}");
        }

        var sut = new AdminUserService(context);

        // Act
        var firstPage = await sut.ListAsync(
            new AdminUserQuery(Search: marker, Page: 1, PageSize: 2, SortBy: "email", SortDirection: "asc"),
            CancellationToken.None);
        var lastPage = await sut.ListAsync(
            new AdminUserQuery(Search: marker, Page: 3, PageSize: 2, SortBy: "email", SortDirection: "asc"),
            CancellationToken.None);

        // Assert
        using (new AssertionScope())
        {
            firstPage!.Items.Should().HaveCount(2);
            firstPage.TotalItems.Should().Be(5);
            firstPage.TotalPages.Should().Be(3);
            firstPage.Items.First().Email.Should().Be($"{marker}-0@example.com");
            lastPage!.Items.Should().ContainSingle();
        }
    }

    [Fact]
    public async Task ListAsync_ShouldCapAnOversizedPageSize()
    {
        // Arrange
        await using var context = database.CreateContext();
        var sut = new AdminUserService(context);

        // Act: a caller asking for everything at once gets the documented ceiling.
        var result = await sut.ListAsync(
            new AdminUserQuery(PageSize: 5000),
            CancellationToken.None);

        // Assert
        result!.PageSize.Should().Be(100);
    }

    private static async Task<User> AddUserAsync(AppDbContext context, string email, string fullName)
    {
        var user = new User(email, fullName, "+639171234567");
        user.SetPasswordHash("integration-test-password-hash");
        context.Users.Add(user);
        await context.SaveChangesAsync();
        return user;
    }
}
