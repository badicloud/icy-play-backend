using IcyPlay.Application.Bookings;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Bookings;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.Bookings;

/// <summary>
/// What has happened to one booking, read from the platform's own trail.
///
/// Shared between the customer's history and the venue desk's, because it is
/// one account of one booking: two readers of the same events who disagreed
/// about what they said would be worse than either of them being wrong.
///
/// Authorization is the caller's job. The customer's screen asks whether the
/// booking is theirs; the desk asks whether it is at a venue they work. Both
/// then hand the booking over, and what comes back is the same.
/// </summary>
internal static class BookingHistory
{
    public static async Task<IReadOnlyCollection<BookingHistoryEntry>> ReadAsync(
        AppDbContext db,
        Booking booking,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var entries = await db.AuditLogs
            .AsNoTracking()
            .Where(entry =>
                entry.EntityType == AuditEntityType.Booking && entry.EntityId == booking.Id)
            .OrderByDescending(entry => entry.CreatedAt)
            .Select(entry => new
            {
                entry.Action,
                entry.NewValuesJson,
                entry.Reason,
                entry.CreatedAt
            })
            .ToListAsync(ct);

        var told = entries
            .Select(entry => new BookingHistoryEntry(
                entry.Action,
                Describe(entry.Action, entry.NewValuesJson),
                entry.Reason,
                entry.CreatedAt))
            .ToList();

        // An expiry leaves no row, because nothing is there to write one: a
        // booking does not change when its hold ends, it simply stops holding.
        // So it is worked out here, from the booking, by the same rule the card
        // uses to call itself expired. Without it the history of a booking that
        // lapsed says only that it was made — and stops, at the exact moment
        // the reader wants to know what became of it.
        if (booking.HasLapsedAt(now))
        {
            told.Add(new BookingHistoryEntry(
                AuditAction.BookingHoldExpired,
                "The hold ran out before payment arrived, so the hours went back on sale.",
                null,
                booking.HoldsUntil));
        }

        // Newest first. The entry somebody opens this for is almost always the
        // last one — what just happened to this booking — and a list that puts
        // it at the bottom makes them scroll past everything they already knew.
        return [.. told.OrderByDescending(entry => entry.At)];
    }

    /// <summary>
    /// What an entry says, in words.
    ///
    /// The description written at the time is preferred, because it knows
    /// things the action name cannot — which court, how much, where from. The
    /// switch is the fallback for entries written before anybody thought to
    /// store one.
    /// </summary>
    private static string Describe(string action, string? detailsJson)
    {
        if (!string.IsNullOrWhiteSpace(detailsJson))
        {
            try
            {
                var details = System.Text.Json.JsonSerializer
                    .Deserialize<Dictionary<string, string?>>(detailsJson);

                if (details?.GetValueOrDefault("description") is string written
                    && !string.IsNullOrWhiteSpace(written))
                {
                    return written;
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // An entry nobody can read is still an entry: it happened, and
                // the action and the time are worth more than nothing.
            }
        }

        return action switch
        {
            AuditAction.BookingCreated => "Booking made.",
            AuditAction.BookingPaymentSubmitted => "Payment sent to the venue to check.",
            AuditAction.BookingMoved => "Moved to another court.",
            AuditAction.BookingMoveRequested => "Asked the venue to move this booking.",
            AuditAction.BookingMoveApproved => "The venue agreed to the move.",
            AuditAction.BookingMoveDeclined => "The venue declined the move. The booking stays where it is.",
            AuditAction.BookingConfirmed => "The venue confirmed your booking.",
            AuditAction.BookingRejected => "The venue could not accept the payment.",
            AuditAction.BookingHoldExpired =>
                "The hold ran out before payment arrived, so the hours went back on sale.",
            _ => "Updated."
        };
    }
}
