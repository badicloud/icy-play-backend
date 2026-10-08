using IcyPlay.Application.Payments;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Common;

/// <summary>
/// How a checkout that could not be opened is answered — the same words for a
/// booking, an upgrade or an open play.
/// </summary>
public static class PaymentFailureResults
{
    public static IActionResult ToResult(PaymentFailure failure)
    {
        var (status, code, message) = failure switch
        {
            PaymentFailure.NotFound => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "There is nothing here to pay for."),
            PaymentFailure.NotPaidOnline => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This venue takes payment by GCash receipt, not online."),
            PaymentFailure.NotAwaitingPayment => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This is not waiting to be paid for."),
            PaymentFailure.HoldExpired => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "The hold ran out and the hours went back on sale. Book again to pay."),
            PaymentFailure.GatewayNotConfigured or PaymentFailure.GatewayUnavailable => (
                StatusCodes.Status503ServiceUnavailable,
                ErrorCodes.UnexpectedError,
                "Online payment is not available right now. Please try again in a moment."),
            _ => (StatusCodes.Status400BadRequest, ErrorCodes.BadRequest, "The request could not be completed.")
        };

        return new ObjectResult(new ApiErrorEnvelope(new ApiError(code, message))) { StatusCode = status };
    }
}
