namespace IcyPlay.Infrastructure.Storage;

public sealed class CloudinaryOptions
{
    public const string SectionName = "Cloudinary";

    public string CloudName { get; init; } = string.Empty;
    public string ApiKey { get; init; } = string.Empty;
    public string ApiSecret { get; init; } = string.Empty;
    /// <summary>Seconds a signed upload stays valid.</summary>
    public int SignatureLifetimeSeconds { get; init; } = 600;
}
