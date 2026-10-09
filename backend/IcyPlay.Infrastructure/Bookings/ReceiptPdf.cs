using System.Globalization;
using IcyPlay.Application.Bookings;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace IcyPlay.Infrastructure.Bookings;

/// <summary>One line of a receipt: what, how much, and a few words under it if needed.</summary>
public sealed record ReceiptLine(string Label, decimal Amount, string? Note = null);

/// <summary>
/// A receipt as a document to print: whatever it is for — a booking, an open
/// play place — laid out the same way, so a customer holding two of them reads
/// both the same.
/// </summary>
public sealed record ReceiptDocument(
    string Number,
    DateTimeOffset? ConfirmedAt,
    string CustomerName,
    string CustomerEmail,
    string VenueName,
    string? VenueContact,
    /// <summary>What was bought: a court, an open play session.</summary>
    string Title,
    string Subtitle,
    /// <summary>The hours, or the session: what the money was for, one line each.</summary>
    IReadOnlyCollection<ReceiptLine> Items,
    /// <summary>What it costs, before the gateway: rental, discount, platform fee.</summary>
    IReadOnlyCollection<ReceiptLine> Charges,
    string TotalLabel,
    decimal Total,
    /// <summary>The gateway's fees, paid on top. None when paid by receipt.</summary>
    IReadOnlyCollection<ReceiptLine> Fees,
    decimal AmountPaid,
    /// <summary>How and when it was paid, one sentence a payment.</summary>
    IReadOnlyCollection<string> PaymentNotes);

/// <summary>
/// A receipt as a PDF, to travel with the confirmation email.
///
/// The same figures and the same order as the receipt page: a customer
/// comparing the attachment with the page must find them identical.
/// </summary>
public static class ReceiptPdf
{
    private const string Navy = "#071955";
    private const string Blue = "#1767F5";
    private const string Muted = "#64748B";
    private const string Line = "#E2E8F0";

    /// <summary>The Philippines, where every venue on the platform is. No daylight saving.</summary>
    private static readonly TimeSpan Manila = TimeSpan.FromHours(8);

    private static readonly CultureInfo Culture = CultureInfo.InvariantCulture;

    static ReceiptPdf()
    {
        // Free for a business under the revenue threshold, which IcyPlay is.
        // Set before the first document, or QuestPDF refuses to render.
        QuestPDF.Settings.License = LicenseType.Community;
    }

    public static string FileName(BookingReceipt receipt) => FileName(receipt.ReceiptNumber);

    public static string FileName(string receiptNumber) => $"IcyPlay-Receipt-{receiptNumber}.pdf";

    public static byte[] Render(BookingReceipt receipt) => Render(FromBooking(receipt));

    /// <summary>
    /// A booking's receipt as a document: the booking's own payment only. An
    /// upgrade since is named at the foot, with its own receipt number, and
    /// never added in.
    /// </summary>
    public static ReceiptDocument FromBooking(BookingReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);

        var notes = (receipt.Payments.Count > 0
                ? receipt.Payments
                    .Select(payment =>
                        $"Paid {(payment.PaidAt is DateTimeOffset at ? Moment(at) : string.Empty)} with {MethodName(payment.PaymentMethod)}" +
                        (payment.Reference is null ? string.Empty : $" · PayMongo ref {payment.Reference}"))
                : [$"Paid by GCash to {receipt.FacilityName}, and checked by the venue."])
            .Concat((receipt.Upgrades ?? []).Select(upgrade =>
                $"Upgraded to {upgrade.ToCourtName} since — a separate payment with its own receipt, {upgrade.ReceiptNumber}."))
            .ToArray();

        IReadOnlyCollection<ReceiptLine> items = receipt.OriginalSummary is string summary
            ? [new ReceiptLine(summary, receipt.RentalTotal)]
            :
            [
                .. receipt.Slots.Select(slot => new ReceiptLine(
                    $"{slot.Date.ToString("ddd, d MMM yyyy", Culture)} · {Clock(slot.StartsAt)} – {Clock(slot.EndsAt)}",
                    slot.Amount))
            ];

        return new ReceiptDocument(
            receipt.ReceiptNumber,
            receipt.ConfirmedAt,
            receipt.CustomerName,
            receipt.CustomerEmail,
            receipt.FacilityName,
            Contact(receipt.ContactPhone, receipt.ContactEmail),
            receipt.CourtName,
            receipt.SportName,
            items,
            [
                new ReceiptLine("Court rental", receipt.RentalTotal),
                new ReceiptLine("IcyPlay platform fee", receipt.PlatformFeeTotal)
            ],
            "Booking total",
            receipt.BookingTotal,
            [
                .. receipt.Payments
                    .Where(payment => payment.ProcessingFee > 0m)
                    .Select(payment => new ReceiptLine(
                        "Payment processing fee",
                        payment.ProcessingFee,
                        $"{MethodName(payment.PaymentMethod)}, via PayMongo · VAT incl."))
            ],
            receipt.AmountPaid,
            notes);
    }

    public static byte[] Render(ReceiptDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        return Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(40);
                page.DefaultTextStyle(style => style.FontSize(10).FontColor(Navy));

                page.Header().Element(header => Header(header, document));
                page.Content().PaddingTop(20).Element(content => Body(content, document));
                page.Footer().AlignCenter().Text(text =>
                {
                    text.DefaultTextStyle(style => style.FontSize(8).FontColor(Muted));
                    text.Span("This is IcyPlay's record of your payment, not an official receipt. For an official receipt, ask ");
                    text.Span(document.VenueName);
                    text.Span(".");
                });
            });
        }).GeneratePdf();
    }

    public static string? Contact(string? phone, string? email)
    {
        var parts = new[] { phone, email }.Where(part => !string.IsNullOrWhiteSpace(part)).ToArray();

        return parts.Length == 0 ? null : string.Join(" · ", parts);
    }

    /// <summary>A discount is taken off, and says so: "−₱50.00", not "₱-50.00".</summary>
    public static string Peso(decimal amount) =>
        amount < 0m
            ? $"−₱{Math.Abs(amount).ToString("#,##0.00", Culture)}"
            : $"₱{amount.ToString("#,##0.00", Culture)}";

    public static string Moment(DateTimeOffset at) =>
        at.ToOffset(Manila).ToString("d MMM yyyy, h:mm tt", Culture);

    public static string MethodName(string? method) => method switch
    {
        "qrph" => "QR Ph",
        "gcash" => "GCash",
        "paymaya" => "Maya",
        "card" => "Card",
        "grab_pay" => "GrabPay",
        null or "" => "Online",
        _ => method
    };

    private static void Header(IContainer container, ReceiptDocument document)
    {
        container.BorderBottom(1).BorderColor(Line).PaddingBottom(12).Row(row =>
        {
            row.RelativeItem().Column(column =>
            {
                column.Item().Text("ICYPLAY").FontSize(9).Bold().FontColor(Blue).LetterSpacing(0.15f);
                column.Item().Text("Receipt").FontSize(20).Bold();
            });

            row.ConstantItem(200).AlignRight().Column(column =>
            {
                column.Item().AlignRight().Text(document.Number).Bold();

                if (document.ConfirmedAt is DateTimeOffset confirmed)
                {
                    column.Item().AlignRight().Text($"Confirmed {Moment(confirmed)}").FontColor(Muted);
                }
            });
        });
    }

    private static void Body(IContainer container, ReceiptDocument document)
    {
        container.Column(column =>
        {
            column.Spacing(14);

            column.Item().Row(row =>
            {
                row.RelativeItem().Column(party =>
                {
                    party.Item().Text("BILLED TO").FontSize(8).Bold().FontColor(Muted);
                    party.Item().Text(document.CustomerName).Bold();
                    party.Item().Text(document.CustomerEmail).FontColor(Muted);
                });

                row.RelativeItem().Column(party =>
                {
                    party.Item().Text("VENUE").FontSize(8).Bold().FontColor(Muted);
                    party.Item().Text(document.VenueName).Bold();
                    party.Item().Text(document.VenueContact ?? string.Empty).FontColor(Muted);
                });
            });

            column.Item().Column(what =>
            {
                what.Item().Text(document.Title).Bold().FontSize(12);
                what.Item().Text(document.Subtitle).FontColor(Muted);
            });

            column.Item().Border(1).BorderColor(Line).Column(items =>
            {
                foreach (var item in document.Items)
                {
                    items.Item().BorderBottom(1).BorderColor(Line).PaddingVertical(6).PaddingHorizontal(10).Row(row =>
                    {
                        row.RelativeItem().Text(item.Label);
                        row.ConstantItem(90).AlignRight().Text(Peso(item.Amount)).Bold();
                    });
                }
            });

            column.Item().Column(totals =>
            {
                totals.Spacing(5);

                foreach (var charge in document.Charges)
                {
                    totals.Item().Element(item => Amount(item, charge));
                }

                totals.Item().BorderTop(1).BorderColor(Line).PaddingTop(5).Row(row =>
                {
                    row.RelativeItem().Text(document.TotalLabel).Bold();
                    row.ConstantItem(90).AlignRight().Text(Peso(document.Total)).Bold();
                });

                foreach (var fee in document.Fees)
                {
                    totals.Item().Element(item => Amount(item, fee));
                }

                totals.Item().BorderTop(2).BorderColor("#CBD5E1").PaddingTop(8).Row(row =>
                {
                    row.RelativeItem().Text("Amount paid").FontSize(13).Bold();
                    row.ConstantItem(120).AlignRight().Text(Peso(document.AmountPaid)).FontSize(13).Bold();
                });
            });

            column.Item().Background("#F8FAFC").Padding(10).Column(paid =>
            {
                foreach (var note in document.PaymentNotes)
                {
                    paid.Item().Text(note).FontColor(Muted);
                }
            });
        });
    }

    private static void Amount(IContainer container, ReceiptLine line) =>
        container.Row(row =>
        {
            row.RelativeItem().Column(text =>
            {
                text.Item().Text(line.Label);

                if (line.Note is not null)
                {
                    text.Item().Text(line.Note).FontSize(8).FontColor(Muted);
                }
            });
            row.ConstantItem(90).AlignRight().Text(Peso(line.Amount)).Bold();
        });

    private static string Clock(TimeOnly time) => time.ToString("h:mm tt", Culture);
}
