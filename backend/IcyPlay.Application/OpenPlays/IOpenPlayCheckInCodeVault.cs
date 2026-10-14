namespace IcyPlay.Application.OpenPlays;

/// <summary>
/// Keeps a readable copy of the venue's open play check-in code, so the owner
/// and the attendants can look it up again instead of generating a new one
/// and telling everybody. Encrypted with a key only this server holds — never
/// stored as it is. Checking a typed code still goes by its hash.
/// </summary>
public interface IOpenPlayCheckInCodeVault
{
    /// <summary>The code, encrypted for keeping.</summary>
    string Protect(string code);

    /// <summary>
    /// The code back, or null when it cannot be read: the server's key has
    /// changed since it was kept, or the copy has been tampered with.
    /// </summary>
    string? Reveal(string protectedCode);
}
