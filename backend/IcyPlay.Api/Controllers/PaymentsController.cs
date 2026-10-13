using System.Security.Claims;
using IcyPlay.Api.Common;
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
    /// What the second step offers: whether the customer pays on our page or
    /// the gateway's, each method's total, and the attempt under way.
    /// </summary>
    [HttpGet("{purpose}/{subjectId:guid}/options")]
    public async Task<IActionResult> Options(string purpose, Guid subjectId, CancellationToken ct)
    {
        if (!PaymentPurpose.All.Contains(purpose))
        {
            return NotFound();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Unauthorized();
        }

        var result = await payments.GetOptionsAsync(purpose, subjectId, userId, ct);

        return result.Succeeded ? Ok(result.Value) : PaymentFailureResults.ToResult(result.Failure);
    }

    /// <summary>
    /// Starts paying by the method the customer picked: a QR code to show, or
    /// a page to send them to. Asked again for the same method, it hands back
    /// the one already open.
    /// </summary>
    [HttpPost("{purpose}/{subjectId:guid}/start")]
    public async Task<IActionResult> Start(
        string purpose,
        Guid subjectId,
        [FromBody] StartPaymentRequest request,
        CancellationToken ct)
    {
        if (!PaymentPurpose.All.Contains(purpose))
        {
            return NotFound();
        }

        if (!Guid.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
        {
            return Unauthorized();
        }

        if (string.IsNullOrWhiteSpace(request?.Method))
        {
            return PaymentFailureResults.ToResult(PaymentFailure.UnknownMethod);
        }

        var result = await payments.StartMethodAsync(purpose, subjectId, userId, request.Method, ct);

        return result.Succeeded ? Ok(result.Value) : PaymentFailureResults.ToResult(result.Failure);
    }

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
