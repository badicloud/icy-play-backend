using System.Buffers.Text;
using System.Security.Cryptography;

namespace IcyPlay.Domain.OpenPlays;

/// <summary>
/// What a registration's check-in QR code holds: a marker and a random token,
/// never the account's id or email, so a picture of it says nothing about
/// whose it is and the next one cannot be guessed from it.
///
/// One per registration, made when the desk confirms the payment. It gets its
/// holder into that one session and no other, and once they are checked in it
/// is spent: a copy that has been shared is worth nothing after the door.
/// </summary>
public static class CheckInPass
{
    /// <summary>How the token is marked inside the QR, so a scan of anything else is told apart at once.</summary>
    public const string QrPrefix = "icyplay-checkin:";

    /// <summary>The longest token the column takes.</summary>
    public const int TokenLimit = 64;

    /// <summary>32 random bytes, 43 characters of base64url.</summary>
    public static string NewToken() => Base64Url.EncodeToString(RandomNumberGenerator.GetBytes(32));

    public static string QrContent(string token) => QrPrefix + token;

    /// <summary>
    /// The token in a scanned QR, or null when what was scanned is not one of
    /// ours: a payment QR, a menu, a URL.
    /// </summary>
    public static string? TokenFrom(string? scanned)
    {
        if (string.IsNullOrWhiteSpace(scanned))
        {
            return null;
        }

        var text = scanned.Trim();

        return text.StartsWith(QrPrefix, StringComparison.Ordinal)
            && text.Length > QrPrefix.Length
            && text.Length <= QrPrefix.Length + TokenLimit
                ? text[QrPrefix.Length..]
                : null;
    }
}

/// <summary>Where a registration's check-in QR stands, as its player sees it.</summary>
public static class CheckInPassState
{
    // Confirmed, the session not over, not yet checked in: the QR works.
    public const string Active = "Active";
    // Checked in: the QR has been spent.
    public const string Used = "Used";
    // The session ended without a check-in.
    public const string Expired = "Expired";
}
