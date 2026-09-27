using IcyPlay.Application.Facilities;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Common;

/// <summary>
/// How an attendant change that did not go through is answered — the same
/// words whether the platform admin or the venue's owner was making it.
/// </summary>
public static class AttendantFailureResults
{
    public static IActionResult ToResult(AttendantFailure failure)
    {
        var (status, code, message) = failure switch
        {
            AttendantFailure.FacilityNotFound => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "That facility does not exist, or does not belong to this owner."),
            AttendantFailure.AttendantNotFound => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "That person is not on this venue's desk."),
            AttendantFailure.AlreadyAttending => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "They already work this venue."),
            AttendantFailure.IsTheOwner => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "The owner attends their own venue already."),
            AttendantFailure.InvitationAlreadyAccepted => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "They have already set up their account, so there is nothing to resend."),
            AttendantFailure.EmailAlreadyRegistered => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "That address already has an IcyPlay account, so it cannot be used. Try one nobody has signed up with."),
            _ => (StatusCodes.Status400BadRequest, ErrorCodes.BadRequest, "The request could not be completed.")
        };

        return new ObjectResult(new ApiErrorEnvelope(new ApiError(code, message))) { StatusCode = status };
    }
}
