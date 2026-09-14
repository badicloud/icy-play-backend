namespace IcyPlay.Domain.Email;

public static class EmailTemplateKey
{
    public const string AccountVerification = "account-verification";
    public const string PasswordReset = "password-reset";
    public const string FacilityOwnerInvitation = "facility-owner-invitation";

    /// <summary>
    /// Hands a venue's desk to somebody the owner named. Its own letter rather
    /// than the owner's: what the reader is being asked to do is different, and
    /// an attendant told they now own a business would reasonably be confused.
    /// </summary>
    public const string FacilityAttendantInvitation = "facility-attendant-invitation";

    /// <summary>
    /// Tells the customer their receipt is in and the court is held while the
    /// venue checks it. Deliberately not a confirmation: nothing is confirmed
    /// until a person has looked at the payment.
    /// </summary>
    public const string BookingPaymentReceived = "booking-payment-received";

    /// <summary>
    /// Thanks the customer and confirms their court. Sent when the venue has
    /// checked the payment — not when the customer says they have paid, because
    /// until somebody looks at the receipt there is nothing to confirm.
    /// </summary>
    public const string BookingConfirmed = "booking-confirmed";

    /// <summary>Tells the venue somebody has paid and is waiting to be confirmed.</summary>
    public const string BookingPaymentSubmitted = "booking-payment-submitted";
}
