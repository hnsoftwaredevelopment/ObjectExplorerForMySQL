using System;
using System.Security.Cryptography;
using System.Text;

namespace ObjectExplorerForMySql.Core.Security;

/// <summary>Encrypts secrets such as passwords before they are written to disk.</summary>
public interface ISecretProtector
{
    /// <summary>Encrypts <paramref name="secret"/> and returns Base64 text.</summary>
    string Protect(string secret);

    /// <summary>
    /// Decrypts text made by <see cref="Protect"/>. Returns null when it cannot be decrypted,
    /// for example after copying the file to another Windows user or computer.
    /// </summary>
    string? Unprotect(string protectedSecret);
}

/// <summary>
/// <see cref="ISecretProtector"/> using Windows DPAPI with scope CurrentUser (ADR-004):
/// only the same Windows user on the same computer can decrypt.
/// </summary>
public sealed class DpapiSecretProtector : ISecretProtector
{
    // Extra entropy so other applications of the same user cannot decrypt the values by accident.
    // Changing it makes every saved password unreadable.
    private static readonly byte[] _entropy = Encoding.UTF8.GetBytes("ObjectExplorerForMySql.Connections.v1");

    /// <inheritdoc />
    public string Protect(string secret)
    {
        if (secret is null)
        {
            throw new ArgumentNullException(nameof(secret));
        }

        var encrypted = ProtectedData.Protect(Encoding.UTF8.GetBytes(secret), _entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(encrypted);
    }

    /// <inheritdoc />
    public string? Unprotect(string protectedSecret)
    {
        if (string.IsNullOrEmpty(protectedSecret))
        {
            return null;
        }

        try
        {
            var decrypted = ProtectedData.Unprotect(Convert.FromBase64String(protectedSecret), _entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(decrypted);
        }
        catch (CryptographicException)
        {
            return null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
