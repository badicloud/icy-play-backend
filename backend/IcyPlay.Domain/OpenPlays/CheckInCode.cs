using System.Security.Cryptography;
using System.Text;

namespace IcyPlay.Domain.OpenPlays;

/// <summary>
/// The venue's code for checking an open play player in by hand: six digits
/// the owner sets and tells their attendants, asked for whenever somebody at
/// the desk checks a player in, or takes a check-in back, without scanning
/// the player's QR.
///
/// Kept only as a salted PBKDF2 hash, so it cannot be read back off the
/// database. Six digits is a small space, so what really guards it is the
/// lock: <see cref="MaxFailures"/> wrong tries shut it for
/// <see cref="LockMinutes"/> minutes.
/// </summary>
public static class CheckInCode
{
    public const int Length = 6;
    public const int MaxFailures = 5;
    public const int LockMinutes = 5;

    private const int Iterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;
    private const string Version = "v1";

    /// <summary>
    /// A random six-digit code, never one a person would guess first: no digit
    /// repeated all the way through, and no straight run up or down.
    /// </summary>
    public static string Generate()
    {
        while (true)
        {
            var code = RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6", System.Globalization.CultureInfo.InvariantCulture);

            if (!IsObvious(code))
            {
                return code;
            }
        }
    }

    /// <summary>000000, 777777, 123456, 987654 and the like.</summary>
    public static bool IsObvious(string code)
    {
        var steps = code.Zip(code.Skip(1), (a, b) => b - a).Distinct().ToArray();

        return steps.Length == 1 && steps[0] is 0 or 1 or -1;
    }

    /// <summary>Exactly six digits, nothing else.</summary>
    public static bool IsWellFormed(string? code) =>
        code is { Length: Length } && code.All(char.IsAsciiDigit);

    public static string Hash(string code)
    {
        if (!IsWellFormed(code))
        {
            throw new ArgumentException($"A check-in code is {Length} digits.", nameof(code));
        }

        var salt = RandomNumberGenerator.GetBytes(SaltBytes);
        var hash = Derive(code, salt, Iterations);

        return $"{Version}.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Matches(string? code, string stored)
    {
        if (!IsWellFormed(code))
        {
            return false;
        }

        var parts = stored.Split('.');

        if (parts.Length != 4 || parts[0] != Version || !int.TryParse(parts[1], out var iterations))
        {
            return false;
        }

        var expected = Convert.FromBase64String(parts[3]);
        var actual = Derive(code!, Convert.FromBase64String(parts[2]), iterations);

        return CryptographicOperations.FixedTimeEquals(expected, actual);
    }

    private static byte[] Derive(string code, byte[] salt, int iterations) =>
        Rfc2898DeriveBytes.Pbkdf2(Encoding.UTF8.GetBytes(code), salt, iterations, HashAlgorithmName.SHA256, HashBytes);
}

/// <summary>What a typed check-in code came to.</summary>
public enum CheckInCodeVerdict
{
    Accepted = 0,
    // The venue has not set one yet: nobody can check in by hand.
    NotSet,
    Wrong,
    // Too many wrong tries: shut for a few minutes.
    Locked
}
