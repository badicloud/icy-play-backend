namespace IcyPlay.Infrastructure.Identity;

public sealed class PlatformAdminSeedOptions
{
    public const string SectionName = "PlatformAdmin";

    /// <summary>
    /// Email addresses promoted to Platform Administrator on startup. These must
    /// already have registered: the seeder never creates an account, so a typo
    /// is logged rather than silently producing a phantom administrator.
    /// </summary>
    public string[] SeedEmails { get; init; } = [];
}
