namespace IcyPlay.Application.OpenPlays;

/// <summary>What a seeding run put where, so the console can say more than "done".</summary>
public sealed record OpenPlaySeedResult(
    int Venues,
    IReadOnlyCollection<SeededOpenPlaySummary> OpenPlays,
    IReadOnlyCollection<string> Skipped);

/// <summary>
/// One sample open play that is standing now. The registrations are the number
/// that matters: the seeder made everything else, but a registration was made
/// by somebody trying the product, and it goes with the open play.
/// </summary>
public sealed record SeededOpenPlaySummary(
    Guid OpenPlayId,
    string Title,
    string FacilityName,
    string CourtName,
    string SportName,
    string Level,
    string Days,
    TimeOnly StartsAt,
    TimeOnly EndsAt,
    decimal RegistrationFee,
    int MaxPlayers,
    int Registrations);

/// <summary>What a removal actually took away.</summary>
public sealed record OpenPlaySeedRemovalResult(int OpenPlays, int Sessions, int Registrations);
