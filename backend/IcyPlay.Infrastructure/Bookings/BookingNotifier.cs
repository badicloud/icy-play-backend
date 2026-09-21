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
                        ["booking_dates"] = Dates(booking),
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

        await SendAsync(
            new TransactionalEmailMessage(
                EmailTemplateKey.BookingPaymentSubmitted,
                parties.AdministratorEmail,
                parties.AdministratorName,
                new Dictionary<string, object>
                {
                    ["recipient_name"] = parties.AdministratorName,
                    ["business_name"] = parties.BusinessName,
                    ["customer_name"] = parties.CustomerName,
                    ["customer_email"] = parties.CustomerEmail,
                    ["court_name"] = booking.CourtName,
                    ["facility_name"] = booking.FacilityName,
                    ["sport_name"] = booking.SportName,
                    ["booking_dates"] = Dates(booking),
                    ["booked_hours"] = booking.BookedHours,
                    ["total_amount"] = Money(booking.Total),
                    ["receipt_url"] = booking.ReceiptUrl ?? string.Empty,
                    ["booking_url"] = BookingUrl(booking.Id),
                    ["support_email"] = Settings.SupportEmail,
                    ["current_year"] = timeProvider.GetUtcNow().Year
                }),
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
                    ["booking_dates"] = Dates(booking),
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

        await SendAsync(
            new TransactionalEmailMessage(
                EmailTemplateKey.BookingUpgradeSubmitted,
                about.Parties.AdministratorEmail,
                about.Parties.AdministratorName,
                new Dictionary<string, object>
                {
                    ["recipient_name"] = about.Parties.AdministratorName,
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
                }),
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

    public async Task BookingMovedAsync(
        Booking booking,
        BookingMoveNotice notice,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(booking);
        ArgumentNullException.ThrowIfNull(notice);

        var parties = await PartiesAsync(booking.Id, ct);

        if (parties is null)
        {
            return;
        }

        // The venue only. The customer just did this and is looking at the
        // screen that did it; a letter telling them what they have this second
        // done is noise.
        await SendAsync(
            new TransactionalEmailMessage(
                EmailTemplateKey.BookingMoved,
                parties.AdministratorEmail,
                parties.AdministratorName,
                new Dictionary<string, object>
                {
                    ["recipient_name"] = parties.AdministratorName,
                    ["business_name"] = parties.BusinessName,
                    ["customer_name"] = parties.CustomerName,
                    ["customer_email"] = parties.CustomerEmail,
                    ["from_court_name"] = notice.FromCourtName,
                    ["to_court_name"] = booking.CourtName,
                    ["sport_name"] = booking.SportName,
                    ["was_when"] = notice.WasWhen,
                    ["now_when"] = notice.NowWhen,
                    ["bookings_url"] = Settings.CourtBookingsUrl,
                    ["support_email"] = Settings.SupportEmail,
                    ["current_year"] = timeProvider.GetUtcNow().Year
                }),
            booking.Id,
            ct);
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
                Administrator = candidate.Booking.BookableCourt.Court.Facility.FacilityOwner.User
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
                    row.Administrator.Email));
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
                Administrator = candidate.BookableCourt.Court.Facility.FacilityOwner.User
            })
            .SingleOrDefaultAsync(ct);

        return row is null
            ? null
            : new Parties(
                row.Customer?.FullName ?? "A customer",
                row.Customer?.Email ?? string.Empty,
                row.Owner.BusinessName,
                row.Administrator.FullName,
                row.Administrator.Email);
    }

    private sealed record Parties(
        string CustomerName,
        string CustomerEmail,
        string BusinessName,
        string AdministratorName,
        string AdministratorEmail);

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

    /// <summary>One day, or a range, said the way a person would say it.</summary>
    private static string Dates(Booking booking) =>
        booking.StartDate == booking.EndDate
            ? booking.StartDate.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)
            : $"{booking.StartDate.ToString("d MMM", System.Globalization.CultureInfo.InvariantCulture)} – " +
                $"{booking.EndDate.ToString("d MMM yyyy", System.Globalization.CultureInfo.InvariantCulture)}";
}
