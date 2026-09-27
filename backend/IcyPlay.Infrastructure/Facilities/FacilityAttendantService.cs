using System.Security.Cryptography;
using IcyPlay.Application.Audit;
using IcyPlay.Application.Email;
using IcyPlay.Application.Facilities;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.Email;
using IcyPlay.Domain.Facilities;
using IcyPlay.Domain.Identity;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace IcyPlay.Infrastructure.Facilities;

/// <summary>
/// Who works a venue's desk.
///
/// The owner is on every list and in no table: they attend their own venues by
/// owning them. Storing a row for that would be a second copy of what the
/// ownership already says, and the day the two disagree is the day somebody
/// cannot confirm a booking in a place they own.
/// </summary>
public sealed class FacilityAttendantService(
    AppDbContext db,
    IAccountInvitationService invitations,
    IPasswordHasher<User> passwordHasher,
    IAuditLogger audit,
    TimeProvider timeProvider,
    ILogger<FacilityAttendantService> logger) : IFacilityAttendantService
{
    public async Task<Guid?> FacilityOwnerIdOfAsync(Guid userId, CancellationToken ct) =>
        await db.FacilityOwners
            .AsNoTracking()
            .Where(owner => owner.UserId == userId)
            .Select(owner => (Guid?)owner.Id)
            .FirstOrDefaultAsync(ct);

    public async Task<AttendantResult<IReadOnlyCollection<FacilityAttendantDetail>>> ListAsync(
        Guid facilityOwnerId,
        Guid facilityId,
        CancellationToken ct)
    {
        var venue = await VenueAsync(facilityOwnerId, facilityId, ct);

        if (venue is null)
        {
            return AttendantResult<IReadOnlyCollection<FacilityAttendantDetail>>
                .Fail(AttendantFailure.FacilityNotFound);
        }

        return AttendantResult<IReadOnlyCollection<FacilityAttendantDetail>>
            .Success(await RosterAsync(venue, ct));
    }

    public async Task<AttendantResult<AttendantEmailCheck>> CheckEmailAsync(
        Guid facilityOwnerId,
        Guid facilityId,
        string email,
        CancellationToken ct)
    {
        var venue = await VenueAsync(facilityOwnerId, facilityId, ct);

        if (venue is null)
        {
            return AttendantResult<AttendantEmailCheck>.Fail(AttendantFailure.FacilityNotFound);
        }

        var wanted = (email ?? string.Empty).Trim().ToLowerInvariant();

        var holder = await db.Users
            .AsNoTracking()
            .Where(user => user.Email == wanted)
            .Select(user => new { user.Id, user.FullName })
            .SingleOrDefaultAsync(ct);

        if (holder is null)
        {
            return AttendantResult<AttendantEmailCheck>.Success(
                new AttendantEmailCheck(wanted, AttendantEmailStatus.Available, null));
        }

        if (holder.Id == venue.OwnerUserId)
        {
            return AttendantResult<AttendantEmailCheck>.Success(
                new AttendantEmailCheck(wanted, AttendantEmailStatus.IsTheOwner, holder.FullName));
        }

        // Being on the desk today is worth saying in its own words. Everything
        // else about a taken address amounts to the same answer: it is spoken
        // for, and this venue cannot have it.
        var onDesk = await db.FacilityAttendants
            .AsNoTracking()
            .AnyAsync(
                attendant => attendant.FacilityId == facilityId
                    && attendant.UserId == holder.Id
                    && attendant.IsActive,
                ct);

        return AttendantResult<AttendantEmailCheck>.Success(
            new AttendantEmailCheck(
                wanted,
                onDesk ? AttendantEmailStatus.AlreadyAttending : AttendantEmailStatus.AlreadyRegistered,
                holder.FullName));
    }

    public async Task<AttendantResult<FacilityAttendantDetail>> InviteAsync(
        Guid facilityOwnerId,
        Guid facilityId,
        InviteAttendantRequest request,
        AuditActor actor,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);

        var venue = await VenueAsync(facilityOwnerId, facilityId, ct);

        if (venue is null)
        {
            return AttendantResult<FacilityAttendantDetail>.Fail(AttendantFailure.FacilityNotFound);
        }

        var email = request.Email.Trim().ToLowerInvariant();
        var now = timeProvider.GetUtcNow();

        var existing = await db.Users.SingleOrDefaultAsync(user => user.Email == email, ct);

        if (existing is not null && existing.Id == venue.OwnerUserId)
        {
            // They already attend it, by owning it.
            return AttendantResult<FacilityAttendantDetail>.Fail(AttendantFailure.IsTheOwner);
        }

        if (existing is not null)
        {
            // One address, one account, settled before anything is written. An
            // admin typing an address that is already taken is naming somebody
            // this console has not shown them, so it is refused rather than
            // handed a desk and the bookings that come with it.
            var existingId = existing.Id;

            var onDesk = await db.FacilityAttendants.AnyAsync(
                attendant => attendant.FacilityId == facilityId
                    && attendant.UserId == existingId
                    && attendant.IsActive,
                ct);

            return AttendantResult<FacilityAttendantDetail>.Fail(
                onDesk ? AttendantFailure.AlreadyAttending : AttendantFailure.EmailAlreadyRegistered);
        }

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        var user = new User(email, request.FullName.Trim(), request.PhoneNumber);

        // Nobody, the inviting admin included, ever knows this password. The
        // attendant sets their own through the emailed link.
        user.SetPasswordHash(passwordHasher.HashPassword(user, UnguessablePassword()));
        db.Users.Add(user);
        db.UserRoles.Add(new UserRole(user.Id, UserRoleName.FacilityAttendant));
        db.FacilityAttendants.Add(new FacilityAttendant(facilityId, user.Id, now));

        audit.RecordEvent(
            actor,
            AuditAction.FacilityAttendantAdded,
            AuditEntityType.Facility,
            facilityId,
            new Dictionary<string, string?>
            {
                ["attendant"] = user.FullName,
                ["email"] = user.Email
            },
            request.Reason);

        await db.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        // After the commit, and best effort: a Mailjet outage must not undo a
        // desk assignment the admin has already made. They can resend.
        await InviteByEmailAsync(user, venue, ct);

        var roster = await RosterAsync(venue, ct);

        return AttendantResult<FacilityAttendantDetail>.Success(
            roster.Single(attendant => attendant.UserId == user.Id));
    }

    public async Task<AttendantResult<bool>> ResendInvitationAsync(
        Guid facilityOwnerId,
        Guid facilityId,
        Guid attendantId,
        AuditActor actor,
        CancellationToken ct)
    {
        var venue = await VenueAsync(facilityOwnerId, facilityId, ct);

        if (venue is null)
        {
            return AttendantResult<bool>.Fail(AttendantFailure.FacilityNotFound);
        }

        var attendant = await db.FacilityAttendants
            .AsNoTracking()
            .Where(candidate => candidate.Id == attendantId
                && candidate.FacilityId == facilityId
                && candidate.IsActive)
            .Select(candidate => new
            {
                candidate.UserId,
                candidate.User.Email,
                candidate.User.FullName,
                // Whether any invitation of theirs has been claimed. Read from
                // the invitation rather than from the verified address, which
                // ordinary registration sets for reasons of its own.
                Accepted = db.AccountInvitationTokens
                    .Any(token => token.UserId == candidate.UserId && token.AcceptedAt != null),
                EverInvited = db.AccountInvitationTokens
                    .Any(token => token.UserId == candidate.UserId)
            })
            .SingleOrDefaultAsync(ct);

        if (attendant is null)
        {
            return AttendantResult<bool>.Fail(AttendantFailure.AttendantNotFound);
        }

        // Never invited means the address already had an account when they were
        // put on the desk: they have a password of their own, and sending them
        // an activation link would offer to replace it.
        if (attendant.Accepted || !attendant.EverInvited)
        {
            return AttendantResult<bool>.Fail(AttendantFailure.InvitationAlreadyAccepted);
        }

        await invitations.SendAsync(
            new InvitationRequest(
                attendant.UserId,
                attendant.Email,
                attendant.FullName,
                venue.BusinessName,
                EmailTemplateKey.FacilityAttendantInvitation,
                venue.Name),
            ct);

        audit.RecordEvent(
            actor,
            AuditAction.FacilityAttendantInvitationSent,
            AuditEntityType.Facility,
            facilityId,
            new Dictionary<string, string?>
            {
                ["attendant"] = attendant.FullName,
                ["email"] = attendant.Email
            });

        await db.SaveChangesAsync(ct);

        return AttendantResult<bool>.Success(true);
    }

    public async Task<AttendantResult<bool>> RemoveAsync(
        Guid facilityOwnerId,
        Guid facilityId,
        Guid attendantId,
        string? reason,
        AuditActor actor,
        CancellationToken ct)
    {
        var venue = await VenueAsync(facilityOwnerId, facilityId, ct);

        if (venue is null)
        {
            return AttendantResult<bool>.Fail(AttendantFailure.FacilityNotFound);
        }

        var attendant = await db.FacilityAttendants
            .Include(candidate => candidate.User)
            .SingleOrDefaultAsync(
                candidate => candidate.Id == attendantId && candidate.FacilityId == facilityId,
                ct);

        if (attendant is null)
        {
            return AttendantResult<bool>.Fail(AttendantFailure.AttendantNotFound);
        }

        attendant.Retire(timeProvider.GetUtcNow());

        audit.RecordEvent(
            actor,
            AuditAction.FacilityAttendantRemoved,
            AuditEntityType.Facility,
            facilityId,
            new Dictionary<string, string?>
            {
                ["attendant"] = attendant.User.FullName,
                ["email"] = attendant.User.Email
            },
            reason);

        await db.SaveChangesAsync(ct);

        return AttendantResult<bool>.Success(true);
    }

    /// <summary>
    /// The venue, scoped to its owner. Another owner's facility answers the
    /// same as one that does not exist.
    /// </summary>
    private async Task<Venue?> VenueAsync(Guid facilityOwnerId, Guid facilityId, CancellationToken ct) =>
        await db.Facilities
            .AsNoTracking()
            .Where(facility => facility.Id == facilityId && facility.FacilityOwnerId == facilityOwnerId)
            .Select(facility => new Venue(
                facility.Id,
                facility.Name,
                facility.FacilityOwner.UserId,
                facility.FacilityOwner.BusinessName))
            .SingleOrDefaultAsync(ct);

    /// <summary>The owner first, then whoever else is on the desk.</summary>
    private async Task<IReadOnlyCollection<FacilityAttendantDetail>> RosterAsync(
        Venue venue,
        CancellationToken ct)
    {
        var owner = await db.Users
            .AsNoTracking()
            .Where(user => user.Id == venue.OwnerUserId)
            .Select(user => new FacilityAttendantDetail(
                null,
                user.Id,
                user.FullName,
                user.Email,
                user.PhoneNumber,
                true,
                true,
                false,
                0,
                null,
                null,
                true))
            .SingleAsync(ct);

        var staff = await db.FacilityAttendants
            .AsNoTracking()
            .Where(attendant => attendant.FacilityId == venue.Id && attendant.IsActive)
            .OrderBy(attendant => attendant.CreatedAt)
            .Select(attendant => new
            {
                attendant.Id,
                attendant.UserId,
                attendant.User.FullName,
                attendant.User.Email,
                attendant.User.PhoneNumber,
                attendant.CreatedAt,
                attendant.CanSeeMoney,
                // Claiming any invitation sets the password, so any accepted
                // token means the account is theirs.
                Accepted = db.AccountInvitationTokens
                    .Any(token => token.UserId == attendant.UserId && token.AcceptedAt != null),
                // Every send writes a token, so counting them counts the sends:
                // one for the original invitation and one for each resend. They
                // are counted per person rather than per venue because a person
                // with an account cannot be added to a second desk at all, so
                // one person's tokens all belong to the desk they are on.
                InvitationsSent = db.AccountInvitationTokens
                    .Count(token => token.UserId == attendant.UserId),
                LastInvitedAt = db.AccountInvitationTokens
                    .Where(token => token.UserId == attendant.UserId)
                    .Max(token => (DateTimeOffset?)token.CreatedAt)
            })
            .ToListAsync(ct);

        return
        [
            owner,
            .. staff.Select(attendant => new FacilityAttendantDetail(
                attendant.Id,
                attendant.UserId,
                attendant.FullName,
                attendant.Email,
                attendant.PhoneNumber,
                false,
                // Never invited means the address already had an account when
                // they were added: they have a password and can sign in today,
                // so there is nothing for them to accept.
                attendant.InvitationsSent == 0 || attendant.Accepted,
                attendant.InvitationsSent > 0,
                attendant.InvitationsSent,
                attendant.LastInvitedAt,
                attendant.CreatedAt,
                attendant.CanSeeMoney))
        ];
    }

    private async Task InviteByEmailAsync(User user, Venue venue, CancellationToken ct)
    {
        try
        {
            await invitations.SendAsync(
                new InvitationRequest(
                    user.Id,
                    user.Email,
                    user.FullName,
                    venue.BusinessName,
                    EmailTemplateKey.FacilityAttendantInvitation,
                    venue.Name),
                ct);
        }
        catch (Exception exception)
        {
            logger.LogError(
                exception,
                "The attendant invitation could not be sent to user {UserId}.",
                user.Id);
        }
    }

    private static string UnguessablePassword() =>
        WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32));

    private sealed record Venue(Guid Id, string Name, Guid OwnerUserId, string BusinessName);
}
