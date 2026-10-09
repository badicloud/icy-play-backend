using System.Globalization;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Payments;
using IcyPlay.Infrastructure.Bookings;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>A registration's reference, as the player and the payment gateway both see it.</summary>
internal static class OpenPlayReference
{
    public static string For(Guid registrationId) =>
        $"OP-{registrationId.ToString("N")[..10].ToUpperInvariant()}";
}

/// <summary>
/// A player's receipt for their place at one open play session: the fee, any
/// early-bird discount, the platform fee, and the gateway's fee when paid
/// online. The same layout as a booking's.
/// </summary>
internal static class OpenPlayReceipts
{
    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    public static async Task<ReceiptDocument?> ReadAsync(AppDbContext db, Guid registrationId, CancellationToken ct)
    {
        var row = await db.OpenPlayRegistrations
            .AsNoTracking()
            .Where(registration => registration.Id == registrationId && registration.Status == BookingStatus.Confirmed)
            .Select(registration => new
            {
                Registration = registration,
                registration.Session.Date,
                registration.Session.OpenPlay.Title,
                registration.Session.OpenPlay.StartsAt,
                registration.Session.OpenPlay.EndsAt,
                FacilityName = registration.Session.OpenPlay.Facility.Name,
                CourtName = registration.Session.OpenPlay.BookableCourt.Court.Name,
                SportName = registration.Session.OpenPlay.BookableCourt.CourtSport.Sport.Name,
                registration.Session.OpenPlay.Facility.ContactPhone,
                registration.Session.OpenPlay.Facility.ContactEmail,
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

        var registration = row.Registration;

        var payment = await db.OnlinePayments
            .AsNoTracking()
            .Where(candidate => candidate.Purpose == PaymentPurpose.OpenPlayRegistration
                && candidate.SubjectId == registrationId
                && candidate.Status == OnlinePaymentStatus.Paid)
            .OrderByDescending(candidate => candidate.PaidAt)
            .FirstOrDefaultAsync(ct);

        var charges = new List<ReceiptLine> { new("Open play fee", registration.RegistrationFee) };

        if (registration.Discount > 0m)
        {
            charges.Add(new ReceiptLine("Early bird discount", -registration.Discount));
        }

        charges.Add(new ReceiptLine("IcyPlay platform fee", registration.PlatformFee));

        var fees = payment?.ProcessingFee is decimal fee && fee > 0m
            ? new[] { new ReceiptLine("Payment processing fee", fee, $"{ReceiptPdf.MethodName(payment.PaymentMethod)}, via PayMongo · VAT incl.") }
            : [];

        var notes = payment is not null
            ? new[]
            {
                $"Paid {(payment.PaidAt is DateTimeOffset at ? ReceiptPdf.Moment(at) : string.Empty)} with " +
                $"{ReceiptPdf.MethodName(payment.PaymentMethod)}" +
                (payment.ProviderPaymentId is null ? string.Empty : $" · PayMongo ref {payment.ProviderPaymentId}")
            }
            : [$"Paid by GCash to {row.FacilityName}, and checked by the venue."];

        return new ReceiptDocument(
            OpenPlayReference.For(registration.Id),
            registration.ConfirmedAt,
            row.Player?.FullName ?? string.Empty,
            row.Player?.Email ?? string.Empty,
            row.FacilityName,
            ReceiptPdf.Contact(row.ContactPhone, row.ContactEmail),
            row.Title,
            $"Open play · {row.SportName} · {row.CourtName}",
            [
                new ReceiptLine(
                    $"{row.Date.ToString("ddd, d MMM yyyy", Culture)} · " +
                    $"{row.StartsAt.ToString("h:mm tt", Culture)} – {row.EndsAt.ToString("h:mm tt", Culture)}",
                    registration.RegistrationFee - registration.Discount)
            ],
            charges,
            "Registration total",
            registration.Total,
            fees,
            payment?.AmountCharged ?? registration.Total,
            notes);
    }
}
