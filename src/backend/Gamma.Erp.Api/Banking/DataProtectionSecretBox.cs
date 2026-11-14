using System.Security.Cryptography;
using Gamma.Erp.Services.Banking;
using Microsoft.AspNetCore.DataProtection;

namespace Gamma.Erp.Api.Banking;

/// <summary>
/// Seals bank secrets with the host's Data Protection keys (kept in DataProtection:KeysFolder, the same keys as the sign-in cookies). If the host loses
/// its keys, a sealed secret can no longer be opened and must be entered again.
/// </summary>
public sealed class DataProtectionSecretBox(IDataProtectionProvider provider) : ISecretBox
{
    private readonly IDataProtector protector = provider.CreateProtector("Gamma.Erp.Banking.Secrets.v1");

    public string Seal(string plain) => protector.Protect(plain);

    public string? Open(string sealedText)
    {
        try
        {
            return protector.Unprotect(sealedText);
        }
        catch (CryptographicException)
        {
            return null;
        }
    }
}
