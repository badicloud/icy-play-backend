using System.Text;
using FluentAssertions.Execution;
using IcyPlay.Application.Bookings;
using IcyPlay.Infrastructure.Bookings;
using IcyPlay.UnitTests.TestData;

namespace IcyPlay.UnitTests;

public sealed class ReceiptPdfTests
{
    [Fact]
    public void Should_Render_A_Pdf_When_Receipt_Was_Paid_Online()
    {
        // Arrange
        var receipt = Receipt("Direct", [new ReceiptPayment("Booking", "gcash", 646.15m, 16.15m, TestTimes.UtcNow, "pay_1")]);

        // Act
        var pdf = ReceiptPdf.Render(receipt);

        // Assert
        using (new AssertionScope())
        {
            pdf.Should().NotBeEmpty();
            Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
        }
    }

    [Fact]
    public void Should_Render_A_Pdf_When_Receipt_Was_Paid_By_Receipt()
    {
        // Arrange
        var receipt = Receipt("Manual", []);

        // Act
        var pdf = ReceiptPdf.Render(receipt);

        // Assert
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Should_Render_A_Pdf_When_Receipt_Has_A_Discount()
    {
        // Arrange
        var document = new ReceiptDocument(
            "OP-1A2B3C4D5E",
            TestTimes.UtcNow,
            "Juan Dela Cruz",
            "juan@example.com",
            "Demo Sports Center",
            null,
            "Saturday Smash",
            "Open play · Pickleball · Court 1",
            [new ReceiptLine("Sat, 10 Oct 2026 · 6:00 PM – 9:00 PM", 130m)],
            [
                new ReceiptLine("Open play fee", 150m),
                new ReceiptLine("Early bird discount", -20m),
                new ReceiptLine("IcyPlay platform fee", 15m)
            ],
            "Registration total",
            145m,
            [new ReceiptLine("Payment processing fee", 1.95m, "QR Ph, via PayMongo · VAT incl.")],
            146.95m,
            ["Paid with QR Ph · PayMongo ref pay_1"]);

        // Act
        var pdf = ReceiptPdf.Render(document);

        // Assert
        Encoding.ASCII.GetString(pdf, 0, 5).Should().Be("%PDF-");
    }

    [Fact]
    public void Should_Show_A_Discount_As_Taken_Off_When_Amount_Is_Negative()
    {
        // Arrange
        // Act
        var shown = ReceiptPdf.Peso(-20m);

        // Assert
        shown.Should().Be("−₱20.00");
    }

    [Fact]
    public void Should_Show_Only_The_Booking_Payment_When_Booking_Was_Upgraded_Since()
    {
        // Arrange: upgraded since, so the hours now are not what this paid for.
        var receipt = Receipt("Direct", [new ReceiptPayment("Booking", "gcash", 425.64m, 10.64m, TestTimes.UtcNow, "pay_1")]) with
        {
            Slots = [],
            RentalTotal = 400m,
            BookingTotal = 415m,
            ProcessingFeeTotal = 10.64m,
            AmountPaid = 425.64m,
            OriginalSummary = "1 hour on Court 1 · Pickleball 1, as first booked",
            Upgrades = [new UpgradeReceiptSummary("UP-1A2B3C4D5E", "Court 1 · Pickleball 1", "Court 1 · Badminton 1", 205.13m, TestTimes.UtcNow)]
        };

        // Act
        var document = ReceiptPdf.FromBooking(receipt);

        // Assert
        using (new AssertionScope())
        {
            document.Items.Should().ContainSingle().Which.Label.Should().Contain("as first booked");
            document.Fees.Should().ContainSingle().Which.Amount.Should().Be(10.64m);
            document.AmountPaid.Should().Be(425.64m);
            document.PaymentNotes.Should().Contain(note => note.Contains("UP-1A2B3C4D5E"));
        }
    }

    [Fact]
    public void Should_Name_The_File_After_The_Receipt_Number()
    {
        // Arrange
        var receipt = Receipt("Manual", []);

        // Act
        var name = ReceiptPdf.FileName(receipt);

        // Assert
        name.Should().Be("IcyPlay-Receipt-BK-343897110B.pdf");
    }

    private static BookingReceipt Receipt(string channel, IReadOnlyCollection<ReceiptPayment> payments) =>
        new(
            "BK-343897110B",
            TestIds.For("booking"),
            "Juan Dela Cruz",
            "juan@example.com",
            "Court 1 · Pickleball 2",
            "Demo Sports Center",
            "Pickleball",
            "+639171234567",
            "hello@example.com",
            [
                new BookedSlot(new DateOnly(2026, 10, 9), new TimeOnly(6, 0), new TimeOnly(7, 0), "Standard", 300m, 15m),
                new BookedSlot(new DateOnly(2026, 10, 9), new TimeOnly(7, 0), new TimeOnly(8, 0), "Standard", 300m, 15m)
            ],
            600m,
            30m,
            630m,
            channel,
            payments,
            payments.Sum(payment => payment.ProcessingFee),
            payments.Count > 0 ? payments.Sum(payment => payment.AmountCharged) : 630m,
            TestTimes.UtcNow);
}
