using System.Data;
using System.Globalization;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Common;
using IcyPlay.Application.OpenPlays;
using IcyPlay.Application.Storage;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.Infrastructure.Bookings;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>
/// Players joining open plays, and the desk checking their payments.
///
/// The same road as a court booking: a hold while the player pays, a GCash
/// receipt that stops the clock and hands it to the desk, and a person at the
/// venue who confirms or turns it down. Only a confirmed registration is a
/// registered player; the hold and the wait reserve the spot so it is not sold
/// twice while somebody pays for it.
/// </summary>
public sealed class OpenPlayRegistrationService(
    AppDbContext db,
    IAuditLogger audit,
    ICloudinaryAssetService assets,
    IOpenPlayNotifier notifier,
    TimeProvider timeProvider) : ICustomerOpenPlayService, IDeskOpenPlayRequestService
{
    private const int LargestPage = 50;

    // ------------------------------------------------------------ the player

    public async Task<OpenPlayRegistrationResult<OpenPlayRegistrationDetail>> RegisterAsync(
        Guid customerUserId,
        Guid openPlayId,
        RegisterForOpenPlayRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!request.AgreedToPolicy)
        {
            return Fail<OpenPlayRegistrationDetail>(OpenPlayRegistrationFailure.PolicyNotAgreed);
        }

        var now = timeProvider.GetUtcNow();

        // The open play as a player can see it: published, on a live court at
        // a live venue. Anything else is not usefully different from one that
        // does not exist.
        var found = await db.OpenPlays
            .AsNoTracking()
            .Where(openPlay => openPlay.Id == openPlayId
                && openPlay.PublishedAt != null
                && openPlay.BookableCourt.IsActive
                && openPlay.BookableCourt.Court.IsActive
                && openPlay.Facility.IsActive
                && openPlay.Facility.FacilityOwner.IsActive)
            .Select(openPlay => new
            {
                OpenPlay = openPlay,
                openPlay.Facility.TimeZone,
                Owner = openPlay.Facility.FacilityOwner
            })
            .SingleOrDefaultAsync(ct);

        if (found is null)
        {
            return Fail<OpenPlayRegistrationDetail>(OpenPlayRegistrationFailure.NotFound);
        }

        var openPlay = found.OpenPlay;
        var venueNow = VenueClock.LocalNowIn(found.TimeZone, now).DateTime;
        var today = DateOnly.FromDateTime(venueNow);

        var contract = await db.FacilityOwnerContracts
            .AsNoTracking()
            .Where(candidate => candidate.FacilityOwnerId == found.Owner.Id
                && candidate.CancelledAt == null
                && candidate.StartDate <= today
                && candidate.EndDate >= today)
            .FirstOrDefaultAsync(ct);

        if (contract is null)
        {
            // No term covers today, so the venue is not live.
            return Fail<OpenPlayRegistrationDetail>(OpenPlayRegistrationFailure.NotFound);
        }

        if (!openPlay.RunsOn(request.Date) || await IsClosedAsync(openPlay, found.TimeZone, request.Date, ct))
        {
            return Fail<OpenPlayRegistrationDetail>(OpenPlayRegistrationFailure.NotRunning);
        }

        // On the venue's clock, from the server. The browser's is never asked.
        if (!openPlay.IsOpenForRegistration(request.Date, venueNow))
        {
            return Fail<OpenPlayRegistrationDetail>(OpenPlayRegistrationFailure.RegistrationClosed);
        }

        if (!found.Owner.CanTakePayment)
        {
            return Fail<OpenPlayRegistrationDetail>(OpenPlayRegistrationFailure.VenueCannotBePaid);
        }

        // Counting the spots and taking one happen together, or two players
        // pressing Join at once could both be told there was one left.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);

        var session = await db.OpenPlaySessions
            .Include(candidate => candidate.Registrations)
            .SingleOrDefaultAsync(candidate => candidate.OpenPlayId == openPlay.Id && candidate.Date == request.Date, ct);

        if (session is null)
        {
            session = new OpenPlaySession(openPlay.Id, request.Date, now);
            db.OpenPlaySessions.Add(session);
        }
        else if (session.IsCancelled)
        {
            return Fail<OpenPlayRegistrationDetail>(OpenPlayRegistrationFailure.SessionCancelled);
        }

        // Already holding a spot: back to the same registration rather than a
        // second one. Pressing Join twice is ordinary; paying twice is not.
        var mine = session.Registrations.FirstOrDefault(registration =>
            registration.CustomerUserId == customerUserId && registration.HoldsSpotAt(now));

        if (mine is not null)
        {
            return await DetailAsync(mine.Id, customerUserId, ct);
        }

        if (session.SpotsLeftAt(now, openPlay.MaxPlayers) == 0)
        {
            return Fail<OpenPlayRegistrationDetail>(OpenPlayRegistrationFailure.Full);
        }

        var registration = new OpenPlayRegistration(
            session.Id,
            customerUserId,
            openPlay.PriceFor(request.Date, venueNow, contract.PlatformHourlyRate),
            found.Owner.PartialBookingExpiryMinutes,
            now,
            now);

        db.OpenPlayRegistrations.Add(registration);
        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        return await DetailAsync(registration.Id, customerUserId, ct);
    }

    public Task<OpenPlayRegistrationResult<OpenPlayRegistrationDetail>> GetAsync(
        Guid customerUserId,
        Guid registrationId,
        CancellationToken ct) =>
        DetailAsync(registrationId, customerUserId, ct);

    public async Task<IReadOnlyCollection<OpenPlayRegistrationDetail>> ListMineAsync(
        Guid customerUserId,
        CancellationToken ct)
    {
        var ids = await db.OpenPlayRegistrations
            .AsNoTracking()
            .Where(registration => registration.CustomerUserId == customerUserId)
            .Select(registration => registration.Id)
            .ToArrayAsync(ct);

        return [.. (await ProjectAsync(ids, ct))
            .OrderByDescending(detail => detail.Date)
            .ThenByDescending(detail => detail.StartsAt)];
    }

    public async Task<OpenPlayRegistrationResult<OpenPlayRegistrationDetail>> SendReceiptAsync(
        Guid customerUserId,
        Guid registrationId,
        OpenPlayReceiptRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var registration = await db.OpenPlayRegistrations
            .Include(candidate => candidate.Session)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == registrationId && candidate.CustomerUserId == customerUserId,
                ct);

        if (registration is null)
        {
            return Fail<OpenPlayRegistrationDetail>(OpenPlayRegistrationFailure.NotFound);
        }

        var now = timeProvider.GetUtcNow();

        if (registration.Status is not (BookingStatus.PendingPayment or BookingStatus.PendingVerification))
        {
            return Fail<OpenPlayRegistrationDetail>(OpenPlayRegistrationFailure.NotAwaitingPayment);
        }

        if (registration.HasLapsedAt(now))
        {
            // The spot went back while they were paying. Better to say so
            // than to take a receipt for a spot somebody else now has.
            return Fail<OpenPlayRegistrationDetail>(OpenPlayRegistrationFailure.HoldExpired);
        }

        if (!assets.IsTrustedSecureUrl(request.ReceiptUrl))
        {
            return Fail<OpenPlayRegistrationDetail>(OpenPlayRegistrationFailure.UntrustedReceiptUrl);
        }

        if (!ReceiptLinks.IsImage(request.ReceiptUrl))
        {
            return Fail<OpenPlayRegistrationDetail>(OpenPlayRegistrationFailure.ReceiptNotAnImage);
        }

        var canBePaid = await db.OpenPlays
            .AsNoTracking()
            .Where(openPlay => openPlay.Id == registration.Session.OpenPlayId)
            .Select(openPlay => openPlay.Facility.FacilityOwner)
            .FirstOrDefaultAsync(ct);

        if (canBePaid is null || !canBePaid.CanTakePayment)
        {
            return Fail<OpenPlayRegistrationDetail>(OpenPlayRegistrationFailure.VenueCannotBePaid);
        }

        var first = registration.SendReceipt(request.ReceiptUrl, now);

        audit.RecordEvent(
            new AuditActor(customerUserId, Domain.Identity.UserRoleName.Customer),
            AuditAction.OpenPlayPaymentSubmitted,
            AuditEntityType.OpenPlayRegistration,
            registration.Id,
            new Dictionary<string, string?> { ["receiptUrl"] = registration.ReceiptUrl },
            first ? "Payment sent to the venue to check." : "Sent a different receipt to the venue.");

        await db.SaveChangesAsync(ct);

        if (first)
        {
            // After the save, deliberately: a mail provider being down must
            // not undo a submission the player has been told went through.
            await notifier.PaymentSubmittedAsync(registration.Id, ct);
        }

        return await DetailAsync(registration.Id, customerUserId, ct);
    }

    // ------------------------------------------------------------ the desk

    public async Task<OpenPlayRegistrationResult<PagedResult<DeskOpenPlayRequest>>> ListAsync(
        Guid userId,
        string tab,
        Guid? facilityId,
        int page,
        int pageSize,
        CancellationToken ct)
    {
        if (!OpenPlayRequestTab.IsSupported(tab))
        {
            return Fail<PagedResult<DeskOpenPlayRequest>>(OpenPlayRegistrationFailure.UnknownTab);
        }

        var venueIds = await DeskVenues(userId).ToListAsync(ct);

        if (facilityId is Guid wanted)
        {
            if (!venueIds.Contains(wanted))
            {
                return Fail<PagedResult<DeskOpenPlayRequest>>(OpenPlayRegistrationFailure.NotFound);
            }

            venueIds = [wanted];
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, LargestPage);

        var status = tab == OpenPlayRequestTab.Waiting ? BookingStatus.PendingVerification : BookingStatus.Confirmed;

        var rows = db.OpenPlayRegistrations
            .AsNoTracking()
            .Where(registration => registration.Status == status
                && venueIds.Contains(registration.Session.OpenPlay.FacilityId));

        var total = await rows.CountAsync(ct);

        // The waiting ones oldest first, because the one who has waited
        // longest is next; the confirmed ones by the session they are for.
        var ordered = status == BookingStatus.PendingVerification
            ? rows.OrderBy(registration => registration.SubmittedForVerificationAt)
            : rows.OrderByDescending(registration => registration.Session.Date)
                .ThenByDescending(registration => registration.ConfirmedAt);

        var ids = await ordered
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(registration => registration.Id)
            .ToListAsync(ct);

        var requests = await RequestsAsync(ids, ct);

        return OpenPlayRegistrationResult<PagedResult<DeskOpenPlayRequest>>.Success(
            new PagedResult<DeskOpenPlayRequest>(
                [.. ids.Select(id => requests.Single(request => request.RegistrationId == id))],
                page,
                pageSize,
                total));
    }

    public async Task<OpenPlayRegistrationResult<DeskOpenPlayRequest>> ConfirmAsync(
        AuditActor actor,
        Guid registrationId,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);

        var registration = await ForDeskAsync(actor, registrationId, ct);

        if (registration is null)
        {
            return Fail<DeskOpenPlayRequest>(OpenPlayRegistrationFailure.NotFound);
        }

        if (registration.Status != BookingStatus.PendingVerification)
        {
            return Fail<DeskOpenPlayRequest>(OpenPlayRegistrationFailure.NotWaiting);
        }

        registration.Confirm(actor.UserId!.Value, timeProvider.GetUtcNow());

        audit.RecordEvent(
            actor,
            AuditAction.OpenPlayRegistrationConfirmed,
            AuditEntityType.OpenPlayRegistration,
            registration.Id,
            new Dictionary<string, string?>
            {
                ["total"] = registration.Total.ToString("0.00", CultureInfo.InvariantCulture)
            },
            "Payment checked: the player is registered.");

        await db.SaveChangesAsync(ct);

        await notifier.ConfirmedAsync(registration.Id, ct);

        return OpenPlayRegistrationResult<DeskOpenPlayRequest>.Success(
            (await RequestsAsync([registration.Id], ct)).Single());
    }

    public async Task<OpenPlayRegistrationResult<DeskOpenPlayRequest>> RejectAsync(
        AuditActor actor,
        Guid registrationId,
        RejectOpenPlayRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        if (!RejectReason.IsSupported(request.Reason))
        {
            return Fail<DeskOpenPlayRequest>(OpenPlayRegistrationFailure.UnknownReason);
        }

        var registration = await ForDeskAsync(actor, registrationId, ct);

        if (registration is null)
        {
            return Fail<DeskOpenPlayRequest>(OpenPlayRegistrationFailure.NotFound);
        }

        if (registration.Status != BookingStatus.PendingVerification)
        {
            return Fail<DeskOpenPlayRequest>(OpenPlayRegistrationFailure.NotWaiting);
        }

        var note = request.Note is { Length: > RejectReason.NoteLimit }
            ? request.Note[..RejectReason.NoteLimit]
            : request.Note;

        registration.Reject(request.Reason, note, actor.UserId!.Value, timeProvider.GetUtcNow());

        audit.RecordEvent(
            actor,
            AuditAction.OpenPlayRegistrationRejected,
            AuditEntityType.OpenPlayRegistration,
            registration.Id,
            new Dictionary<string, string?>
            {
                ["reason"] = registration.RejectionReason,
                ["note"] = registration.RejectionNote
            },
            registration.CancellationReason);

        await db.SaveChangesAsync(ct);
        await notifier.DeclinedAsync(registration.Id, ct);

        return OpenPlayRegistrationResult<DeskOpenPlayRequest>.Success(
            (await RequestsAsync([registration.Id], ct)).Single());
    }

    // ------------------------------------------------------------ the reads

    private static OpenPlayRegistrationResult<T> Fail<T>(OpenPlayRegistrationFailure failure) =>
        OpenPlayRegistrationResult<T>.Fail(failure);

    /// <summary>The venues this person works: the ones they own, and the ones they are on the desk of.</summary>
    private IQueryable<Guid> DeskVenues(Guid userId) =>
        db.Facilities
            .Where(facility => facility.FacilityOwner.UserId == userId
                || facility.Attendants.Any(attendant => attendant.UserId == userId && attendant.IsActive))
            .Select(facility => facility.Id);

    /// <summary>
    /// The registration, tracked, only if it is at a venue this person works.
    /// One at somebody else's venue answers the same as one that is not there.
    /// </summary>
    private async Task<OpenPlayRegistration?> ForDeskAsync(AuditActor actor, Guid registrationId, CancellationToken ct)
    {
        var venueIds = DeskVenues(actor.UserId ?? Guid.Empty);

        return await db.OpenPlayRegistrations.SingleOrDefaultAsync(
            registration => registration.Id == registrationId
                && venueIds.Contains(registration.Session.OpenPlay.FacilityId),
            ct);
    }

    /// <summary>A maintenance closure that touches the session's hours, for the venue or this court.</summary>
    private async Task<bool> IsClosedAsync(OpenPlay openPlay, string timeZone, DateOnly date, CancellationToken ct)
    {
        var starts = AtVenue(date.ToDateTime(openPlay.StartsAt), timeZone);
        var ends = AtVenue(date.ToDateTime(openPlay.EndsAt), timeZone);

        return await db.MaintenancePeriods.AnyAsync(
            period => period.FacilityId == openPlay.FacilityId
                && period.LiftedAt == null
                && (period.CourtId == null || period.CourtId == openPlay.CourtId)
                && period.StartsAt < ends
                && (period.EndsAt == null || starts < period.EndsAt),
            ct);
    }

    private static DateTimeOffset AtVenue(DateTime wallClock, string timeZone) =>
        TimeZoneInfo.TryFindSystemTimeZoneById(timeZone, out var zone)
            ? new DateTimeOffset(wallClock, zone.GetUtcOffset(wallClock))
            : new DateTimeOffset(wallClock, TimeSpan.Zero);

    private async Task<OpenPlayRegistrationResult<OpenPlayRegistrationDetail>> DetailAsync(
        Guid registrationId,
        Guid customerUserId,
        CancellationToken ct)
    {
        var mine = await db.OpenPlayRegistrations
            .AsNoTracking()
            .AnyAsync(registration => registration.Id == registrationId
                && registration.CustomerUserId == customerUserId, ct);

        return mine
            ? OpenPlayRegistrationResult<OpenPlayRegistrationDetail>.Success((await ProjectAsync([registrationId], ct)).Single())
            : Fail<OpenPlayRegistrationDetail>(OpenPlayRegistrationFailure.NotFound);
    }

    private async Task<IReadOnlyCollection<OpenPlayRegistrationDetail>> ProjectAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var now = timeProvider.GetUtcNow();

        var rows = await db.OpenPlayRegistrations
            .AsNoTracking()
            .Where(registration => ids.Contains(registration.Id))
            .Select(registration => new
            {
                Registration = registration,
                registration.Session.Date,
                OpenPlay = registration.Session.OpenPlay,
                SportKey = registration.Session.OpenPlay.BookableCourt.CourtSport.Sport.Key,
                SportName = registration.Session.OpenPlay.BookableCourt.CourtSport.Sport.Name,
                registration.Session.OpenPlay.BookableCourt.DivisionNumber,
                registration.Session.OpenPlay.BookableCourt.CourtSport.Divisions,
                CourtName = registration.Session.OpenPlay.BookableCourt.Court.Name,
                FacilityName = registration.Session.OpenPlay.Facility.Name,
                registration.Session.OpenPlay.Facility.Slug,
                registration.Session.OpenPlay.Facility.City,
                registration.Session.OpenPlay.Facility.ContactPhone,
                registration.Session.OpenPlay.Facility.ContactEmail,
                registration.Session.OpenPlay.Facility.TimeZone,
                Owner = registration.Session.OpenPlay.Facility.FacilityOwner,
                OwnerEmail = registration.Session.OpenPlay.Facility.FacilityOwner.User.Email
            })
            .ToListAsync(ct);

        return
        [
            .. rows.Select(row =>
            {
                var registration = row.Registration;
                var openPlay = row.OpenPlay;
                var contact = new[] { row.ContactPhone, row.ContactEmail }
                    .Where(part => !string.IsNullOrWhiteSpace(part))
                    .ToArray();
                var sessionHasEnded = VenueClock.LocalNowIn(row.TimeZone, now).DateTime
                    >= row.Date.ToDateTime(openPlay.EndsAt);
                var passState = registration.PassState(sessionHasEnded);

                return new OpenPlayRegistrationDetail(
                    registration.Id,
                    registration.Status.ToString(),
                    openPlay.Id,
                    openPlay.Title,
                    openPlay.Level,
                    row.SportKey,
                    row.SportName,
                    openPlay.FacilityId,
                    row.FacilityName,
                    row.Slug,
                    row.City,
                    row.CourtName,
                    DeskService.UnitLabel(row.SportName, row.DivisionNumber, row.Divisions),
                    row.Date,
                    openPlay.StartsAt,
                    openPlay.EndsAt,
                    registration.RegistrationFee,
                    registration.Discount,
                    registration.PlatformFee,
                    registration.Total,
                    registration.HoldsUntil,
                    registration.HasLapsedAt(now),
                    registration.ReceiptUrl,
                    registration.ReceiptUploadedAt,
                    registration.ConfirmedAt,
                    registration.CancelledAt,
                    registration.CancellationReason,
                    registration.AgreedToPolicyAt,
                    registration.CreatedAt,
                    row.Owner.GcashNumber,
                    row.Owner.GcashAccountName,
                    row.Owner.GcashQrCodeUrl,
                    contact.Length > 0 ? string.Join(" · ", contact) : row.OwnerEmail,
                    row.ContactPhone,
                    contact.Length > 0 ? row.ContactEmail : row.OwnerEmail,
                    openPlay.CoverPhotoUrl,
                    sessionHasEnded,
                    passState,
                    // The QR itself only while it can still get them in: a
                    // spent or expired one is not handed out to be saved or shared.
                    passState == CheckInPassState.Active ? CheckInPass.QrContent(registration.CheckInToken!) : null,
                    registration.CheckedInAt);
            })
        ];
    }

    private async Task<IReadOnlyCollection<DeskOpenPlayRequest>> RequestsAsync(
        IReadOnlyCollection<Guid> ids,
        CancellationToken ct)
    {
        if (ids.Count == 0)
        {
            return [];
        }

        var rows = await db.OpenPlayRegistrations
            .AsNoTracking()
            .Where(registration => ids.Contains(registration.Id))
            .Select(registration => new
            {
                Registration = registration,
                registration.Session.Date,
                OpenPlay = registration.Session.OpenPlay,
                FacilityName = registration.Session.OpenPlay.Facility.Name,
                CourtName = registration.Session.OpenPlay.BookableCourt.Court.Name,
                SportName = registration.Session.OpenPlay.BookableCourt.CourtSport.Sport.Name,
                registration.Session.OpenPlay.BookableCourt.DivisionNumber,
                registration.Session.OpenPlay.BookableCourt.CourtSport.Divisions,
                Player = db.Users
                    .Where(user => user.Id == registration.CustomerUserId)
                    .Select(user => new { user.FullName, user.Email, user.PhoneNumber })
                    .FirstOrDefault(),
                Registered = registration.Session.Registrations.Count(other => other.Status == BookingStatus.Confirmed)
            })
            .ToListAsync(ct);

        return
        [
            .. rows.Select(row => new DeskOpenPlayRequest(
                row.Registration.Id,
                row.Registration.Status.ToString(),
                row.OpenPlay.Id,
                row.OpenPlay.Title,
                row.OpenPlay.FacilityId,
                row.FacilityName,
                row.CourtName,
                DeskService.UnitLabel(row.SportName, row.DivisionNumber, row.Divisions),
                row.Date,
                row.OpenPlay.StartsAt,
                row.OpenPlay.EndsAt,
                row.Player?.FullName ?? "A player",
                row.Player?.Email ?? string.Empty,
                row.Player?.PhoneNumber,
                row.Registration.RegistrationFee,
                row.Registration.Discount,
                row.Registration.PlatformFee,
                row.Registration.Total,
                row.Registration.ReceiptUrl,
                row.Registration.SubmittedForVerificationAt,
                row.Registration.ConfirmedAt,
                row.Registered,
                row.OpenPlay.MaxPlayers))
        ];
    }
}
