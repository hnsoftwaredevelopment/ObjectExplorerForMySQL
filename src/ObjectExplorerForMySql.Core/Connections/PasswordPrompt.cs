using System.Threading;
using System.Threading.Tasks;

namespace ObjectExplorerForMySql.Core.Connections;

/// <summary>Answer of the password prompt.</summary>
public sealed class PasswordPromptResult
{
    /// <summary>Initializes a new instance of the <see cref="PasswordPromptResult"/> class.</summary>
    public PasswordPromptResult(string password, bool savePassword)
    {
        Password = password ?? string.Empty;
        SavePassword = savePassword;
    }

    /// <summary>Gets the password typed by the user (may be empty for accounts without a password).</summary>
    public string Password { get; }

    /// <summary>Gets a value indicating whether the password must be saved.</summary>
    public bool SavePassword { get; }
}

/// <summary>Asks the user for the password of a connection that has none (not saved, or not decryptable).</summary>
public interface IPasswordPrompt
{
    /// <summary>Shows the prompt. Returns null when the user cancels.</summary>
    Task<PasswordPromptResult?> PromptAsync(ConnectionDefinition definition, CancellationToken cancellationToken);
}
