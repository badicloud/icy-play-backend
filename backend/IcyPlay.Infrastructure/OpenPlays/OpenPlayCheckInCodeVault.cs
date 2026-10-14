using System.Security.Cryptography;
using IcyPlay.Application.OpenPlays;
using Microsoft.AspNetCore.DataProtection;

namespace IcyPlay.Infrastructure.OpenPlays;

/// <summary>
/// ASP.NET Core Data Protection, under a purpose of its own so nothing else
/// the server protects can be read back as a check-in code, or the other way
/// round. The keys are the ones the API keeps on disk; a server that loses
/// them can no longer show old codes, and says so rather than failing.
/// </summary>
public sealed class OpenPlayCheckInCodeVault(IDataProtectionProvider provider) : IOpenPlayCheckInCodeVault
{
    private const string Purpose = "IcyPlay.OpenPlayCheckInCode.v1";

    private readonly IDataProtector protector = provider.CreateProtector(Purpose);

    public string Protect(string code) => protector.Protect(code);

    public string? Reveal(string protectedCode)
    {
        try
        {
            return protector.Unprotect(protectedCode);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
