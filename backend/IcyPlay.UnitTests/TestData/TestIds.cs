using System.Security.Cryptography;
using System.Text;

namespace IcyPlay.UnitTests.TestData;

public static class TestIds
{
    public static Guid CustomerUserId => For("customer-user");
    public static Guid FacilityOwnerUserId => For("facility-owner-user");
    public static Guid FacilityId => For("facility");
    public static Guid CourtId => For("court");
    public static Guid BookingId => For("booking");

    public static Guid For(string scope, int sequence = 1)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(scope);

        var input = Encoding.UTF8.GetBytes($"icyplay-tests:{scope}:{sequence}");
        var hash = SHA256.HashData(input);
        Span<byte> guidBytes = stackalloc byte[16];
        hash.AsSpan(0, 16).CopyTo(guidBytes);

        // Mark the deterministic value as RFC 4122 variant and name-based version 5.
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes);
    }
}

