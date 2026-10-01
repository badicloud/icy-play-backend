using System.Globalization;
using IcyPlay.Application.Email;
using IcyPlay.Application.OpenPlays;
using IcyPlay.Domain.Email;
using IcyPlay.Infrastructure.Email;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>
/// The letters an open play registration sends, the way a booking's are sent:
/// when a receipt arrives, one to the player saying the venue is checking it
/// and one to everybody on the venue's desk asking them to; then one to the
/// player when the desk confirms or turns it down.
///
/// Nothing here throws. The registration is already saved, and the desk sees
/// it in its queue whether or not a letter went; failures are logged instead.
/// </summary>
public sealed class OpenPlayNotifier(
    AppDbContext db,
    ITransactionalEmailSender emailSender,
    IOptions<BookingNotificationOptions> options,
    TimeProvider timeProvider,
    ILogger<OpenPlayNotifier> logger) : IOpenPlayNotifier
{
    private BookingNotificationOptions Settings => options.Value;

    public async Task PaymentSubmittedAsync(Guid registrationId, CancellationToken ct)
    {
        var about = await AboutAsync(registrationId, ct);

        if (about is null)
        {
            return;
        }

        if (about.PlayerEmail.Length > 0)
        {
            await SendAsync(
                new TransactionalEmailMessage(
                    EmailTemplateKey.OpenPlayPaymentReceived,
                    about.PlayerEmail,
                    about.PlayerName,
                    Common(about, about.PlayerName)),
                registrationId,
                ct);
        }

        // One letter to each person on the desk: the owner, and every attendant
        // still on it who has set up their account, as a booking's are sent.
        var desk = new[] { new Person(about.OwnerName, about.OwnerEmail) }
            .Concat(about.Attendants)
            .Where(person => !string.IsNullOrWhiteSpace(person.Email))
            .DistinctBy(person => person.Email.Trim().ToUpperInvariant());

        foreach (var person in desk)
        {
            var variables = Common(about, person.Name);
            variables["business_name"] = about.BusinessName;
            variables["player_name"] = about.PlayerName;
            variables["player_email"] = about.PlayerEmail;
            variables["receipt_url"] = about.ReceiptUrl ?? string.Empty;
            // The picture to look at, and the queue to act in.
            variables["requests_url"] = Settings.OpenPlayRequestsUrl;

            await SendAsync(
                new TransactionalEmailMessage(
                    EmailTemplateKey.OpenPlayPaymentSubmitted,
                    person.Email,
                    person.Name,
                    variables),
                registrationId,
                ct);
        }
    }

    public async Task ConfirmedAsync(Guid registrationId, CancellationToken ct)
    {
        var about = await AboutAsync(registrationId, ct);

        if (about is null || about.PlayerEmail.Length == 0)
        {
            return;
        }

        await SendAsync(
            new TransactionalEmailMessage(
                EmailTemplateKey.OpenPlayConfirmed,
                about.PlayerEmail,
                about.PlayerName,
                Common(about, about.PlayerName)),
            registrationId,
            ct);
    }

    public async Task DeclinedAsync(Guid registrationId, CancellationToken ct)
    {
        var about = await AboutAsync(registrationId, ct);

        if (about is null || about.PlayerEmail.Length == 0)
        {
            return;
        }

        var variables = Common(about, about.PlayerName);
        // The same sentence the registration page shows: the reason the desk
        // picked, and its note.
        variables["decline_reason"] = about.DeclineReason ?? "The venue could not accept the payment.";

        await SendAsync(
            new TransactionalEmailMessage(
                EmailTemplateKey.OpenPlayDeclined,
                about.PlayerEmail,
                about.PlayerName,
                variables),
            registrationId,
            ct);
    }

    /// <summary>What every one of these letters says about the registration.</summary>
    private Dictionary<string, object> Common(About about, string recipientName) => new()
    {
        ["recipient_name"] = recipientName,
        ["open_play_title"] = about.Title,
        ["facility_name"] = about.FacilityName,
        ["court_name"] = about.CourtName,
        ["sport_name"] = about.SportName,
        ["session_date"] = about.Date.ToString("dddd, d MMMM yyyy", CultureInfo.InvariantCulture),
        ["session_hours"] = $"{Clock(about.StartsAt)} – {Clock(about.EndsAt)}",
        ["registration_fee"] = Money(about.RegistrationFee),
        ["discount"] = Money(about.Discount),
        ["platform_fee"] = Money(about.PlatformFee),
        ["total_amount"] = Money(about.Total),
        ["venue_contact"] = about.VenueContact,
        ["registration_url"] = RegistrationUrl(about.RegistrationId),
        ["support_email"] = Settings.SupportEmail,
        ["current_year"] = timeProvider.GetUtcNow().Year
    };

    private async Task<About?> AboutAsync(Guid registrationId, CancellationToken ct)
    {
        var row = await db.OpenPlayRegistrations
            .AsNoTracking()
            .Where(registration => registration.Id == registrationId)
            .Select(registration => new
            {
                Registration = registration,
                registration.Session.Date,
                OpenPlay = registration.Session.OpenPlay,
                FacilityName = registration.Session.OpenPlay.Facility.Name,
                CourtName = registration.Session.OpenPlay.BookableCourt.Court.Name,
                SportName = registration.Session.OpenPlay.BookableCourt.CourtSport.Sport.Name,
                registration.Session.OpenPlay.Facility.ContactPhone,
                registration.Session.OpenPlay.Facility.ContactEmail,
                registration.Session.OpenPlay.Facility.FacilityOwner.BusinessName,
                OwnerName = registration.Session.OpenPlay.Facility.FacilityOwner.User.FullName,
                OwnerEmail = registration.Session.OpenPlay.Facility.FacilityOwner.User.Email,
                Attendants = registration.Session.OpenPlay.Facility.Attendants
                    .Where(attendant => attendant.IsActive
                        && attendant.User.IsActive
                        && attendant.User.EmailVerifiedAt != null)
                    .Select(attendant => new Person(attendant.User.FullName, attendant.User.Email))
                    .ToList(),
                Player = db.Users
                    .Where(user => user.Id == registration.CustomerUserId)
                    .Select(user => new { user.FullName, user.Email })
                    .FirstOrDefault()
            })
            .SingleOrDefaultAsync(ct);

        if (row is null)
        {
            return null;
        }

        var contact = new[] { row.ContactPhone, row.ContactEmail }
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return new About(
            row.Registration.Id,
            row.OpenPlay.Title,
            row.FacilityName,
            row.CourtName,
            row.SportName,
            row.Date,
            row.OpenPlay.StartsAt,
            row.OpenPlay.EndsAt,
            row.Registration.RegistrationFee,
            row.Registration.Discount,
            row.Registration.PlatformFee,
            row.Registration.Total,
            row.Registration.ReceiptUrl,
            row.Registration.CancellationReason,
            contact.Length > 0 ? string.Join(" · ", contact) : row.OwnerEmail,
            row.Player?.FullName ?? "A player",
            row.Player?.Email ?? string.Empty,
            row.BusinessName,
            row.OwnerName,
            row.OwnerEmail,
            row.Attendants);
    }

    private string RegistrationUrl(Guid registrationId) =>
        Settings.OpenPlayRegistrationUrl.Length == 0
            ? string.Empty
            : $"{Settings.OpenPlayRegistrationUrl.TrimEnd('/')}/{registrationId}";

    /// <summary>Money as a person reads it: a template that receives 1645 cannot know it is pesos.</summary>
    private static string Money(decimal amount) => amount.ToString("#,##0.00", CultureInfo.InvariantCulture);

    private static string Clock(TimeOnly time) => time.ToString("h:mm tt", CultureInfo.InvariantCulture);

    private async Task SendAsync(TransactionalEmailMessage message, Guid registrationId, CancellationToken ct)
    {
        try
        {
            await emailSender.SendAsync(message, ct);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogError(
                exception,
                "Could not send {TemplateKey} for open play registration {RegistrationId}. The registration stands; the letter did not go.",
                message.TemplateKey,
                registrationId);
        }
    }

    private sealed record Person(string Name, string Email);

    private sealed record About(
        Guid RegistrationId,
        string Title,
        string FacilityName,
        string CourtName,
        string SportName,
        DateOnly Date,
        TimeOnly StartsAt,
        TimeOnly EndsAt,
        decimal RegistrationFee,
        decimal Discount,
        decimal PlatformFee,
        decimal Total,
        string? ReceiptUrl,
        string? DeclineReason,
        string VenueContact,
        string PlayerName,
        string PlayerEmail,
        string BusinessName,
        string OwnerName,
        string OwnerEmail,
        IReadOnlyList<Person> Attendants);
}
