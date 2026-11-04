using System;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Security;

namespace ObjectExplorerForMySql.Core.ObjectProperties;

/// <summary>Properties of a user or role (US-1002). Values the server does not have stay empty.</summary>
public sealed class AccountProperties : ServerObjectPropertiesBase
{
    private readonly AccountInfo _account;

    /// <summary>Initializes a new instance of the <see cref="AccountProperties"/> class.</summary>
    public AccountProperties(AccountInfo account, ConnectionDefinition? connection)
        : base(connection)
    {
        _account = account ?? throw new ArgumentNullException(nameof(account));
    }

    /// <summary>Gets the user name (role name).</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropUserName")]
    public string UserName => _account.User;

    /// <summary>Gets the host part of the account.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropHost")]
    public string Host => _account.Host;

    /// <summary>Gets the authentication plugin.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropAuthenticationPlugin")]
    public string AuthenticationPlugin => _account.Plugin ?? string.Empty;

    /// <summary>Gets a value indicating whether the account is locked.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropAccountLocked")]
    public bool? AccountLocked => _account.IsLocked;

    /// <summary>Gets a value indicating whether the password is expired.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropPasswordExpired")]
    public bool? PasswordExpired => _account.PasswordExpired;

    /// <summary>Gets when the password was last changed.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropPasswordLastChanged")]
    public DateTime? PasswordLastChanged => _account.PasswordLastChanged;

    /// <summary>Gets the password lifetime in days; empty means the server default.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropPasswordLifetime")]
    public int? PasswordLifetime => _account.PasswordLifetime;

    /// <inheritdoc />
    protected override string TypeKey => _account.IsRole ? "TypeRole" : "TypeUser";

    /// <inheritdoc />
    public override string GetObjectName() => _account.Label;
}
