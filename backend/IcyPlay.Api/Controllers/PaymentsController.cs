using System.Security.Claims;
using IcyPlay.Application.Payments;
using IcyPlay.Domain.Identity;
using IcyPlay.Domain.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// A customer's own online payments, for the page they return to after paying.
/// </summary>
[ApiController]
[Authorize(Roles = UserRoleName.Customer)]
[Route("api/v1/payments")]
public sealed class PaymentsController(IOnlinePaymentService payments) : ControllerBase
{
    /// <summary>
    /// Asks the gateway directly whether the customer's open checkout for this
    /// booking, upgrade or registration has been paid, and settles it if so.
    /// What the "Confirming your payment" page calls while it waits, so a
    /// webhook that never arrives does not leave anybody stuck there. The page
    /// reads the outcome from the thing itself afterwards.
    /// </summary>
    [HttpPost("{purpose}/{subjectId:guid}/verify")]
    public async Task<IActionResult> Verify(string purpose, Guid subjectId, CancellationToken ct)
    {
        if (!PaymentPurpose.All.Contains(purpose))
        {
            return NotFound();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Unauthorized();
        }

        await payments.VerifyAsync(purpose, subjectId, userId, ct);

        return NoContent();
    }
}
