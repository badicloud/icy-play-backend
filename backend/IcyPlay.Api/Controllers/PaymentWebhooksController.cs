using IcyPlay.Application.Payments;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace IcyPlay.Api.Controllers;

/// <summary>
/// Where the payment gateway tells us something was paid.
///
/// Anonymous, because the gateway has no account here: what makes an event
/// believable is its signature, checked against the exact bytes that arrived.
/// That is why the body is read raw rather than bound — re-serialising it
/// changes the bytes and no signature would ever match.
/// </summary>
[ApiController]
[AllowAnonymous]
[DisableRateLimiting]
[Route("api/v1/webhooks")]
public sealed class PaymentWebhooksController(IOnlinePaymentService payments) : ControllerBase
{
    [HttpPost("paymongo")]
    public async Task<IActionResult> PayMongo(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);
        var body = await reader.ReadToEndAsync(ct);

        var outcome = await payments.HandleWebhookAsync(body, Request.Headers["Paymongo-Signature"], ct);

        // Anything but a 2xx and the gateway sends it again. An unsigned
        // request is turned away; everything genuine is acknowledged, including
        // events we do not act on, so they are not retried for ever.
        return outcome == WebhookOutcome.Rejected ? Unauthorized() : Ok();
    }
}
