using IcyPlay.Application.Audit;
using IcyPlay.Application.OpenPlays;
using IcyPlay.Domain.Audit;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>The desk's list of an open play's claim stubs, and marking one claimed.</summary>
public sealed class DeskClaimStubService(
    AppDbContext db,
    IAuditLogger audit,
    TimeProvider timeProvider) : IDeskClaimStubService
{
    public async Task<OpenPlayRegistrationResult<IReadOnlyList<ClaimStubView>>> ListAsync(
        Guid userId,
        Guid openPlayId,
        CancellationToken ct)
    {
        var openPlay = await db.OpenPlays
            .AsNoTracking()
            .Where(candidate => candidate.Id == openPlayId && VenuesOf(userId).Contains(candidate.FacilityId))
            .Select(candidate => new { candidate.Id })
            .SingleOrDefaultAsync(ct);

        if (openPlay is null)
        {
            return OpenPlayRegistrationResult<IReadOnlyList<ClaimStubView>>.Fail(OpenPlayRegistrationFailure.NotFound);
        }

        var stubs = await db.OpenPlayClaimStubs
            .AsNoTracking()
            .Where(stub => stub.OpenPlayId == openPlayId)
            .ToListAsync(ct);

        var views = await ViewsAsync(stubs, ct);

        // The ones to act on first: given and waiting, then claimed, expired, and still to give.
        var order = new Dictionary<string, int>
        {
            [OpenPlayClaimStubStatus.Given] = 0,
            [OpenPlayClaimStubStatus.Claimed] = 1,
            [OpenPlayClaimStubStatus.Expired] = 2,
            [OpenPlayClaimStubStatus.Available] = 3
        };

        return OpenPlayRegistrationResult<IReadOnlyList<ClaimStubView>>.Success(
            [.. views
                .OrderBy(view => order.GetValueOrDefault(view.Status))
                .ThenByDescending(view => view.ClaimedAt ?? view.GivenAt)
                .ThenBy(view => view.Code)]);
    }

    public async Task<OpenPlayRegistrationResult<ClaimStubView>> ClaimAsync(
        AuditActor actor,
        ClaimStubRequest request,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(actor);
        ArgumentNullException.ThrowIfNull(request);

        var code = Normalise(request.Code);

        var stub = code is null
            ? null
            : await db.OpenPlayClaimStubs.SingleOrDefaultAsync(
                candidate => candidate.Code == code && VenuesOf(actor.UserId ?? Guid.Empty).Contains(candidate.FacilityId),
                ct);

        if (stub is null)
        {
            return Fail(OpenPlayRegistrationFailure.ClaimStubNotFound);
        }

        var now = timeProvider.GetUtcNow();

        switch (stub.StatusAt(now))
        {
            case OpenPlayClaimStubStatus.Available:
                return Fail(OpenPlayRegistrationFailure.ClaimStubNotGiven);
            case OpenPlayClaimStubStatus.Claimed:
                return Fail(OpenPlayRegistrationFailure.ClaimStubAlreadyClaimed);
            case OpenPlayClaimStubStatus.Expired:
                return Fail(OpenPlayRegistrationFailure.ClaimStubExpired);
        }

        stub.Claim(actor.UserId!.Value, now);

        audit.RecordEvent(
            actor,
            AuditAction.OpenPlayClaimStubClaimed,
            AuditEntityType.OpenPlay,
            stub.OpenPlayId,
            new Dictionary<string, string?> { ["code"] = stub.Code, ["prize"] = stub.Prize },
            "Prize handed over against the claim stub.");

        await db.SaveChangesAsync(ct);

        return OpenPlayRegistrationResult<ClaimStubView>.Success((await ViewsAsync([stub], ct)).Single());
    }

    /// <summary>"7kq2m9xa", " 7KQ2-M9XA " and "7KQ2 M9XA" all read as "7KQ2-M9XA".</summary>
    internal static string? Normalise(string? code)
    {
        var letters = new string((code ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToUpperInvariant).ToArray());

        return letters.Length == 8 ? $"{letters[..4]}-{letters[4..]}" : null;
    }

    private IQueryable<Guid> VenuesOf(Guid userId) =>
        db.Facilities
            .Where(facility => facility.FacilityOwner.UserId == userId
                || facility.Attendants.Any(attendant => attendant.UserId == userId && attendant.IsActive))
            .Select(facility => facility.Id);

    private async Task<List<ClaimStubView>> ViewsAsync(IReadOnlyCollection<OpenPlayClaimStub> stubs, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();
        var people = stubs.SelectMany(stub => new[] { stub.CustomerUserId, stub.ClaimedByUserId })
            .OfType<Guid>()
            .Distinct()
            .ToList();
        var names = await db.Users
            .AsNoTracking()
            .Where(user => people.Contains(user.Id))
            .ToDictionaryAsync(user => user.Id, user => user.FullName, ct);
        var sessionIds = stubs.Select(stub => stub.SessionId).OfType<Guid>().Distinct().ToList();
        var dates = await db.OpenPlaySessions
            .AsNoTracking()
            .Where(session => sessionIds.Contains(session.Id))
            .ToDictionaryAsync(session => session.Id, session => session.Date, ct);
        var openPlayIds = stubs.Select(stub => stub.OpenPlayId).Distinct().ToList();
        var titles = await db.OpenPlays
            .AsNoTracking()
            .Where(openPlay => openPlayIds.Contains(openPlay.Id))
            .ToDictionaryAsync(openPlay => openPlay.Id, openPlay => openPlay.Title, ct);

        return
        [
            .. stubs.Select(stub => new ClaimStubView(
                stub.Id,
                stub.OpenPlayId,
                titles.GetValueOrDefault(stub.OpenPlayId) ?? "Open play",
                stub.Code,
                stub.Prize,
                stub.StatusAt(now),
                stub.CustomerUserId is Guid player ? names.GetValueOrDefault(player) : null,
                stub.SessionId is Guid session ? dates.GetValueOrDefault(session) : null,
                stub.AssignedAt,
                stub.ExpiresAt,
                stub.ClaimedAt,
                stub.ClaimedByUserId is Guid desk ? names.GetValueOrDefault(desk) : null))
        ];
    }

    private static OpenPlayRegistrationResult<ClaimStubView> Fail(OpenPlayRegistrationFailure failure) =>
        OpenPlayRegistrationResult<ClaimStubView>.Fail(failure);
}
