using IcyPlay.Domain.OpenPlays;
using IcyPlay.Infrastructure.Bookings;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>
/// One open play session as the desk reaches it: the check-in door and the
/// matches board both go through it, so both answer the same people.
/// </summary>
internal sealed record SessionDoor(
    OpenPlay OpenPlay,
    DateOnly Date,
    string FacilityName,
    string CourtName,
    string UnitLabel,
    string SportKey,
    string SportName,
    string TimeZone,
    bool IsOpen,
    // The session has finished on the venue's clock.
    bool IsOver)
{
    /// <summary>Players per team in force: the open play's own, else the sport's.</summary>
    public int TeamSize => OpenPlay.TeamSizeFor(SportKey);

    /// <summary>
    /// The session, only if it is a published open play at a venue this person
    /// works, on a date it runs and that has not been cancelled.
    /// </summary>
    public static async Task<SessionDoor?> FindAsync(
        AppDbContext db,
        TimeProvider timeProvider,
        Guid userId,
        Guid openPlayId,
        DateOnly date,
        CancellationToken ct)
    {
        var venueIds = db.Facilities
            .Where(facility => facility.FacilityOwner.UserId == userId
                || facility.Attendants.Any(attendant => attendant.UserId == userId && attendant.IsActive))
            .Select(facility => facility.Id);

        var row = await db.OpenPlays
            .AsNoTracking()
            .Where(openPlay => openPlay.Id == openPlayId
                && openPlay.PublishedAt != null
                && venueIds.Contains(openPlay.FacilityId))
            .Select(openPlay => new
            {
                OpenPlay = openPlay,
                FacilityName = openPlay.Facility.Name,
                openPlay.Facility.TimeZone,
                CourtName = openPlay.BookableCourt.Court.Name,
                SportKey = openPlay.BookableCourt.CourtSport.Sport.Key,
                SportName = openPlay.BookableCourt.CourtSport.Sport.Name,
                openPlay.BookableCourt.DivisionNumber,
                openPlay.BookableCourt.CourtSport.Divisions,
                Cancelled = db.OpenPlaySessions.Any(session =>
                    session.OpenPlayId == openPlay.Id && session.Date == date && session.CancelledAt != null)
            })
            .SingleOrDefaultAsync(ct);

        if (row is null || row.Cancelled || !row.OpenPlay.RunsOn(date))
        {
            return null;
        }

        var venueNow = VenueClock.LocalNowIn(row.TimeZone, timeProvider.GetUtcNow()).DateTime;

        return new SessionDoor(
            row.OpenPlay,
            date,
            row.FacilityName,
            row.CourtName,
            DeskService.UnitLabel(row.SportName, row.DivisionNumber, row.Divisions),
            row.SportKey,
            row.SportName,
            row.TimeZone,
            row.OpenPlay.IsCheckInOpen(date, venueNow),
            venueNow >= date.ToDateTime(row.OpenPlay.EndsAt));
    }
}
