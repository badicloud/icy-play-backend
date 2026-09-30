using IcyPlay.Application.OpenPlays;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Common;

/// <summary>
/// How a refused open play action is said, for the desk and the platform admin
/// alike. In one place so the two consoles cannot tell somebody different
/// things about the same rule.
/// </summary>
public static class OpenPlayErrors
{
    public static IActionResult Failure<T>(OpenPlayResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var (status, code, message) = result.Failure switch
        {
            OpenPlayFailure.NotFound or OpenPlayFailure.NotAttended => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "That open play or court is not at one of these venues."),
            OpenPlayFailure.NotADraft => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This open play is published and open for registration, so it cannot be changed or deleted."),
            OpenPlayFailure.NotPublished => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This open play is still a draft. Delete it instead of ending it."),
            OpenPlayFailure.Ended => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This open play has ended."),
            OpenPlayFailure.HasRegistrations => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Players have already registered, so it cannot go back to a draft. End it instead."),
            OpenPlayFailure.Clashes => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Some of these hours are already taken. Move or cancel what is in the way, or change the draft, then publish again."),
            OpenPlayFailure.UntrustedPhoto => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "That photo was not uploaded through IcyPlay. Upload it again from this page."),
            OpenPlayFailure.OutsideOpeningHours or OpenPlayFailure.StartsInThePast or OpenPlayFailure.Invalid => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                result.Message ?? "Check the open play's details."),
            _ => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                result.Message ?? "That could not be done.")
        };

        // The clashes travel as the error's details, so the page can list what
        // is in the way beside the open play it is about.
        var details = result.Failure == OpenPlayFailure.Clashes ? result.Clashes : null;

        return new ObjectResult(new ApiErrorEnvelope(new ApiError(code, message, details)))
        {
            StatusCode = status
        };
    }
}
