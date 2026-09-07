using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using IcyPlay.Application.Storage;
using Microsoft.Extensions.Options;

namespace IcyPlay.Infrastructure.Storage;

/// <summary>
/// Signs browser uploads and builds delivery URLs by hand rather than pulling in
/// the Cloudinary SDK. Signing is a sorted query string and a SHA-1, and every
/// URL this type produces starts with https, so a stored asset can never come
/// back as mixed content.
/// </summary>
public sealed class CloudinaryAssetService(
    IOptions<CloudinaryOptions> options,
    TimeProvider timeProvider) : ICloudinaryAssetService
{
    private const string DeliveryHost = "res.cloudinary.com";

    private readonly CloudinaryOptions cloudinaryOptions = options.Value;

    public CloudinaryUploadSignature CreateUploadSignature(string folder)
    {
        ValidateConfiguration();

        if (string.IsNullOrWhiteSpace(folder))
        {
            throw new ArgumentException("An upload folder is required.", nameof(folder));
        }

        var timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds();

        // Cloudinary signs the parameters that are sent with the upload, sorted
        // by key and joined as a query string, with the API secret appended.
        var parameters = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["folder"] = folder,
            ["timestamp"] = timestamp.ToString(CultureInfo.InvariantCulture)
        };

        var payload = string.Join("&", parameters.Select(pair => $"{pair.Key}={pair.Value}"));
        var signature = Convert.ToHexString(
                SHA1.HashData(Encoding.UTF8.GetBytes(payload + cloudinaryOptions.ApiSecret)))
            .ToLowerInvariant();

        return new CloudinaryUploadSignature(
            cloudinaryOptions.CloudName,
            cloudinaryOptions.ApiKey,
            folder,
            timestamp,
            signature);
    }

    public string BuildSecureUrl(string publicId, string? transformation = null)
    {
        ValidateConfiguration();

        if (string.IsNullOrWhiteSpace(publicId))
        {
            throw new ArgumentException("A public id is required.", nameof(publicId));
        }

        var segments = string.IsNullOrWhiteSpace(transformation)
            ? publicId.Trim()
            : $"{transformation.Trim()}/{publicId.Trim()}";

        return $"https://{DeliveryHost}/{cloudinaryOptions.CloudName}/image/upload/{segments}";
    }

    public bool IsTrustedSecureUrl(string? url)
    {
        if (string.IsNullOrWhiteSpace(url) ||
            !Uri.TryCreate(url, UriKind.Absolute, out var parsed))
        {
            return false;
        }

        // Cloudinary returns both "url" (http) and "secure_url" (https). Only the
        // second is acceptable, and only on our own cloud.
        return parsed.Scheme == Uri.UriSchemeHttps &&
            string.Equals(parsed.Host, DeliveryHost, StringComparison.OrdinalIgnoreCase) &&
            parsed.AbsolutePath.StartsWith(
                $"/{cloudinaryOptions.CloudName}/",
                StringComparison.Ordinal);
    }

    private void ValidateConfiguration()
    {
        if (string.IsNullOrWhiteSpace(cloudinaryOptions.CloudName) ||
            string.IsNullOrWhiteSpace(cloudinaryOptions.ApiKey) ||
            string.IsNullOrWhiteSpace(cloudinaryOptions.ApiSecret))
        {
            throw new InvalidOperationException(
                "Cloudinary cloud name, API key, and API secret must be configured.");
        }
    }
}
