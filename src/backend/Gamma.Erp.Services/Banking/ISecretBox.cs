namespace Gamma.Erp.Services.Banking;

/// <summary>Seals what must not be readable in the database (a provider's private key, a bank session) with the host's own keys.</summary>
public interface ISecretBox
{
    string Seal(string plain);

    /// <summary>The plain text, or null when the host can no longer open it (its keys were lost): it must then be entered again.</summary>
    string? Open(string sealedText);
}
