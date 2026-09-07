namespace IcyPlay.Application.Identity;

public interface IPlatformAdminSeeder
{
    /// <summary>
    /// Grants the Platform Administrator role to the configured accounts.
    /// Safe to run on every startup.
    /// </summary>
    Task SeedAsync(CancellationToken cancellationToken);
}
