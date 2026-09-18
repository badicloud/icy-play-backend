using IcyPlay.Application.Audit;

namespace IcyPlay.Application.Facilities;

/// <summary>
/// What a seeding run produced, so the console can say more than "done".
/// </summary>
public sealed record SeedResult(
    Guid FacilityOwnerId,
    Guid FacilityId,
    string FacilityName,
    string SignInEmail,
    IReadOnlyCollection<string> Courts);

/// <summary>
/// What is standing there now, so the console can say what removing it would
/// cost before anybody presses the button.
///
/// The bookings are the number that matters. Everything else a seeded venue
/// holds was put there by the seeder; a booking was put there by somebody
/// trying the product, and it goes with the court it was made on.
/// </summary>
public sealed record SeededVenueSummary(
    Guid FacilityOwnerId,
    string BusinessName,
    string SignInEmail,
    DateTimeOffset SeededAt,
    int Facilities,
    int Courts,
    int Bookings);

/// <summary>
/// What this admin may do here, so the console shows one button or two.
///
/// Two answers rather than one, because removing is narrower than building:
/// building adds rows nobody was relying on, removing takes away whatever has
/// been done with them since.
/// </summary>
public sealed record SeedAllowance(bool MayBuild, bool MayRemove);

/// <summary>What a removal actually took away.</summary>
public sealed record SeedRemovalResult(
    int Venues,
    int Facilities,
    int Courts,
    int Bookings);

/// <summary>
/// Where the seeded owner's post should go. Blank falls back to a throwaway
/// public inbox.
/// </summary>
public sealed record SeedVenueRequest(string? OwnerEmail);

/// <summary>
/// Builds a venue with courts on it, for demonstrating and for testing against
/// something that looks like a real one.
///
/// It goes through the ordinary court service rather than writing rows: a venue
/// seeded by a shortcut is a venue whose bookable courts, audit trail and
/// pricing were never built the way a real one's are, and the first thing it
/// does is behave differently from the thing it was meant to stand in for.
/// </summary>
public interface ISeedService
{
    /// <summary>
    /// One owner, one facility, and five courts on it — each set up for
    /// basketball, badminton two ways, pickleball three ways and volleyball,
    /// priced as the existing venue is. No events: those are priced differently
    /// and are not what this is for.
    /// </summary>
    /// <param name="ownerEmail">
    /// Where the seeded owner's post goes. An address somebody can actually
    /// read, because the only way into that account afterwards is a password
    /// reset — and a reset sent to a made-up address is an account nobody can
    /// open. Blank falls back to a throwaway public inbox, which is the usual
    /// case and saves inventing an address per run.
    /// </param>
    Task<SeedResult> BuildVenueAsync(
        AuditActor actor,
        string? ownerEmail,
        CancellationToken cancellationToken);

    /// <summary>
    /// Every venue the seeder raised, with what each one is holding. Read
    /// before removing, so the console can show what is about to go rather
    /// than asking for a blind yes.
    /// </summary>
    Task<IReadOnlyCollection<SeededVenueSummary>> SeededVenuesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Removes every seeded venue and everything under it — facilities,
    /// courts, hours, photos, and the bookings made against them — together
    /// with the account the seeder created for it.
    ///
    /// Scoped by the marker the seeder writes, never by name. A venue called
    /// "Demo something" that somebody is actually trading from is a real
    /// venue, and matching on the name is how it would be deleted.
    /// </summary>
    Task<SeedRemovalResult> RemoveSeededAsync(AuditActor actor, CancellationToken cancellationToken);
}
