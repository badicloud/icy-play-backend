namespace IcyPlay.Domain.Bookings;

/// <summary>
/// Why the desk turned a payment down, from a short fixed list.
///
/// A list for the same reason <see cref="MoveReason"/> is one: the point of
/// asking is to count the answers. A venue that keeps writing "no money came
/// in" and "not received" and "wala nisulod" has one problem, and a report can
/// only say so if it was picked rather than typed. <see cref="Other"/> is the
/// way out, and asks for a few words.
/// </summary>
public static class RejectReason
{
    public const string PaymentNotReceived = "PaymentNotReceived";
    public const string WrongAmount = "WrongAmount";
    public const string ReceiptUnclear = "ReceiptUnclear";
    public const string CourtNotAvailable = "CourtNotAvailable";
    public const string Other = "Other";

    public static readonly IReadOnlyCollection<string> All =
        [PaymentNotReceived, WrongAmount, ReceiptUnclear, CourtNotAvailable, Other];

    /// <summary>The same limit a move's note has: a reason, not a letter.</summary>
    public const int NoteLimit = 200;

    public static bool IsSupported(string? value) =>
        value is not null && All.Contains(value, StringComparer.Ordinal);

    /// <summary>How the reason reads to the customer and in the booking's history.</summary>
    public static string Label(string reason) => reason switch
    {
        PaymentNotReceived => "Payment not received",
        WrongAmount => "Wrong amount",
        ReceiptUnclear => "Receipt unclear",
        CourtNotAvailable => "Court not available",
        Other => "Other",
        _ => reason
    };

    /// <summary>
    /// The one sentence a person reads: the reason, and the note after it.
    /// What <see cref="Booking.CancellationReason"/> holds on a refusal, so
    /// every screen that already shows it keeps working unchanged.
    /// </summary>
    public static string Sentence(string reason, string? note) =>
        string.IsNullOrWhiteSpace(note)
            ? $"{Label(reason)}."
            : $"{Label(reason)} — {note.Trim()}";
}
