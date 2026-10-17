using IcyPlay.Application.OpenPlays;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>A player's vouchers and claim stubs, for their rewards page.</summary>
public sealed class MyRewardService(AppDbContext db, TimeProvider timeProvider) : IMyRewardService
{
    public async Task<MyRewards> ListAsync(Guid customerUserId, CancellationToken ct)
    {
        var now = timeProvider.GetUtcNow();

        // A voucher held for a registration that never went through is theirs again.
        await OpenPlayVoucherLedger.SettleAsync(db, customerUserId, now, ct);
        await db.SaveChangesAsync(ct);

        // A voucher taken back — its session was reopened — was never really theirs.
        var vouchers = await db.OpenPlayVouchers
            .AsNoTracking()
            .Where(voucher => voucher.CustomerUserId == customerUserId && voucher.Status != OpenPlayVoucherStatus.Revoked)
            .ToListAsync(ct);

        var stubs = await db.OpenPlayClaimStubs
            .AsNoTracking()
            .Where(stub => stub.CustomerUserId == customerUserId)
            .ToListAsync(ct);

        var facilityIds = vouchers.Select(voucher => voucher.FacilityId).Concat(stubs.Select(stub => stub.FacilityId)).Distinct().ToList();
        var venues = await db.Facilities
            .AsNoTracking()
            .Where(facility => facilityIds.Contains(facility.Id))
            .ToDictionaryAsync(facility => facility.Id, facility => facility.Name, ct);

        var openPlayIds = vouchers.Select(voucher => voucher.OpenPlayId).Concat(stubs.Select(stub => stub.OpenPlayId)).Distinct().ToList();
        var titles = await db.OpenPlays
            .AsNoTracking()
            .Where(openPlay => openPlayIds.Contains(openPlay.Id))
            .ToDictionaryAsync(openPlay => openPlay.Id, openPlay => openPlay.Title, ct);

        var sessionIds = vouchers.Select(voucher => voucher.SessionId).Concat(stubs.Select(stub => stub.SessionId).OfType<Guid>()).Distinct().ToList();
        var dates = await db.OpenPlaySessions
            .AsNoTracking()
            .Where(session => sessionIds.Contains(session.Id))
            .ToDictionaryAsync(session => session.Id, session => session.Date, ct);

        string VoucherStatus(OpenPlayVoucher voucher) =>
            voucher.Status == OpenPlayVoucherStatus.Available && now >= voucher.ExpiresAt ? "Expired" : voucher.Status;

        // Those to use first: usable ones, soonest to expire; then the rest, newest first.
        var myVouchers = vouchers
            .Select(voucher => new MyVoucher(
                voucher.Id,
                voucher.FacilityId,
                venues.GetValueOrDefault(voucher.FacilityId) ?? "A venue",
                DeskMatchService.VoucherLabel(voucher.DiscountKind, voucher.DiscountValue, voucher.MaxDiscount),
                voucher.DiscountKind,
                voucher.DiscountValue,
                voucher.MaxDiscount,
                VoucherStatus(voucher),
                titles.GetValueOrDefault(voucher.OpenPlayId) ?? "Open play",
                dates.GetValueOrDefault(voucher.SessionId),
                voucher.CreatedAt,
                voucher.ExpiresAt,
                voucher.UsedAt))
            .OrderBy(voucher => voucher.Status == OpenPlayVoucherStatus.Available ? 0 : 1)
            .ThenBy(voucher => voucher.Status == OpenPlayVoucherStatus.Available ? voucher.ExpiresAt : DateTimeOffset.MaxValue)
            .ThenByDescending(voucher => voucher.GivenAt)
            .ToList();

        var myStubs = stubs
            .Select(stub => new MyClaimStub(
                stub.Id,
                venues.GetValueOrDefault(stub.FacilityId) ?? "A venue",
                titles.GetValueOrDefault(stub.OpenPlayId) ?? "Open play",
                stub.SessionId is Guid session ? dates.GetValueOrDefault(session) : null,
                stub.Prize,
                stub.Code,
                stub.StatusAt(now),
                stub.ExpiresAt,
                stub.ClaimedAt))
            .OrderBy(stub => stub.Status == OpenPlayClaimStubStatus.Given ? 0 : 1)
            .ThenByDescending(stub => stub.ClaimedAt ?? stub.ExpiresAt)
            .ToList();

        return new MyRewards(myVouchers, myStubs);
    }
}
