namespace IcyPlay.Application.OpenPlays;

/// <summary>
/// One open play as the public site shows it: what, where, when, and what a
/// player pays, with the next few dates it runs on.
/// </summary>
public sealed record CatalogOpenPlay(
    Guid OpenPlayId,
    string Title,
    Guid FacilityId,
    string FacilityName,
    string City,
    string CourtName,
    string SportKey,
    string SportName,
    string Level,
    IReadOnlyCollection<string> Days,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    DateOnly StartDate,
    DateOnly? EndDate,
    int MaxPlayers,
    // The venue's fee.
    decimal RegistrationFee,
    // The platform's top-up, added once per registration.
    decimal PlatformFee,
    // What a player pays without the early bird: fee plus top-up.
    decimal Price,
    int RegistrationCutoffMinutes,
    CatalogEarlyBird? EarlyBird,
    IReadOnlyCollection<CatalogOpenPlaySession> UpcomingSessions,
    // The open play's own cover photo, else the court set up for this sport,
    // else the court, else the venue: the same fallback the court listing uses.
    // Null when none of them has one.
    string? CoverPhotoUrl);

public sealed record CatalogEarlyBird(string DiscountKind, decimal DiscountValue, int LeadMinutes);

/// <summary>
/// One upcoming date. Registration times are on the venue's clock, worked out
/// on the server; the browser only displays them.
/// </summary>
public sealed record CatalogOpenPlaySession(
    DateOnly Date,
    int SpotsLeft,
    DateTime RegistrationClosesAt,
    bool IsOpenForRegistration,
    // What a player registering right now would pay, early bird included.
    decimal PriceNow,
    bool EarlyBirdNow);
