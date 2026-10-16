using IcyPlay.Application.OpenPlays;
using Microsoft.AspNetCore.Mvc;

namespace IcyPlay.Api.Common;

/// <summary>How a refused registration or desk action is said, to the player and to the desk.</summary>
public static class OpenPlayRegistrationErrors
{
    public static IActionResult Failure<T>(OpenPlayRegistrationResult<T> result)
    {
        ArgumentNullException.ThrowIfNull(result);

        var (status, code, message) = result.Failure switch
        {
            OpenPlayRegistrationFailure.NotFound => (
                StatusCodes.Status404NotFound,
                ErrorCodes.NotFound,
                "That open play or registration could not be found."),
            OpenPlayRegistrationFailure.NotRunning => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This open play does not run on that date."),
            OpenPlayRegistrationFailure.SessionCancelled => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "The venue has cancelled this session."),
            OpenPlayRegistrationFailure.RegistrationClosed => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Registration for this session has closed."),
            OpenPlayRegistrationFailure.Full => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This session is full. Another date may still have spots."),
            OpenPlayRegistrationFailure.AlreadyRegistered => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "You are already registered for this session."),
            OpenPlayRegistrationFailure.PolicyNotAgreed => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Read and agree to the open play policy to continue."),
            OpenPlayRegistrationFailure.VenueCannotBePaid => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This venue has not set up a way to be paid yet, so it cannot take registrations."),
            OpenPlayRegistrationFailure.HoldExpired => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Your hold on this spot ran out before the receipt arrived. Register again if there is still a spot."),
            OpenPlayRegistrationFailure.NotAwaitingPayment => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This registration is no longer waiting for a payment."),
            OpenPlayRegistrationFailure.PaidOnline => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This registration is paid online, so there is no receipt to send."),
            OpenPlayRegistrationFailure.UntrustedReceiptUrl => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "That receipt is not a secure link on the configured Cloudinary account."),
            OpenPlayRegistrationFailure.ReceiptNotAnImage => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "The receipt has to be a picture — a screenshot or a photo of your GCash confirmation."),
            OpenPlayRegistrationFailure.NotWaiting => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "That registration has already been decided. Refresh to see where it stands."),
            OpenPlayRegistrationFailure.UnknownReason => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Pick a reason from the list."),
            OpenPlayRegistrationFailure.CheckInClosed => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Check-in for this session is not open right now."),
            OpenPlayRegistrationFailure.NotRegistered => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Only a player the venue has confirmed can be checked in."),
            OpenPlayRegistrationFailure.CheckInCodeNotSet => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "The venue owner has not made a check-in code yet. Generate one in Settings first."),
            OpenPlayRegistrationFailure.WrongCheckInCode => (
                StatusCodes.Status403Forbidden,
                ErrorCodes.Forbidden,
                "That check-in code is not right."),
            OpenPlayRegistrationFailure.CheckInCodeLocked => (
                StatusCodes.Status429TooManyRequests,
                ErrorCodes.RateLimitExceeded,
                "Too many wrong codes. Try again in 5 minutes."),
            OpenPlayRegistrationFailure.FreeSpotsUsed => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Every free spot for this session is used. Raise the number of free spots on the open play to add more."),
            OpenPlayRegistrationFailure.SessionOver => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This session has finished."),
            OpenPlayRegistrationFailure.InvalidFreePlayer => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Enter the player's name, up to 100 characters, and a mobile number of up to 30."),
            OpenPlayRegistrationFailure.FreePlayerHasPlayed => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This player has been on court, so they stay on record. Mark them done on the matches page instead."),
            OpenPlayRegistrationFailure.MatchAlreadyStarted => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "That match has already started."),
            OpenPlayRegistrationFailure.CourtBusy => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Another match is on the court. Finish it first."),
            OpenPlayRegistrationFailure.NotNextMatch => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Matches are played in order. Start the earlier one first."),
            OpenPlayRegistrationFailure.TeamEmpty => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "A team has nobody on it yet."),
            OpenPlayRegistrationFailure.MatchShort => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "The teams are not full. Start it anyway, or wait for more players."),
            OpenPlayRegistrationFailure.MatchStillPlaying => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "A match is still on the court. Finish it first, then end the session."),
            OpenPlayRegistrationFailure.CannotReopenSession => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "This session can no longer be reopened: its time is up."),
            OpenPlayRegistrationFailure.SessionNotOver => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Take the result photos once the session has ended."),
            OpenPlayRegistrationFailure.InvalidResultPhoto => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "That photo could not be saved. Take it again."),
            OpenPlayRegistrationFailure.TeamsUneven => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "The teams are uneven. Move a player across so both teams have the same number."),
            OpenPlayRegistrationFailure.MatchNotPlaying => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Only the match on the court can be finished."),
            OpenPlayRegistrationFailure.InvalidMatchResult => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Choose Team A, Team B or a draw. A score is for both teams, from 0 to 999, and has to agree with who won."),
            OpenPlayRegistrationFailure.MatchNotFinished => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "Only a finished match's result can be changed."),
            OpenPlayRegistrationFailure.CannotPlayNextMatch => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "The lineup has changed. Pick again: a player in the next match, and somebody from a later one."),
            OpenPlayRegistrationFailure.CannotMoveTeam => (
                StatusCodes.Status409Conflict,
                ErrorCodes.Conflict,
                "The lineup has changed. Only a player in the next match can move, to the other team."),
            OpenPlayRegistrationFailure.UnknownTab => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "Ask for either the waiting registrations or the confirmed ones."),
            _ => (
                StatusCodes.Status400BadRequest,
                ErrorCodes.BadRequest,
                "That could not be done.")
        };

        return new ObjectResult(new ApiErrorEnvelope(new ApiError(code, message))) { StatusCode = status };
    }
}
