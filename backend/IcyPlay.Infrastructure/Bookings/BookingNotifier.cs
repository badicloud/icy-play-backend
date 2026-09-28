using IcyPlay.Application.Bookings;
using IcyPlay.Application.Email;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Email;
using IcyPlay.Infrastructure.Email;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IcyPlay.Infrastructure.Bookings;

/// <summary>
/// The letters a booking sends: when a receipt arrives, one to the customer
/// saying the court is held while it is checked and one to the venue asking
/// them to check it; then one to the customer when they do. An upgrade sends
/// the same three, about the change rather than the booking.
///
/// Nothing here throws. A booking that is already saved must not be reported as
/// failed because a mail provider was having a bad afternoon: the venue still
/// sees it in their queue, and a missing email is a nuisance rather than a lost
/// court. Failures are logged loudly instead.
/// </summary>
public sealed class BookingNotifier(
    AppDbContext db,
    ITransactionalEmailSender emailSender,
    IOptions<BookingNotificationOptions> options,
    TimeProvider timeProvider,
    ILogger<BookingNotifier> logger) : IBookingNotifier
{
    private BookingNotificationOptions Settings => options.Value;
    public async Task PaymentSubmittedAsync(Booking booking, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(booking);

        var parties = await PartiesAsync(booking.Id, ct);

        if (parties is null)
        {
            return;
        }

        var days = await DaysAsync(booking.Id, ct);

        if (parties.CustomerEmail.Length > 0)
        {
            await SendAsync(
                new TransactionalEmailMessage(
                    EmailTemplateKey.BookingPaymentReceived,
                    parties.CustomerEmail,
                    parties.CustomerName,
                    new Dictionary<string, object>
                    {
                        ["recipient_name"] = parties.CustomerName,
                        ["court_name"] = booking.CourtName,
                        ["facility_name"] = booking.FacilityName,
                        ["sport_name"] = booking.SportName,
                        ["booking_dates"] = Dates(days),
                        ["booked_hours"] = booking.BookedHours,
                        ["rental_amount"] = Money(booking.RentalTotal),
                        ["platform_fee"] = Money(booking.PlatformFeeTotal),
                        ["total_amount"] = Money(booking.Total),
                        ["booking_url"] = BookingUrl(booking.Id),
                        ["support_email"] = Settings.SupportEmail,
                        ["current_year"] = timeProvider.GetUtcNow().Year
                    }),
                booking.Id,
                ct);
        }

        await SendToDeskAsync(
            parties,
            EmailTemplateKey.BookingPaymentSubmitted,
            name => new Dictionary<string, object>
            {
                ["recipient_name"] = name,
                ["business_name"] = parties.BusinessName,
                ["customer_name"] = parties.CustomerName,
                ["customer_email"] = parties.CustomerEmail,
                ["court_name"] = booking.CourtName,
                ["facility_name"] = booking.FacilityName,
                ["sport_name"] = booking.SportName,
                ["booking_dates"] = Dates(days),
                ["booked_hours"] = booking.BookedHours,
                ["total_amount"] = Money(booking.Total),
                ["receipt_url"] = booking.ReceiptUrl ?? string.Empty,
                // The picture to look at, and the queue to act in. They
                // are different places and the letter needs both.
                ["confirmations_url"] = Settings.ConfirmationsUrl,
                ["booking_url"] = BookingUrl(booking.Id),
                ["support_email"] = Settings.SupportEmail,
                ["current_year"] = timeProvider.GetUtcNow().Year
            },
            booking.Id,
            ct);
    }

    public async Task BookingConfirmedAsync(Booking booking, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(booking);

        var parties = await PartiesAsync(booking.Id, ct);

        if (parties is null || parties.CustomerEmail.Length == 0)
        {
            return;
        }

        var days = await DaysAsync(booking.Id, ct);

        await SendAsync(
            new TransactionalEmailMessage(
                EmailTemplateKey.BookingConfirmed,
                parties.CustomerEmail,
                parties.CustomerName,
                new Dictionary<string, object>
                {
                    ["recipient_name"] = parties.CustomerName,
                    ["court_name"] = booking.CourtName,
                    ["facility_name"] = booking.FacilityName,
                    ["sport_name"] = booking.SportName,
                    ["booking_dates"] = Dates(days),
                    ["booked_hours"] = booking.BookedHours,
                    ["rental_amount"] = Money(booking.RentalTotal),
                    ["platform_fee"] = Money(booking.PlatformFeeTotal),
                    ["total_amount"] = Money(booking.Total),
                    ["booking_url"] = BookingUrl(booking.Id),
                    ["support_email"] = Settings.SupportEmail,
                    ["current_year"] = timeProvider.GetUtcNow().Year
                }),
            booking.Id,
            ct);
    }

    public async Task BookingDeclinedAsync(Booking booking, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(booking);

        var parties = await PartiesAsync(booking.Id, ct);

        if (parties is null || parties.CustomerEmail.Length == 0)
        {
            return;
        }

        var days = await DaysAsync(booking.Id, ct);

        await SendAsync(
            new TransactionalEmailMessage(
                EmailTemplateKey.BookingDeclined,
                parties.CustomerEmail,
                parties.CustomerName,
                new Dictionary<string, object>
                {
                    ["recipient_name"] = parties.CustomerName,
                    ["court_name"] = booking.CourtName,
                    ["facility_name"] = booking.FacilityName,
                    ["sport_name"] = booking.SportName,
                    ["booking_dates"] = Dates(days),
                    ["booked_hours"] = booking.BookedHours,
                    ["total_amount"] = Money(booking.Total),
                    // The one sentence the booking page shows too: the reason
                    // the desk picked, and its note.
                    ["decline_reason"] = booking.CancellationReason ?? "The venue could not accept the payment.",
                    ["venue_contact"] = parties.VenueContact,
                    ["booking_url"] = BookingUrl(booking.Id),
                    ["support_email"] = Settings.SupportEmail,
                    ["current_year"] = timeProvider.GetUtcNow().Year
                }),
            booking.Id,
            ct);
    }

    public async Task UpgradeSubmittedAsync(BookingUpgradeRequest upgrade, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(upgrade);

        var about = await UpgradeAsync(upgrade.Id, ct);

        if (about is null)
        {
            return;
        }

        if (about.Parties.CustomerEmail.Length > 0)
        {
            await SendAsync(
                new TransactionalEmailMessage(
                    EmailTemplateKey.BookingUpgradeReceived,
                    about.Parties.CustomerEmail,
                    about.Parties.CustomerName,
                    new Dictionary<string, object>
                    {
                        ["recipient_name"] = about.Parties.CustomerName,
                        ["from_court_name"] = about.FromCourtName,
                        ["to_court_name"] = upgrade.ToCourtName,
                        ["facility_name"] = about.FacilityName,
                        ["sport_name"] = about.SportName,
                        ["upgrade_dates"] = Dates(upgrade),
                        ["upgrade_hours"] = Hours(upgrade),
                        ["balance_due"] = Money(upgrade.BalanceDue),
                        ["booking_url"] = BookingUrl(upgrade.BookingId),
                        ["support_email"] = Settings.SupportEmail,
                        ["current_year"] = timeProvider.GetUtcNow().Year
                    }),
                upgrade.BookingId,
                ct);
        }

        await SendToDeskAsync(
            about.Parties,
            EmailTemplateKey.BookingUpgradeSubmitted,
            name => new Dictionary<string, object>
            {
                ["recipient_name"] = name,
                ["business_name"] = about.Parties.BusinessName,
                ["customer_name"] = about.Parties.CustomerName,
                ["customer_email"] = about.Parties.CustomerEmail,
                ["from_court_name"] = about.FromCourtName,
                ["to_court_name"] = upgrade.ToCourtName,
                ["facility_name"] = about.FacilityName,
                ["sport_name"] = about.SportName,
                ["upgrade_dates"] = Dates(upgrade),
                ["upgrade_hours"] = Hours(upgrade),
                // All three figures, because the desk is checking a bank
                // statement against one of them and the wrong one is the
                // obvious one. What landed is the difference.
                ["rental_now"] = Money(upgrade.RentalNow),
                ["rental_new"] = Money(upgrade.RentalNew),
                ["balance_due"] = Money(upgrade.BalanceDue),
                ["receipt_url"] = upgrade.ReceiptUrl ?? string.Empty,
                ["upgrades_url"] = Settings.UpgradesUrl,
                ["support_email"] = Settings.SupportEmail,
                ["current_year"] = timeProvider.GetUtcNow().Year
            },
            upgrade.BookingId,
            ct);
    }

    public async Task UpgradeApprovedAsync(BookingUpgradeRequest upgrade, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(upgrade);

        var about = await UpgradeAsync(upgrade.Id, ct);

        if (about is null || about.Parties.CustomerEmail.Length == 0)
        {
            return;
        }

        await SendAsync(
            new TransactionalEmailMessage(
                EmailTemplateKey.BookingUpgradeApproved,
                about.Parties.CustomerEmail,
                about.Parties.CustomerName,
                new Dictionary<string, object>
                {
                    ["recipient_name"] = about.Parties.CustomerName,
                    ["from_court_name"] = about.FromCourtName,
                    ["to_court_name"] = upgrade.ToCourtName,
                    ["facility_name"] = about.FacilityName,
                    ["sport_name"] = about.SportName,
                    ["upgrade_dates"] = Dates(upgrade),
                    ["upgrade_hours"] = Hours(upgrade),
                    ["balance_due"] = Money(upgrade.BalanceDue),
                    ["booking_url"] = BookingUrl(upgrade.BookingId),
                    ["support_email"] = Settings.SupportEmail,
                    ["current_year"] = timeProvider.GetUtcNow().Year
                }),
            upgrade.BookingId,
            ct);
    }

    public async Task MoveRequestedAsync(BookingUpgradeRequest move, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(move);

        var about = await UpgradeAsync(move.Id, ct);

        if (about is null)
        {
            return;
        }

        var now = await HeldAsync(move.BookingId, ct);

        // The venue only. The customer just asked and is looking at the screen
        // that says it is with the venue; a letter repeating it is noise.
        await SendToDeskAsync(
            about.Parties,
            EmailTemplateKey.BookingMoveRequested,
            name => new Dictionary<string, object>
            {
                ["recipient_name"] = name,
                ["business_name"] = about.Parties.BusinessName,
                ["customer_name"] = about.Parties.CustomerName,
                ["customer_email"] = about.Parties.CustomerEmail,
                ["facility_name"] = about.FacilityName,
                ["sport_name"] = about.SportName,
                // Where it is, and where they would like it to be. The
                // desk is deciding between the two, so both are spelled out.
                ["from_court_name"] = about.FromCourtName,
                ["from_dates"] = now.Dates,
                ["from_hours"] = now.Hours,
                ["to_court_name"] = move.ToCourtName,
                ["to_dates"] = Dates(move),
                ["to_hours"] = Ranges(move.Slots.Select(slot => (slot.Date, slot.StartsAt, slot.EndsAt))),
                ["move_reason"] = Reason(move),
                ["requests_url"] = Settings.UpgradesUrl,
                ["support_email"] = Settings.SupportEmail,
                ["current_year"] = timeProvider.GetUtcNow().Year
            },
            move.BookingId,
            ct);
    }

    public async Task MoveApprovedAsync(
        BookingUpgradeRequest move,
        string fromCourtName,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(move);

        var about = await UpgradeAsync(move.Id, ct);

        if (about is null || about.Parties.CustomerEmail.Length == 0)
        {
            return;
        }

        var now = await HeldAsync(move.BookingId, ct);

        await SendAsync(
            new TransactionalEmailMessage(
                EmailTemplateKey.BookingMoveApproved,
                about.Parties.CustomerEmail,
                about.Parties.CustomerName,
                new Dictionary<string, object>
                {
                    ["recipient_name"] = about.Parties.CustomerName,
                    ["from_court_name"] = fromCourtName,
                    // The booking has moved, so what it holds now IS the
                    // answer: the court to walk to and when.
                    ["court_name"] = about.FromCourtName,
                    ["facility_name"] = about.FacilityName,
                    ["sport_name"] = about.SportName,
                    ["booking_dates"] = now.Dates,
                    ["booking_hours"] = now.Hours,
                    ["booking_url"] = BookingUrl(move.BookingId),
                    ["support_email"] = Settings.SupportEmail,
                    ["current_year"] = timeProvider.GetUtcNow().Year
                }),
            move.BookingId,
            ct);
    }

    public async Task MoveDeclinedAsync(BookingUpgradeRequest move, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(move);

        var about = await UpgradeAsync(move.Id, ct);

        if (about is null || about.Parties.CustomerEmail.Length == 0)
        {
            return;
        }

        var now = await HeldAsync(move.BookingId, ct);

        await SendAsync(
            new TransactionalEmailMessage(
                EmailTemplateKey.BookingMoveDeclined,
                about.Parties.CustomerEmail,
                about.Parties.CustomerName,
                new Dictionary<string, object>
                {
                    ["recipient_name"] = about.Parties.CustomerName,
                    // Still theirs, said first: nobody should turn up at the
                    // court they asked for.
                    ["court_name"] = about.FromCourtName,
                    ["booking_dates"] = now.Dates,
                    ["booking_hours"] = now.Hours,
                    ["facility_name"] = about.FacilityName,
                    ["sport_name"] = about.SportName,
                    ["asked_court_name"] = move.ToCourtName,
                    ["asked_dates"] = Dates(move),
                    ["asked_hours"] = Ranges(move.Slots.Select(slot => (slot.Date, slot.StartsAt, slot.EndsAt))),
                    ["decline_reason"] = move.DeclineReason ?? "The venue could not make the move.",
                    // Only an upgrade had money sent for it, and the venue has
                    // it — IcyPlay never does. Empty on a free move, so the
                    // template prints nothing.
                    ["money_note"] = move.IsFree || move.ReceiptUrl is null
                        ? string.Empty
                        : $"You sent ₱{Money(move.BalanceDue)} for the difference. The venue has it, so ask them to send it back.",
                    ["venue_contact"] = about.Parties.VenueContact,
                    ["booking_url"] = BookingUrl(move.BookingId),
                    ["support_email"] = Settings.SupportEmail,
                    ["current_year"] = timeProvider.GetUtcNow().Year
                }),
            move.BookingId,
            ct);
    }

    /// <summary>The customer's reason, as the desk reads it.</summary>
    private static string Reason(BookingUpgradeRequest move) =>
        move.MoveReason is null
            ? "Not given."
            : string.IsNullOrWhiteSpace(move.MoveReasonNote)
                ? MoveReason.Label(move.MoveReason)
                : $"{MoveReason.Label(move.MoveReason)} — {move.MoveReasonNote.Trim()}";

    /// <summary>What the booking holds right now, as dates and hours.</summary>
    private async Task<(string Dates, string Hours)> HeldAsync(Guid bookingId, CancellationToken ct)
    {
        var slots = await db.BookingSlots
            .AsNoTracking()
            .Where(slot => slot.BookingId == bookingId)
            .Select(slot => new { slot.Date, slot.StartsAt, slot.EndsAt })
            .ToListAsync(ct);

        return (
            Dates([.. slots.Select(slot => slot.Date).Distinct().OrderBy(date => date)]),
            Ranges(slots.Select(slot => (slot.Date, slot.StartsAt, slot.EndsAt))));
    }

    /// <summary>
    /// Hours as runs — "8:00 AM – 11:00 AM" rather than three start times —
    /// with the date in front of each day's once there is more than one day.
    /// </summary>
    private static string Ranges(IEnumerable<(DateOnly Date, TimeOnly StartsAt, TimeOnly EndsAt)> slots)
    {
        var culture = System.Globalization.CultureInfo.InvariantCulture;
        var days = slots
            .OrderBy(slot => slot.Date)
            .ThenBy(slot => slot.StartsAt)
            .GroupBy(slot => slot.Date)
            .ToArray();

        var parts = new List<string>();

        foreach (var day in days)
        {
            var runs = new List<(TimeOnly From, TimeOnly To)>();

            foreach (var slot in day)
            {
                if (runs.Count > 0 && runs[^1].To == slot.StartsAt)
                {
                    runs[^1] = (runs[^1].From, slot.EndsAt);
                }
                else
                {
                    runs.Add((slot.StartsAt, slot.EndsAt));
                }
            }

            var said = string.Join(
                ", ",
                runs.Select(run => $"{run.From.ToString("h:mm tt", culture)} – {run.To.ToString("h:mm tt", culture)}"));

            parts.Add(days.Length > 1 ? $"{day.Key.ToString("d MMM", culture)} {said}" : said);
        }

        return string.Join("; ", parts);
    }

    /// <summary>
    /// Who is on each side of an upgrade, and what the booking is leaving.
    ///
    /// The court it is moving FROM is read here rather than taken from the
    /// request, because the request only ever carried where it was going. On
    /// the approved letter it has already changed, so it is read before the
    /// caller saves — which is why both letters go out after the save and read
    /// their own copy.
    /// </summary>
    private async Task<AboutUpgrade?> UpgradeAsync(Guid upgradeId, CancellationToken ct)
    {
        var row = await db.BookingUpgradeRequests
            .AsNoTracking()
            .Where(candidate => candidate.Id == upgradeId)
            .Select(candidate => new
            {
                candidate.BookingId,
                FromCourtName = candidate.Booking.CourtName,
                candidate.Booking.FacilityName,
                candidate.Booking.SportName,
                Customer = db.Users
                    .Where(user => user.Id == candidate.RequestedByUserId)
                    .Select(user => new { user.Email, user.FullName })
                    .FirstOrDefault(),
                Owner = candidate.Booking.BookableCourt.Court.Facility.FacilityOwner,
                Administrator = candidate.Booking.BookableCourt.Court.Facility.FacilityOwner.User,
                Desk = candidate.Booking.BookableCourt.Court.Facility.Attendants
                    .Where(attendant => attendant.IsActive
                        && attendant.User.IsActive
                        && attendant.User.EmailVerifiedAt != null)
                    .Select(attendant => new DeskPerson(attendant.User.FullName, attendant.User.Email))
                    .ToList(),
                candidate.Booking.BookableCourt.Court.Facility.ContactPhone,
                candidate.Booking.BookableCourt.Court.Facility.ContactEmail
            })
            .SingleOrDefaultAsync(ct);

        return row is null
            ? null
            : new AboutUpgrade(
                row.FromCourtName,
                row.FacilityName,
                row.SportName,
                new Parties(
                    row.Customer?.FullName ?? "A customer",
                    row.Customer?.Email ?? string.Empty,
                    row.Owner.BusinessName,
                    row.Administrator.FullName,
                    row.Administrator.Email,
                    VenueContact(row.ContactPhone, row.ContactEmail, row.Administrator.Email),
                    row.Desk));
    }

    private sealed record AboutUpgrade(
        string FromCourtName,
        string FacilityName,
        string SportName,
        Parties Parties);

    /// <summary>The dates an upgrade is asking for, one day or a range.</summary>
    private static string Dates(BookingUpgradeRequest upgrade)
    {
        var ordered = upgrade.Slots.OrderBy(slot => slot.Date).ToArray();

        if (ordered.Length == 0)
        {
            return string.Empty;
        }

        var first = ordered[0].Date;
        var last = ordered[^1].Date;

        return first == last
            ? first.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : $"{first.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture)} – " +
                $"{last.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// The hours themselves, not a count.
    ///
    /// A booking's letter says "3 hours" because the dates carry the rest. An
    /// upgrade is often the same day at a different time, so "3 hours" would
    /// leave the reader unable to tell what changed.
    /// </summary>
    private static string Hours(BookingUpgradeRequest upgrade) =>
        string.Join(
            ", ",
            upgrade.Slots
                .OrderBy(slot => slot.Date)
                .ThenBy(slot => slot.StartsAt)
                .Select(slot => slot.StartsAt.ToString("h:mm tt", System.Globalization.CultureInfo.InvariantCulture)));

    private string BookingUrl(Guid bookingId) =>
        Settings.BookingUrl.Length == 0
            ? string.Empty
            : $"{Settings.BookingUrl.TrimEnd('/')}/{bookingId}";

    /// <summary>
    /// Who is on each side of a booking.
    ///
    /// The venue's letter goes to the owner's ACCOUNT email — the person who
    /// signs in and works the queue — rather than the billing address, which is
    /// where invoices go and may be an accountant who has never seen a court.
    /// </summary>
    private async Task<Parties?> PartiesAsync(Guid bookingId, CancellationToken ct)
    {
        var row = await db.Bookings
            .AsNoTracking()
            .Where(candidate => candidate.Id == bookingId)
            .Select(candidate => new
            {
                Customer = db.Users
                    .Where(user => user.Id == candidate.CustomerUserId)
                    .Select(user => new { user.Email, user.FullName })
                    .FirstOrDefault(),
                Owner = candidate.BookableCourt.Court.Facility.FacilityOwner,
                Administrator = candidate.BookableCourt.Court.Facility.FacilityOwner.User,
                Desk = candidate.BookableCourt.Court.Facility.Attendants
                    .Where(attendant => attendant.IsActive
                        && attendant.User.IsActive
                        && attendant.User.EmailVerifiedAt != null)
                    .Select(attendant => new DeskPerson(attendant.User.FullName, attendant.User.Email))
                    .ToList(),
                candidate.BookableCourt.Court.Facility.ContactPhone,
                candidate.BookableCourt.Court.Facility.ContactEmail
            })
            .SingleOrDefaultAsync(ct);

        return row is null
            ? null
            : new Parties(
                row.Customer?.FullName ?? "A customer",
                row.Customer?.Email ?? string.Empty,
                row.Owner.BusinessName,
                row.Administrator.FullName,
                row.Administrator.Email,
                VenueContact(row.ContactPhone, row.ContactEmail, row.Administrator.Email),
                row.Desk);
    }

    /// <summary>
    /// How a customer reaches the venue: the phone and email it published, or
    /// the owner's own when it published neither — a letter that says "speak
    /// to the venue" has to say how.
    /// </summary>
    private static string VenueContact(string? phone, string? email, string ownerEmail)
    {
        var published = new[] { phone, email }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return published.Length > 0 ? string.Join(" · ", published) : ownerEmail;
    }

    private sealed record Parties(
        string CustomerName,
        string CustomerEmail,
        string BusinessName,
        string AdministratorName,
        string AdministratorEmail,
        string VenueContact,
        IReadOnlyList<DeskPerson> Attendants);

    /// <summary>Somebody at a venue's desk, as a letter addresses them.</summary>
    private sealed record DeskPerson(string FullName, string Email);

    /// <summary>
    /// Writes one letter to each person at the venue's desk: the owner, and
    /// every attendant still on it who has set up their account.
    ///
    /// Everybody, rather than the owner alone, because the attendants are the
    /// ones working the queue — a letter only the owner gets is a customer
    /// waiting while the one person who could answer is not told. Someone
    /// invited but not yet signed up — their email not yet verified by
    /// accepting the invitation — is left out: they cannot open the queue the
    /// letter sends them to. One letter each rather than one with many
    /// recipients, so each is addressed by name and nobody's address is shown
    /// to the others.
    /// </summary>
    private async Task SendToDeskAsync(
        Parties parties,
        string templateKey,
        Func<string, Dictionary<string, object>> variables,
        Guid bookingId,
        CancellationToken ct)
    {
        var desk = new[] { new DeskPerson(parties.AdministratorName, parties.AdministratorEmail) }
            .Concat(parties.Attendants)
            .Where(person => !string.IsNullOrWhiteSpace(person.Email))
            .DistinctBy(person => person.Email.Trim().ToUpperInvariant());

        foreach (var person in desk)
        {
            await SendAsync(
                new TransactionalEmailMessage(templateKey, person.Email, person.FullName, variables(person.FullName)),
                bookingId,
                ct);
        }
    }

    /// <summary>
    /// Money as a person reads it. Formatted here rather than in the template
    /// because a template that receives 1645 has no way to know it is pesos.
    /// </summary>
    private static string Money(decimal amount) =>
        amount.ToString("#,##0.00", System.Globalization.CultureInfo.InvariantCulture);

    private async Task SendAsync(TransactionalEmailMessage message, Guid bookingId, CancellationToken ct)
    {
        try
        {
            await emailSender.SendAsync(message, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Could not send {TemplateKey} for booking {BookingId}. The booking stands; the letter did not go.",
                message.TemplateKey,
                bookingId);
        }
    }

    /// <summary>
    /// The days a booking actually covers, read rather than inferred.
    ///
    /// Not taken from the booking handed in: whether its hours are loaded is
    /// the caller's business, and a letter that prints nothing because somebody
    /// forgot an Include is a letter nobody notices is wrong.
    /// </summary>
    private async Task<IReadOnlyList<DateOnly>> DaysAsync(Guid bookingId, CancellationToken ct) =>
        await db.BookingSlots
            .AsNoTracking()
            .Where(slot => slot.BookingId == bookingId)
            .Select(slot => slot.Date)
            .Distinct()
            .OrderBy(date => date)
            .ToListAsync(ct);

    /// <summary>
    /// One day, a run of them, or a run with holes, said the way a person would.
    ///
    /// The span between the first day and the last is NOT the days booked: a
    /// run passes over days it could not have, and "24 – 26 Sep" for a booking
    /// of the 24th and the 26th is how somebody turns up on a day that was
    /// never theirs. So a run with a hole in it lists its days.
    /// </summary>
    private static string Dates(IReadOnlyList<DateOnly> days)
    {
        if (days.Count == 0)
        {
            return string.Empty;
        }

        if (days.Count == 1)
        {
            return days[0].ToString("d MMM yyyy", Culture);
        }

        var unbroken = days[^1].DayNumber - days[0].DayNumber + 1 == days.Count;

        return unbroken
            ? $"{days[0].ToString("d MMM", Culture)} – {days[^1].ToString("d MMM yyyy", Culture)}"
            : string.Join(", ", days.Select((day, index) =>
                day.ToString(index == days.Count - 1 ? "d MMM yyyy" : "d MMM", Culture)));
    }

    private static readonly System.Globalization.CultureInfo Culture =
        System.Globalization.CultureInfo.InvariantCulture;
}
