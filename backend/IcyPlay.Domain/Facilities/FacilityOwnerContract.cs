using IcyPlay.Domain.Common;
using IcyPlay.Domain.Identity;

namespace IcyPlay.Domain.Facilities;

/// <summary>
/// One commencement period for a facility owner. Its own record rather than two
/// columns on the owner, because contracts renew: last year's term and this
/// year's have to coexist, and platform fee pricing will hang off a specific
/// term rather than off whatever dates happen to be current.
/// </summary>
public sealed class FacilityOwnerContract : Entity
{
    private FacilityOwnerContract()
    {
    }

    public FacilityOwnerContract(
        Guid facilityOwnerId,
        DateOnly startDate,
        DateOnly endDate,
        Guid commencedByUserId,
        string? notes,
        DateTimeOffset commencedAt,
        decimal? platformHourlyRate = null,
        decimal? commissionPercentage = null)
    {
        if (endDate < startDate)
        {
            throw new ArgumentException("A contract cannot end before it starts.", nameof(endDate));
        }

        FacilityOwnerId = facilityOwnerId;
        StartDate = startDate;
        EndDate = endDate;
        CommencedByUserId = commencedByUserId;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        PlatformHourlyRate = platformHourlyRate ?? PlatformRates.DefaultHourlyRate;
        CommissionPercentage = commissionPercentage ?? PlatformRates.DefaultCommissionPercentage;
        CreatedAt = commencedAt;
    }

    public Guid FacilityOwnerId
    {
        get; private set;
    }
    public FacilityOwner FacilityOwner { get; private set; } = null!;
    public DateOnly StartDate
    {
        get; private set;
    }
    public DateOnly EndDate
    {
        get; private set;
    }
    /// <summary>The admin who commenced this term.</summary>
    public Guid CommencedByUserId
    {
        get; private set;
    }
    public string? Notes
    {
        get; private set;
    }
    public DateTimeOffset? CancelledAt
    {
        get; private set;
    }

    /// <summary>
    /// The signed agreement. Nullable in the database rather than required,
    /// because terms commenced before this was asked for genuinely have no
    /// document and inventing one would be worse than showing the gap. Every
    /// new term must carry one; the rule lives in validation.
    /// </summary>
    public string? DocumentPublicId
    {
        get; private set;
    }
    public string? DocumentSecureUrl
    {
        get; private set;
    }
    public string? DocumentFileName
    {
        get; private set;
    }
    public string? DocumentContentType
    {
        get; private set;
    }
    public long? DocumentSizeInBytes
    {
        get; private set;
    }

    public bool HasSignedAgreement => DocumentPublicId is not null;

    /// <summary>
    /// Attaches or replaces the signed agreement. Replacing is allowed because
    /// a wrong or unreadable scan is a real mistake, and a term that can never
    /// be corrected is worse than one that records the correction.
    /// </summary>
    public void AttachDocument(
        string publicId,
        string secureUrl,
        string fileName,
        string contentType,
        long sizeInBytes,
        DateTimeOffset now)
    {
        DocumentPublicId = publicId.Trim();
        DocumentSecureUrl = secureUrl.Trim();
        DocumentFileName = fileName.Trim();
        DocumentContentType = contentType.Trim();
        DocumentSizeInBytes = sizeInBytes;
        UpdatedAt = now;
    }

    /// <summary>
    /// What IcyPlay bills the owner for each hour booked on their courts. Held
    /// on the term rather than on the owner: a rate that changed on the owner
    /// would rewrite what was agreed for terms already served.
    /// </summary>
    public decimal PlatformHourlyRate { get; private set; } = PlatformRates.DefaultHourlyRate;

    /// <summary>
    /// The maintenance and commission share of each billing, as a percentage of
    /// the bill itself.
    /// </summary>
    public decimal CommissionPercentage
    {
        get; private set;
    } =
        PlatformRates.DefaultCommissionPercentage;

    /// <summary>
    /// Corrects the dates and the notes of a term. A start date typed wrong is
    /// an ordinary mistake, and a term that can never be corrected is worse
    /// than one that records the correction.
    /// </summary>
    public void Reschedule(DateOnly startDate, DateOnly endDate, string? notes, DateTimeOffset now)
    {
        if (endDate < startDate)
        {
            throw new ArgumentException("A contract cannot end before it starts.", nameof(endDate));
        }

        StartDate = startDate;
        EndDate = endDate;
        Notes = string.IsNullOrWhiteSpace(notes) ? null : notes.Trim();
        UpdatedAt = now;
    }

    public void SetRates(decimal platformHourlyRate, decimal commissionPercentage, DateTimeOffset now)
    {
        PlatformHourlyRate = platformHourlyRate;
        CommissionPercentage = commissionPercentage;
        UpdatedAt = now;
    }

    /// <summary>
    /// What the owner owes IcyPlay for a period's bookings, and how much of
    /// that bill is the maintenance and commission share. Worked out here
    /// rather than at each call site, because a rate charged in one place and a
    /// percentage taken in another is exactly the arithmetic nobody notices is
    /// wrong.
    /// </summary>
    public ContractCharges ChargesFor(decimal bookedHours)
    {
        var platformBill = Round(PlatformHourlyRate * bookedHours);
        // Of the bill, not on top of it.
        var commission = Round(platformBill * CommissionPercentage / 100m);

        return new ContractCharges(
            bookedHours,
            platformBill,
            commission,
            platformBill - commission);
    }

    private static decimal Round(decimal amount) =>
        Math.Round(amount, 2, MidpointRounding.AwayFromZero);

    public bool Covers(DateOnly date) =>
        CancelledAt is null && StartDate <= date && date <= EndDate;

    public void Cancel(DateTimeOffset now)
    {
        CancelledAt = now;
        UpdatedAt = now;
    }
}

/// <summary>
/// What IcyPlay charges unless a contract says otherwise. Constants rather than
/// a settings table: nobody has asked to change the platform-wide figure, and a
/// screen for it is a different feature from overriding one owner's terms.
/// </summary>
public static class PlatformRates
{
    /// <summary>Pesos added to every booked hour, on top of the court's own rate.</summary>
    public const decimal DefaultHourlyRate = 15.00m;

    /// <summary>Per cent of each billing kept for maintenance and commission.</summary>
    public const decimal DefaultCommissionPercentage = 3.00m;
}

/// <summary>
/// One period's bill to a facility owner. Every figure is derived from the
/// hours and the term's rates, so an invoice and a statement can never disagree
/// about the same period.
/// </summary>
public readonly record struct ContractCharges(
    decimal BookedHours,
    /// <summary>Hours booked times the term's hourly rate.</summary>
    decimal PlatformBill,
    /// <summary>The maintenance and commission share, taken out of the bill.</summary>
    decimal Commission,
    decimal NetAfterCommission);

/// <summary>
/// The dates of one live contract, so status can be derived from a projection
/// without loading whole entities.
/// </summary>
public readonly record struct ContractTerm(DateOnly StartDate, DateOnly EndDate);
