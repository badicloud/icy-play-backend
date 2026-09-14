using IcyPlay.Api.Common;
using IcyPlay.Application.Storage;
using IcyPlay.Domain.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// Hands a signed-in customer a signature so their browser can put a payment
/// receipt straight into Cloudinary. The file never passes through the API, and
/// the API secret never leaves it.
///
/// Separate from the admin one rather than opening that up: an admin can upload
/// permits, contracts and court photos, and a customer has no business writing
/// into any of those folders.
/// </summary>
[ApiController]
[Authorize(Roles = UserRoleName.Customer)]
[Route("api/v1/assets")]
public sealed class CustomerAssetsController(ICloudinaryAssetService assets) : ControllerBase
{
    /// <summary>
    /// One purpose, and the signature commits to its folder. A caller that can
    /// name its own destination can scatter uploads anywhere in the account.
    /// </summary>
    private const string ReceiptFolder = "icyplay/bookings/receipts";

    [HttpPost("upload-signature")]
    public IActionResult CreateUploadSignature(CustomerUploadSignatureRequest request)
    {
        if (!string.Equals(request?.Purpose, "booking-receipt", StringComparison.OrdinalIgnoreCase))
        {
            return BadRequest(new ApiErrorEnvelope(new ApiError(
                ErrorCodes.BadRequest,
                "Purpose must be booking-receipt.")));
        }

        return Ok(new ApiEnvelope<CloudinaryUploadSignature>(
            assets.CreateUploadSignature(ReceiptFolder)));
    }
}

public sealed record CustomerUploadSignatureRequest(string? Purpose);
