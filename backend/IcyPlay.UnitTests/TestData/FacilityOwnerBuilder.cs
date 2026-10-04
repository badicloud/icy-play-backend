using IcyPlay.Domain.Identity;

namespace IcyPlay.UnitTests.TestData;

public sealed class FacilityOwnerBuilder
{
    private string? _checkInCode;

    /// <summary>A venue whose owner has set this open play check-in code.</summary>
    public FacilityOwnerBuilder WithCheckInCode(string code)
    {
        _checkInCode = code;
        return this;
    }

    public FacilityOwner Build()
    {
        var owner = new FacilityOwner(TestIds.FacilityOwnerUserId, "Dink and Cue", "billing@example.com", null);

        if (_checkInCode is not null)
        {
            owner.SetOpenPlayCheckInCode(_checkInCode, TestTimes.UtcNow);
        }

        return owner;
    }
}
