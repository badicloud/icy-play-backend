namespace IcyPlay.Application.OpenPlays;

/// <summary>
/// A player's rewards for being a best player: vouchers off their next open
/// play at a venue, and claim stubs for prizes at the desk. Their own only.
/// </summary>
public interface IMyRewardService
{
    Task<MyRewards> ListAsync(Guid customerUserId, CancellationToken cancellationToken);
}
