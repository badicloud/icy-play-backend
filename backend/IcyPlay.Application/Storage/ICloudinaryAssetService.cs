namespace IcyPlay.Application.Storage;

public interface ICloudinaryAssetService
{
    /// <summary>
    /// Signs an upload for one folder. Files go from the browser to Cloudinary
    /// directly, so no blob ever passes through the API or SQL Server.
    /// </summary>
    CloudinaryUploadSignature CreateUploadSignature(string folder);

    /// <summary>
    /// Builds an HTTPS delivery URL for a stored public id. Always https by
    /// construction, so a stored reference can never become mixed content.
    /// </summary>
    string BuildSecureUrl(string publicId, string? transformation = null);

    /// <summary>
    /// Upload metadata is posted by the browser and cannot be trusted. Returns
    /// false unless the value is an HTTPS URL on the configured cloud, which
    /// stops a client pointing an asset at a host of its choosing.
    /// </summary>
    bool IsTrustedSecureUrl(string? url);
}
