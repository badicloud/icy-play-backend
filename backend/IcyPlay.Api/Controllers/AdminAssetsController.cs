using IcyPlay.Api.Common;
using IcyPlay.Application.Storage;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// Hands the browser a signature so it can upload straight to Cloudinary. The
/// file never passes through the API, and the API secret never leaves it.
/// </summary>
[ApiController]
[Authorize(Roles = UserRoleName.PlatformAdmin)]
[Route("api/v1/admin/assets")]
public sealed class AdminAssetsController(ICloudinaryAssetService assets) : ControllerBase
{
    /// <summary>
    /// Purposes rather than a free-text folder. A caller that can name its own
    /// destination can scatter uploads anywhere in the account, and the folder
    /// is what the signature commits to.
    /// </summary>
    private static readonly Dictionary<string, string> Folders =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["facility-owner-document"] = "icyplay/facility-owners/documents",
            ["facility-photo"] = "icyplay/facilities/photos"
        };

    [HttpPost("upload-signature")]
    public IActionResult CreateUploadSignature(UploadSignatureRequest request)
    {
        if (request.Purpose is null || !Folders.TryGetValue(request.Purpose, out var folder))
        {
            return BadRequest(new ApiErrorEnvelope(new ApiError(
                ErrorCodes.BadRequest,
                $"Purpose must be one of: {string.Join(", ", Folders.Keys)}.")));
        }

        return Ok(new ApiEnvelope<CloudinaryUploadSignature>(assets.CreateUploadSignature(folder)));
    }
}

public sealed record UploadSignatureRequest(string? Purpose);
