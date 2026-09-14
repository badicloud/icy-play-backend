using IcyPlay.Domain.Facilities;
using IcyPlay.Infrastructure.Persistence;

namespace IcyPlay.Infrastructure.Facilities;

/// <summary>
/// The one place that decides what a court actually sells. Three callers change
/// the answer — creating a court, editing one, and re-marking the floor — and a
/// bookable court that exists on one path but not another is a court a customer
/// can see and cannot book, or book and cannot see.
///
/// Nothing here deletes. A part that is no longer marked out is retired, because
/// a booking taken against it still has to resolve, and the same part marked out
/// again comes back as the same row rather than a new one.
///
/// Works on entities the caller already has loaded, so the whole reconciliation
/// lands in the caller's own <c>SaveChanges</c>. A court and what it sells go in
/// together or not at all.
/// </summary>
internal static class BookableCourtRoster
{
    /// <summary>
    /// Brings a court's bookable courts in line with its sports and their
    /// division counts.
    ///
    /// Safe to call when nothing has changed: it writes only what differs, which
    /// is what lets every caller call it unconditionally rather than working out
    /// whether it needs to.
    /// </summary>
    /// <param name="pairs">
    /// The court's sports as they stand after the caller's edit, with their
    /// <see cref="CourtSport.BookableCourts"/> loaded. A sport dropped from the
    /// court is simply absent: its link row goes, and its bookable courts go
    /// with it.
    /// </param>
    public static void Reconcile(
        AppDbContext db,
        Guid courtId,
        IEnumerable<CourtSport> pairs,
        DateTimeOffset now)
    {
        foreach (var pair in pairs)
        {
            var kind = BookableCourtKind.For(pair.Divisions);

            foreach (var unit in pair.BookableCourts)
            {
                if (unit.DivisionNumber > pair.Divisions)
                {
                    // The floor was re-marked into fewer parts than this one is.
                    unit.Retire(now);
                    continue;
                }

                unit.Reinstate(now);
                // Part one of a court that becomes three was a whole court
                // yesterday. Same bookable court, described differently.
                unit.SetKind(kind, now);
            }

            var alreadyMarkedOut = pair.BookableCourts
                .Select(unit => unit.DivisionNumber)
                .ToHashSet();

            for (var number = 1; number <= pair.Divisions; number++)
            {
                if (alreadyMarkedOut.Contains(number))
                {
                    continue;
                }

                // Through the set, because a client-generated key added only to
                // a tracked navigation is read by EF as a row that already
                // exists.
                db.BookableCourts.Add(new BookableCourt(
                    pair.Id,
                    courtId,
                    number,
                    kind,
                    now));
            }
        }
    }
}
