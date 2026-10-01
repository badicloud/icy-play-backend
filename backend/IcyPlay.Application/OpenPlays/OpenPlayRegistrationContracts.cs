namespace IcyPlay.Application.OpenPlays;

/// <param name="Date">The session's date, on the venue's calendar.</param>
/// <param name="AgreedToPolicy">
/// The player ticked the open play policy: no refund through IcyPlay, because
/// the money goes straight to the venue. Refused without it.
/// </param>
public sealed record RegisterForOpenPlayRequest(DateOnly Date, bool AgreedToPolicy);

/// <summary>The link the browser has just put in Cloudinary. Checked, never trusted.</summary>
public sealed record OpenPlayReceiptRequest(string ReceiptUrl);

/// <summary>
/// One registration as its player sees it: what, where, when, what they owe
/// and how to pay the venue, and where it stands. Everything the checkout's
/// three steps and the "My open plays" page need.
/// </summary>
public sealed record OpenPlayRegistrationDetail(
    Guid RegistrationId,
    // "PendingPayment", "PendingVerification", "Confirmed", "Rejected", "Cancelled".
    string Status,
    Guid OpenPlayId,
    string Title,
    string Level,
    string SportKey,
    string SportName,
    Guid FacilityId,
    string FacilityName,
    string FacilitySlug,
    string City,
    string CourtName,
    string UnitLabel,
    DateOnly Date,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    decimal RegistrationFee,
    decimal Discount,
    decimal PlatformFee,
    decimal Total,
    DateTimeOffset HoldsUntil,
    // The hold ran out with no receipt: the spot is gone.
    bool HasLapsed,
    string? ReceiptUrl,
    DateTimeOffset? ReceiptUploadedAt,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? CancelledAt,
    // On a refusal: the reason the desk picked and its note, in one sentence.
    string? CancellationReason,
    DateTimeOffset AgreedToPolicyAt,
    DateTimeOffset CreatedAt,
    string? GcashNumber,
    string? GcashAccountName,
    string? GcashQrCodeUrl,
    // How to reach the venue: its published phone and email, or the owner's.
    string VenueContact,
    string? CoverPhotoUrl,
    // The session is over on the venue's clock: what "past" means on the
    // player's list. Decided here, never by the browser's clock.
    bool SessionHasEnded);

public enum OpenPlayRegistrationFailure
{
    None = 0,
    NotFound,
    // The open play is not published, has ended, or does not run on that date.
    NotRunning,
    SessionCancelled,
    // Registration for that date has closed at the venue.
    RegistrationClosed,
    Full,
    AlreadyRegistered,
    PolicyNotAgreed,
    VenueCannotBePaid,
    HoldExpired,
    NotAwaitingPayment,
    UntrustedReceiptUrl,
    ReceiptNotAnImage,
    NotWaiting,
    UnknownReason,
    UnknownTab
}

public sealed record OpenPlayRegistrationResult<T>(T? Value, OpenPlayRegistrationFailure Failure)
{
    public bool Succeeded => Failure == OpenPlayRegistrationFailure.None;

    public static OpenPlayRegistrationResult<T> Success(T value) => new(value, OpenPlayRegistrationFailure.None);

    public static OpenPlayRegistrationResult<T> Fail(OpenPlayRegistrationFailure failure) => new(default, failure);
}

/// <summary>
/// One registration in the desk's queue: who, for which open play and date,
/// what they paid and the picture of it.
/// </summary>
public sealed record DeskOpenPlayRequest(
    Guid RegistrationId,
    string Status,
    Guid OpenPlayId,
    string Title,
    Guid FacilityId,
    string FacilityName,
    string CourtName,
    string UnitLabel,
    DateOnly Date,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    string PlayerName,
    string PlayerEmail,
    string? PlayerPhone,
    decimal RegistrationFee,
    decimal Discount,
    decimal PlatformFee,
    decimal Total,
    string? ReceiptUrl,
    DateTimeOffset? SubmittedAt,
    DateTimeOffset? ConfirmedAt,
    // Registered players on that session now, against its most.
    int Registered,
    int MaxPlayers);

/// <summary>The desk's two queues, as the booking queue has them.</summary>
public static class OpenPlayRequestTab
{
    public const string Waiting = "Waiting";
    public const string Confirmed = "Confirmed";

    public static bool IsSupported(string? value) => value is Waiting or Confirmed;
}

/// <param name="Reason">One of <c>RejectReason</c>, the same list the booking queue offers.</param>
/// <param name="Note">A few words for the player, if the reason needs them.</param>
public sealed record RejectOpenPlayRequest(string Reason, string? Note);
