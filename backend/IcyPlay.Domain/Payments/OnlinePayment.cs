using IcyPlay.Domain.Common;

namespace IcyPlay.Domain.Payments;

/// <summary>
/// One attempt to pay for something through the payment gateway.
///
/// Its own record rather than columns on the booking, because a booking, an
/// upgrade and an open play registration are all paid this way, and because
/// one of them can have more than one attempt — a checkout abandoned and a new
/// one opened, or, at worst, two that were both paid.
///
/// The amounts are kept apart from the start. The venue's share and the
/// platform's fee are what a split payment will route to two accounts, and a
/// single total would have to be taken apart again on the day that arrives.
/// </summary>
public sealed class OnlinePayment : Entity
{
    private OnlinePayment()
    {
    }

    public OnlinePayment(
        string purpose,
        Guid subjectId,
        Guid facilityOwnerId,
        Guid customerUserId,
        decimal venueAmount,
        decimal platformFee,
        string provider,
        DateTimeOffset createdAt,
        Guid facilityId = default,
        string? description = null)
    {
        if (venueAmount < 0m || platformFee < 0m)
        {
            throw new ArgumentException("An amount owed cannot be negative.");
        }

        Purpose = purpose;
        SubjectId = subjectId;
        FacilityOwnerId = facilityOwnerId;
        FacilityId = facilityId;
        Description = description;
        CustomerUserId = customerUserId;
        VenueAmount = venueAmount;
        PlatformFee = platformFee;
        Provider = provider;
        Status = OnlinePaymentStatus.Pending;
        CreatedAt = createdAt;
    }

    /// <summary>What it is paying for. See <see cref="PaymentPurpose"/>.</summary>
    public string Purpose { get; private set; } = string.Empty;

    /// <summary>The id of the booking, upgrade or registration being paid for.</summary>
    public Guid SubjectId
    {
        get; private set;
    }

    /// <summary>Whose venue, so a split knows whose account the venue's share goes to.</summary>
    public Guid FacilityOwnerId
    {
        get; private set;
    }

    /// <summary>Which venue: what its desk lists, and what an attendant on another venue never sees.</summary>
    public Guid FacilityId
    {
        get; private set;
    }

    /// <summary>What was paid for, as the customer was shown it at the gateway.</summary>
    public string? Description
    {
        get; private set;
    }

    public Guid CustomerUserId
    {
        get; private set;
    }

    /// <summary>What the venue is owed: the court, or the difference on an upgrade.</summary>
    public decimal VenueAmount
    {
        get; private set;
    }

    /// <summary>The platform fee inside this payment.</summary>
    public decimal PlatformFee
    {
        get; private set;
    }

    /// <summary>What has to arrive after the gateway's own fee: the venue's share and the platform's.</summary>
    public decimal AmountDue => VenueAmount + PlatformFee;

    public string Provider { get; private set; } = string.Empty;

    public string Status { get; private set; } = OnlinePaymentStatus.Pending;

    /// <summary>The gateway's checkout, and the page the customer is sent to.</summary>
    public string? CheckoutSessionId
    {
        get; private set;
    }
    public string? CheckoutUrl
    {
        get; private set;
    }

    /// <summary>
    /// A way of paying started on our own page rather than at a hosted
    /// checkout: the gateway's intent, which its events name.
    /// </summary>
    public string? PaymentIntentId
    {
        get; private set;
    }

    /// <summary>The method the customer picked on our page: qrph, gcash, paymaya.</summary>
    public string? ChosenMethod
    {
        get; private set;
    }

    /// <summary>
    /// What the customer was told they would be charged for <see cref="ChosenMethod"/>,
    /// the gateway's fee included.
    /// </summary>
    public decimal? QuotedTotal
    {
        get; private set;
    }

    /// <summary>The QR Ph code to scan, as a data URL, while it lasts.</summary>
    public string? QrCodeImage
    {
        get; private set;
    }

    /// <summary>The e-wallet's page, where the customer approves the payment.</summary>
    public string? NextActionUrl
    {
        get; private set;
    }

    /// <summary>When the QR code stops working.</summary>
    public DateTimeOffset? ExpiresAt
    {
        get; private set;
    }

    /// <summary>Why an attempt ended with nothing paid, in the gateway's words where it gave any.</summary>
    public string? FailureReason
    {
        get; private set;
    }

    /// <summary>The gateway's payment, once there is one.</summary>
    public string? ProviderPaymentId
    {
        get; private set;
    }

    /// <summary>How the customer paid: gcash, qrph, card, and so on, in the gateway's words.</summary>
    public string? PaymentMethod
    {
        get; private set;
    }

    /// <summary>
    /// What the customer was charged, the gateway's fee included, since that
    /// fee is passed on to them.
    /// </summary>
    public decimal? AmountCharged
    {
        get; private set;
    }

    /// <summary>The gateway's fee.</summary>
    public decimal? ProcessingFee
    {
        get; private set;
    }

    /// <summary>What arrived after the gateway's fee. Should equal <see cref="AmountDue"/>.</summary>
    public decimal? NetAmount
    {
        get; private set;
    }

    /// <summary>When the customer paid, by the gateway's clock, not when we heard about it.</summary>
    public DateTimeOffset? PaidAt
    {
        get; private set;
    }

    /// <summary>Why a paid payment could not settle what it was for. See <see cref="AttentionReason"/>.</summary>
    public string? AttentionReason
    {
        get; private set;
    }

    public bool IsPending => Status == OnlinePaymentStatus.Pending;

    /// <summary>
    /// Whether money arriving for this would be news: still waiting, or ended
    /// as failed. A failed attempt that is paid after all — a bank's late
    /// word — is money all the same, and is never ignored.
    /// </summary>
    public bool CanStillBePaid => Status is OnlinePaymentStatus.Pending or OnlinePaymentStatus.Failed;

    /// <summary>
    /// Records the gateway's hosted checkout. <paramref name="method"/> is set
    /// when the customer picked it on our page and the checkout offers only
    /// that — a card — so it is never handed back as the all-methods page.
    /// </summary>
    public void OpenCheckout(string checkoutSessionId, string checkoutUrl, DateTimeOffset now, string? method = null)
    {
        CheckoutSessionId = checkoutSessionId;
        CheckoutUrl = checkoutUrl;
        ChosenMethod = method;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records a way of paying started on our own page: one method, one
    /// amount, and what the customer does next — scan a code or approve at
    /// their e-wallet.
    /// </summary>
    public void OpenIntent(
        string paymentIntentId,
        string method,
        decimal quotedTotal,
        string? qrCodeImage,
        string? nextActionUrl,
        DateTimeOffset? expiresAt,
        DateTimeOffset now)
    {
        if (CheckoutSessionId is not null || PaymentIntentId is not null)
        {
            throw new InvalidOperationException("This payment has already been started.");
        }

        if (quotedTotal < AmountDue)
        {
            throw new ArgumentException("The total charged cannot be less than what is owed.", nameof(quotedTotal));
        }

        PaymentIntentId = paymentIntentId;
        ChosenMethod = method;
        QuotedTotal = quotedTotal;
        QrCodeImage = qrCodeImage;
        NextActionUrl = nextActionUrl;
        ExpiresAt = expiresAt;
        UpdatedAt = now;
    }

    /// <summary>
    /// The attempt ended with nothing paid. Only a pending one can: a paid one
    /// stays paid whatever a later event says.
    /// </summary>
    public void EndUnpaid(string reason, DateTimeOffset now)
    {
        if (!IsPending)
        {
            return;
        }

        Status = OnlinePaymentStatus.Failed;
        FailureReason = reason;
        // A code nobody can pay any more is not shown again.
        QrCodeImage = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Records what the gateway says was paid. Says nothing yet about whether
    /// it settled anything; <see cref="Settle"/> or <see cref="FlagForAttention"/>
    /// follows.
    /// </summary>
    public void RecordPaid(
        string providerPaymentId,
        string? paymentMethod,
        decimal amountCharged,
        decimal processingFee,
        decimal netAmount,
        DateTimeOffset paidAt,
        DateTimeOffset now)
    {
        ProviderPaymentId = providerPaymentId;
        PaymentMethod = paymentMethod;
        AmountCharged = amountCharged;
        ProcessingFee = processingFee;
        NetAmount = netAmount;
        PaidAt = paidAt;
        FailureReason = null;
        UpdatedAt = now;
    }

    /// <summary>
    /// Whether what arrived covers what was owed. Short by even a centavo is
    /// not settled: the venue's share would come out of somebody's pocket.
    /// </summary>
    public bool CoversWhatIsDue => NetAmount is decimal net && net >= AmountDue;

    public void Settle(DateTimeOffset now)
    {
        Status = OnlinePaymentStatus.Paid;
        AttentionReason = null;
        UpdatedAt = now;
    }

    public void FlagForAttention(string reason, DateTimeOffset now)
    {
        Status = OnlinePaymentStatus.NeedsAttention;
        AttentionReason = reason;
        UpdatedAt = now;
    }
}

/// <summary>Why a paid payment is waiting on a person.</summary>
public static class AttentionReason
{
    /// <summary>The hold ran out before the customer paid, so the court may already be someone else's.</summary>
    public const string PaidAfterHoldLapsed = "PaidAfterHoldLapsed";

    /// <summary>What it paid for is no longer waiting to be paid: already paid, cancelled or refused.</summary>
    public const string NotAwaitingPayment = "NotAwaitingPayment";

    /// <summary>Less arrived than was owed.</summary>
    public const string AmountShort = "AmountShort";

    /// <summary>What it paid for cannot be found.</summary>
    public const string SubjectNotFound = "SubjectNotFound";

    /// <summary>An upgrade paid in time, but its hours were taken, or the booking had moved on.</summary>
    public const string UpgradeHoursTaken = "UpgradeHoursTaken";
}
