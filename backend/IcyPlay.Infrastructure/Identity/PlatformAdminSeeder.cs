using IcyPlay.Application.Identity;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IcyPlay.Infrastructure.Identity;

/// <summary>
/// Promotes configured accounts to Platform Administrator.
///
/// Someone has to hold the role before anyone can be granted it through the
/// application, so the first administrators come from configuration. It only
/// ever promotes an account that already exists — it will not create one, so a
/// mistyped address cannot become a live administrator nobody meant to make.
/// </summary>
public sealed class PlatformAdminSeeder(
    AppDbContext db,
    IOptions<PlatformAdminSeedOptions> options,
    TimeProvider timeProvider,
    ILogger<PlatformAdminSeeder> logger) : IPlatformAdminSeeder
{
    private readonly PlatformAdminSeedOptions seedOptions = options.Value;

    public async Task SeedAsync(CancellationToken ct)
    {
        var emails = seedOptions.SeedEmails
            .Where(email => !string.IsNullOrWhiteSpace(email))
            .Select(email => email.Trim().ToLowerInvariant())
            .Distinct()
            .ToArray();

        if (emails.Length == 0)
        {
            return;
        }

        var users = await db.Users
            .Include(user => user.Roles)
            .Where(user => emails.Contains(user.Email))
            .ToListAsync(ct);

        foreach (var missing in emails.Except(users.Select(user => user.Email)))
        {
            logger.LogWarning(
                "A Platform Administrator was configured for an address that has not registered yet, so it was skipped. Email: {Email}",
                missing);
        }

        var granted = 0;
        var now = timeProvider.GetUtcNow();

        foreach (var user in users)
        {
            if (user.Roles.Any(role => role.Role == UserRoleName.PlatformAdmin))
            {
                continue;
            }

            db.UserRoles.Add(new UserRole(user.Id, UserRoleName.PlatformAdmin));

            // Every role carries a profile row, and the rest of the system joins
            // through it, so the role alone would be a half-made administrator.
            var hasProfile = await db.PlatformAdmins.AnyAsync(admin => admin.UserId == user.Id, ct);
            if (!hasProfile)
            {
                db.PlatformAdmins.Add(new PlatformAdmin(user.Id));
            }

            granted++;
            logger.LogWarning(
                "The Platform Administrator role was granted from configuration. UserId: {UserId}, At: {GrantedAt}",
                user.Id,
                now);
        }

        if (granted > 0)
        {
            await db.SaveChangesAsync(ct);
        }
    }
}
