namespace IcyPlay.Application.OpenPlays;

/// <summary>One prize of an open play, as a claim stub, for the desk.</summary>
/// <param name="Status">"Available" (not given yet), "Given", "Claimed" or "Expired".</param>
/// <param name="PlayerName">Who it was given to; null until it is.</param>
/// <param name="SessionDate">The session it was won in.</param>
/// <param name="ClaimedByName">Who at the venue marked it claimed.</param>
public sealed record ClaimStubView(
    Guid ClaimStubId,
    Guid OpenPlayId,
    string OpenPlayTitle,
    string Code,
    string Prize,
    string Status,
    string? PlayerName,
    DateOnly? SessionDate,
    DateTimeOffset? GivenAt,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? ClaimedAt,
    string? ClaimedByName);

/// <summary>The code on the stub the player shows, typed or scanned: "7KQ2-M9XA", dash or not.</summary>
public sealed record ClaimStubRequest(string? Code);

/// <summary>A reward a session gave one of its best players, on the matches board.</summary>
/// <param name="Kind">"Voucher" or "ClaimStub".</param>
/// <param name="Label">"₱50 off an open play", "20% off an open play, up to ₱100", or the prize.</param>
/// <param name="Code">A claim stub's code; null for a voucher.</param>
/// <param name="Status">A voucher's "Available", "Reserved" or "Used"; a stub's "Given", "Claimed" or "Expired".</param>
public sealed record MatchReward(
    Guid ParticipantId,
    string FullName,
    string Kind,
    string Label,
    string? Code,
    DateTimeOffset ExpiresAt,
    string Status);

/// <summary>A player's vouchers and claim stubs, those to use first at the top.</summary>
public sealed record MyRewards(IReadOnlyList<MyVoucher> Vouchers, IReadOnlyList<MyClaimStub> ClaimStubs);

/// <summary>A best player's voucher, as the player sees it.</summary>
/// <param name="Label">"₱50 off an open play", "20% off an open play, up to ₱100".</param>
/// <param name="Status">"Available", "Reserved" (held for a registration being paid), "Used" or "Expired".</param>
/// <param name="WonAt">The open play it was won at.</param>
public sealed record MyVoucher(
    Guid VoucherId,
    Guid FacilityId,
    string FacilityName,
    string Label,
    // "Fixed" or "Percentage", its value, and a percentage voucher's most off: for checkout to show what it takes.
    string DiscountKind,
    decimal DiscountValue,
    decimal? MaxDiscount,
    string Status,
    string WonAt,
    DateOnly WonOn,
    DateTimeOffset GivenAt,
    DateTimeOffset ExpiresAt,
    DateTimeOffset? UsedAt);

/// <summary>A best player's claim stub, as the player sees it: what to show at the desk.</summary>
/// <param name="Status">"Given" (to collect), "Claimed" or "Expired".</param>
public sealed record MyClaimStub(
    Guid ClaimStubId,
    string FacilityName,
    string WonAt,
    DateOnly? WonOn,
    string Prize,
    string Code,
    string Status,
    DateTimeOffset? ExpiresAt,
    DateTimeOffset? ClaimedAt);
