using IcyPlay.Domain.Bookings;
using IcyPlay.Domain.OpenPlays;
using IcyPlay.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>
/// Keeps best player vouchers in step with the registrations they were held
/// for: spent once the registration goes through, back to the player when it
/// does not. A hold runs out without anything happening, so this is also done
/// whenever the player's vouchers are next looked at. Saves nothing.
/// </summary>
internal static class OpenPlayVoucherLedger
{
    /// <summary>Settles the voucher held for this registration, if there is one.</summary>
    public static async Task SettleAsync(AppDbContext db, OpenPlayRegistration registration, DateTimeOffset now, CancellationToken ct)
    {
        if (registration.VoucherId is not Guid voucherId)
        {
            return;
        }

        var voucher = await db.OpenPlayVouchers.SingleOrDefaultAsync(candidate => candidate.Id == voucherId, ct);

        if (voucher is not null)
        {
            Settle(voucher, registration, now);
        }
    }

    /// <summary>Settles every voucher the player has held for a registration.</summary>
    public static async Task SettleAsync(AppDbContext db, Guid customerUserId, DateTimeOffset now, CancellationToken ct)
    {
        var held = await db.OpenPlayVouchers
            .Where(voucher => voucher.CustomerUserId == customerUserId && voucher.Status == OpenPlayVoucherStatus.Reserved)
            .ToListAsync(ct);

        if (held.Count == 0)
        {
            return;
        }

        var ids = held.Select(voucher => voucher.RegistrationId).OfType<Guid>().ToList();
        var registrations = await db.OpenPlayRegistrations
            .Where(registration => ids.Contains(registration.Id))
            .ToDictionaryAsync(registration => registration.Id, ct);

        foreach (var voucher in held)
        {
            if (voucher.RegistrationId is Guid id && registrations.TryGetValue(id, out var registration))
            {
                Settle(voucher, registration, now);
            }
            else
            {
                voucher.Release(now);
            }
        }
    }

    /// <summary>
    /// The player's vouchers they can use at this venue now: the one that takes
    /// the most off this fee first, and of those, the one that runs out soonest.
    /// </summary>
    public static async Task<List<OpenPlayVoucher>> UsableAsync(
        AppDbContext db,
        Guid customerUserId,
        Guid facilityId,
        decimal fee,
        DateTimeOffset now,
        CancellationToken ct)
    {
        var vouchers = await db.OpenPlayVouchers
            .Where(voucher => voucher.CustomerUserId == customerUserId
                && voucher.FacilityId == facilityId
                && voucher.Status == OpenPlayVoucherStatus.Available
                && voucher.ExpiresAt > now)
            .ToListAsync(ct);

        return [.. vouchers.OrderByDescending(voucher => voucher.DiscountOn(fee)).ThenBy(voucher => voucher.ExpiresAt)];
    }

    private static void Settle(OpenPlayVoucher voucher, OpenPlayRegistration registration, DateTimeOffset now)
    {
        if (voucher.Status != OpenPlayVoucherStatus.Reserved || voucher.RegistrationId != registration.Id)
        {
            return;
        }

        if (registration.Status == BookingStatus.Confirmed)
        {
            voucher.MarkUsed(registration.ConfirmedAt ?? now, now);
        }
        else if (!registration.HoldsSpotAt(now))
        {
            voucher.Release(now);
        }
    }
}
