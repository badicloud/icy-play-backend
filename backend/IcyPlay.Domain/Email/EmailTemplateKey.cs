namespace IcyPlay.Domain.Email;

public static class EmailTemplateKey
{
    public const string AccountVerification = "account-verification";
    public const string PasswordReset = "password-reset";

    /// <summary>
    /// Tells somebody their account was signed in to from a device we have not
    /// seen before, and offers the one remedy that works: change the password,
    /// which signs every device out at once.
    ///
    /// Only on a new device. A letter on every sign-in becomes a letter nobody
    /// reads, which costs the one that mattered.
    /// </summary>
    public const string AccountNewSignIn = "account-new-sign-in";
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

    /// <summary>
    /// Tells the customer their upgrade payment is in and the venue is looking
    /// at it. Its own letter rather than the booking's, and careful in the same
    /// way: the booking has NOT moved yet, and somebody who reads this as a
    /// confirmation walks onto a court that is still somebody else's.
    /// </summary>
    public const string BookingUpgradeReceived = "booking-upgrade-received";

    /// <summary>
    /// Tells the venue somebody has paid to move onto a dearer court.
    ///
    /// Separate from the booking's own letter because the job is different: a
    /// booking asks whether the payment is real, and this asks that AND
    /// whether the court being asked for is free.
    /// </summary>
    public const string BookingUpgradeSubmitted = "booking-upgrade-submitted";

    /// <summary>
    /// Tells the customer the venue said yes and their booking has moved. The
    /// one letter in this flow that is a confirmation.
    /// </summary>
    public const string BookingUpgradeApproved = "booking-upgrade-approved";

    /// <summary>
    /// Tells the venue a booking has moved itself.
    ///
    /// A move onto hours costing the same or less is free and happens at once,
    /// so nobody is asked and nobody was told — the floor changed under the
    /// desk. This is the only thing that says so, and the hours the booking
    /// vacated matter more than the ones it took: those are back on sale, and
    /// the desk is the one turning people away from them.
    /// </summary>
    public const string BookingMoved = "booking-moved";

    /// <summary>
    /// Tells the venue a customer is asking to move, free, and waiting on them.
    ///
    /// The move has not happened. The hours asked for are held while the desk
    /// decides; the booking stays where it is until somebody approves it.
    /// </summary>
    public const string BookingMoveRequested = "booking-move-requested";

    /// <summary>
    /// Tells the customer the venue agreed to their free move, and where they
    /// are playing now. The confirmation, and the only letter in this flow
    /// that can safely say which court to walk to.
    /// </summary>
    public const string BookingMoveApproved = "booking-move-approved";

    /// <summary>
    /// Tells the customer the venue said no to a move, and why. Says first that
    /// the booking is still where it was, so nobody turns up at the court they
    /// asked for. Sent for a declined upgrade too, with a line about the money.
    /// </summary>
    public const string BookingMoveDeclined = "booking-move-declined";

    /// <summary>
    /// Tells the customer the venue checked their payment and turned it down,
    /// why, and what to do. Says first that the court is NOT held: somebody who
    /// turns up on a declined booking finds the court sold to someone else.
    /// </summary>
    public const string BookingDeclined = "booking-declined";
}
