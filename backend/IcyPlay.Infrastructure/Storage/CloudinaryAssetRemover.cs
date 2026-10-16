using System.Globalization;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using IcyPlay.Application.Storage;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IcyPlay.Infrastructure.Storage;

/// <summary>
/// Deletes images through Cloudinary's destroy call, signed by hand like the
/// uploads: the parameters sorted and joined, the API secret appended, SHA-1.
/// </summary>
public sealed class CloudinaryAssetRemover(
    HttpClient http,
    IOptions<CloudinaryOptions> options,
    TimeProvider timeProvider,
    ILogger<CloudinaryAssetRemover> logger) : ICloudinaryAssetRemover
{
    private readonly CloudinaryOptions cloudinaryOptions = options.Value;

    public async Task<bool> DestroyAsync(string publicId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicId);

        var timestamp = timeProvider.GetUtcNow().ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var payload = $"public_id={publicId}&timestamp={timestamp}";
        var signature = Convert.ToHexString(
                SHA1.HashData(Encoding.UTF8.GetBytes(payload + cloudinaryOptions.ApiSecret)))
            .ToLowerInvariant();

        using var form = new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["public_id"] = publicId,
            ["timestamp"] = timestamp,
            ["api_key"] = cloudinaryOptions.ApiKey,
            ["signature"] = signature
        });

        try
        {
            using var response = await http.PostAsync(
                new Uri($"https://api.cloudinary.com/v1_1/{cloudinaryOptions.CloudName}/image/destroy"),
                form,
                cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Cloudinary refused to delete {PublicId}: {Status}.", publicId, (int)response.StatusCode);
                return false;
            }

            var answer = await response.Content.ReadFromJsonAsync<DestroyAnswer>(cancellationToken);

            // "not found" is as good as deleted: there is nothing left to keep.
            return answer?.Result is "ok" or "not found";
        }
        catch (HttpRequestException problem)
        {
            logger.LogWarning(problem, "Cloudinary could not be reached to delete {PublicId}.", publicId);
            return false;
        }
    }

    private sealed record DestroyAnswer(string? Result);
}
