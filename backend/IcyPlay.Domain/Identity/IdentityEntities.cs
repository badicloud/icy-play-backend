using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.Common;
using IcyPlay.Domain.Facilities;

namespace IcyPlay.Domain.Identity;

public sealed class UserRole : Entity
{
    private UserRole()
    {
    }
    public UserRole(Guid userId, string role)
    {
        UserId = userId;
        Role = role;
    }
    public Guid UserId
    {
        get; private set;
    }
    public User User { get; private set; } = null!;
    public string Role { get; private set; } = string.Empty;
}
public sealed class Customer : Entity
{
    private Customer()
    {
    }
    public Customer(Guid userId) => UserId = userId;
    public Guid UserId
    {
        get; private set;
    }
    public User User { get; private set; } = null!;
}
public sealed class FacilityOwner : Entity
{
    private FacilityOwner()
    {
    }
    public FacilityOwner(Guid userId, string businessName, string billingEmail, string? billingPhone)
    {
        UserId = userId;
        BusinessName = businessName.Trim();
        BillingEmail = billingEmail.Trim().ToLowerInvariant();
        BillingPhone = string.IsNullOrWhiteSpace(billingPhone) ? null : billingPhone.Trim();
    }
    public Guid UserId
    {
        get; private set;
    }
    public User User { get; private set; } = null!;
    public string BusinessName { get; private set; } = string.Empty;
    public string BillingEmail { get; private set; } = string.Empty;
    public string? BillingPhone
    {
        get; private set;
    }
    public bool IsActive { get; private set; } = true;
    public string? BusinessRegistrationNumber
    {
        get; private set;
    }

    /// <summary>
    /// Where a customer sends the money. The platform never touches it: the
    /// customer pays the venue directly, and the platform bills the venue its
    /// fee afterwards.
    ///
    /// Either is enough to pay by — a number to type, or a code to scan — so a
    /// venue that has only one is not blocked from taking bookings.
    /// </summary>
    public string? GcashNumber
    {
        get; private set;
    }

    /// <summary>
    /// Whose account it is. Sent to the customer with the number, because GCash
    /// shows the recipient's name before the money moves and a customer who
    /// cannot check it against anything is being asked to trust a screen.
    /// </summary>
    public string? GcashAccountName
    {
        get; private set;
    }

    /// <summary>The QR code image, in Cloudinary. Stored as its secure URL.</summary>
    public string? GcashQrCodeUrl
    {
        get; private set;
    }

    /// <summary>
    /// How long a booking holds its court while waiting to be paid for.
    ///
    /// Per owner, because it is their court going unsold: a busy city venue
    /// wants the hour back in fifteen minutes, a quiet one can afford an hour.
    /// </summary>
    public int PartialBookingExpiryMinutes { get; private set; } = PaymentHold.DefaultMinutes;

    /// <summary>
    /// How many times a customer may move one of this venue's bookings.
    ///
    /// Per venue for the same reason the hold is: a quiet hall can be generous
    /// about it, a hall turning people away every Saturday cannot. Moves the
    /// venue itself asked for are not counted against it.
    /// </summary>
    public int MoveLimit { get; private set; } = BookingMove.DefaultLimit;

    /// <summary>
    /// When the seeder raised this venue, and null for every real one.
    ///
    /// A stored fact rather than a guess at the name. Removing demonstration
    /// data is destructive and done in bulk, and "everything called Demo
    /// something" is a rule that one day matches a venue somebody is actually
    /// trading from.
    /// </summary>
    public DateTimeOffset? SeededAt
    {
        get; private set;
    }

    /// <summary>Whether this venue exists only to be demonstrated against.</summary>
    public bool IsSeeded => SeededAt is not null;

    public void MarkSeeded(DateTimeOffset now)
    {
        SeededAt = now;
        UpdatedAt = now;
    }

    public void SetPaymentHold(int partialBookingExpiryMinutes, DateTimeOffset now)
    {
        PartialBookingExpiryMinutes = PaymentHold.Clamp(partialBookingExpiryMinutes);
        UpdatedAt = now;
    }

    public void SetMoveLimit(int moveLimit, DateTimeOffset now)
    {
        MoveLimit = BookingMove.ClampLimit(moveLimit);
        UpdatedAt = now;
    }

    /// <summary>Whether this venue can be paid at all yet.</summary>
    public bool CanTakePayment =>
        !string.IsNullOrWhiteSpace(GcashNumber) || !string.IsNullOrWhiteSpace(GcashQrCodeUrl);

    public void SetPaymentDetails(
        string? gcashNumber,
        string? gcashAccountName,
        string? gcashQrCodeUrl,
        int partialBookingExpiryMinutes,
        DateTimeOffset now)
    {
        GcashNumber = string.IsNullOrWhiteSpace(gcashNumber) ? null : gcashNumber.Trim();
        GcashAccountName = string.IsNullOrWhiteSpace(gcashAccountName) ? null : gcashAccountName.Trim();
        GcashQrCodeUrl = string.IsNullOrWhiteSpace(gcashQrCodeUrl) ? null : gcashQrCodeUrl.Trim();
        PartialBookingExpiryMinutes = PaymentHold.Clamp(partialBookingExpiryMinutes);
        UpdatedAt = now;
    }
    public ICollection<FacilityOwnerDocument> Documents { get; private set; } = [];
    public ICollection<FacilityOwnerContract> Contracts { get; private set; } = [];
    public ICollection<Facility> Facilities { get; private set; } = [];

    /// <summary>
    /// Derived rather than stored, so it cannot drift from the contract dates.
    /// An admin encoding an owner does not make them bookable: a contract has to
    /// commence first.
    /// </summary>
    public FacilityOwnerStatus StatusOn(DateOnly date) => DeriveStatus(
        IsActive,
        Contracts
            .Where(contract => contract.CancelledAt is null)
            .Select(contract => new ContractTerm(contract.StartDate, contract.EndDate)),
        date);

    /// <summary>
    /// The one definition of what a facility owner's status means. Static and
    /// shared because the admin list projects contract dates straight out of the
    /// database rather than loading the graph, and two copies of this rule would
    /// eventually disagree about who is bookable.
    /// </summary>
    public static FacilityOwnerStatus DeriveStatus(
        bool isActive,
        IEnumerable<ContractTerm> liveTerms,
        DateOnly date)
    {
        if (!isActive)
        {
            return FacilityOwnerStatus.Suspended;
        }

        var terms = liveTerms as IReadOnlyCollection<ContractTerm> ?? [.. liveTerms];

        if (terms.Any(term => term.StartDate <= date && date <= term.EndDate))
        {
            return FacilityOwnerStatus.Commenced;
        }

        // Expired means a term ran out, not that one has yet to start: an owner
        // encoded today against next month's contract is still Pending.
        return terms.Any(term => term.EndDate < date)
            ? FacilityOwnerStatus.Expired
            : FacilityOwnerStatus.Pending;
    }

    public bool IsBookableOn(DateOnly date) => StatusOn(date) == FacilityOwnerStatus.Commenced;

    public void UpdateBusinessDetails(
        string businessName,
        string billingEmail,
        string? billingPhone,
        string? businessRegistrationNumber,
        DateTimeOffset now)
    {
        BusinessName = businessName.Trim();
        BillingEmail = billingEmail.Trim().ToLowerInvariant();
        BillingPhone = string.IsNullOrWhiteSpace(billingPhone) ? null : billingPhone.Trim();
        BusinessRegistrationNumber = string.IsNullOrWhiteSpace(businessRegistrationNumber)
            ? null
            : businessRegistrationNumber.Trim();
        UpdatedAt = now;
    }

    public void Suspend(DateTimeOffset now)
    {
        IsActive = false;
        UpdatedAt = now;
    }

    public void Reinstate(DateTimeOffset now)
    {
        IsActive = true;
        UpdatedAt = now;
    }
}

public sealed class PlatformAdmin : Entity
{
    private PlatformAdmin()
    {
    }
    public PlatformAdmin(Guid userId) => UserId = userId;
    public Guid UserId
    {
        get; private set;
    }
    public User User { get; private set; } = null!;
}
public sealed class RefreshToken : Entity
{
    private RefreshToken()
    {
    }
    public RefreshToken(Guid userId, string tokenHash, DateTimeOffset expiresAt)
        : this(userId, tokenHash, expiresAt, DateTimeOffset.UtcNow, false)
    {
    }
    public RefreshToken(
        Guid userId,
        string tokenHash,
        DateTimeOffset expiresAt,
        DateTimeOffset issuedAt,
        bool isPersistent,
        string? userAgent = null,
        string? ipAddress = null)
    {
        UserId = userId;
        TokenHash = tokenHash;
        ExpiresAt = expiresAt;
        CreatedAt = issuedAt;
        IsPersistent = isPersistent;
        UserAgent = userAgent;
        IpAddress = ipAddress;
    }
    public Guid UserId
    {
        get; private set;
    }
    public User User { get; private set; } = null!;
    public string TokenHash { get; private set; } = string.Empty;
    public DateTimeOffset ExpiresAt
    {
        get; private set;
    }
    public DateTimeOffset? RevokedAt
    {
        get; private set;
    }
    /// <summary>
    /// True when the visitor asked to be remembered. Recorded per token so a
    /// rotation can size the replacement without guessing from timestamps.
    /// </summary>
    public bool IsPersistent
    {
        get; private set;
    }
    public string? ReplacedByTokenHash
    {
        get; private set;
    }
    /// <summary>Raw user agent, kept unparsed so the client can format it.</summary>
    public string? UserAgent
    {
        get; private set;
    }
    public string? IpAddress
    {
        get; private set;
    }
    /// <summary>Stamped on every rotation, so the list can show recent activity.</summary>
    public DateTimeOffset? LastUsedAt
    {
        get; private set;
    }
    public bool IsActive(DateTimeOffset now) => RevokedAt is null && ExpiresAt > now;
    public void RecordUse(DateTimeOffset now)
    {
        LastUsedAt = now;
        UpdatedAt = now;
    }
    public void Revoke(DateTimeOffset now, string? replacementHash = null)
    {
        RevokedAt = now;
        ReplacedByTokenHash = replacementHash;
        UpdatedAt = now;
    }
}
