using System;
using System.Collections.Generic;

namespace ObjectExplorerForMySql.Core.Connections;

/// <summary>SSL mode of a connection; mirrors the MySqlConnector values.</summary>
public enum SslMode
{
    /// <summary>Do not use SSL.</summary>
    None,

    /// <summary>Use SSL when the server supports it (MySQL default).</summary>
    Preferred,

    /// <summary>Always use SSL; do not check the certificate.</summary>
    Required,

    /// <summary>Always use SSL and check that the certificate is issued by a trusted authority.</summary>
    VerifyCA,

    /// <summary>Like <see cref="VerifyCA"/>, and the certificate must match the host name.</summary>
    VerifyFull,
}

/// <summary>Reasons why a <see cref="ConnectionDefinition"/> is not valid.</summary>
public enum ConnectionValidationError
{
    /// <summary>Host is empty.</summary>
    HostRequired,

    /// <summary>Port is not a number between 1 and 65535.</summary>
    PortInvalid,

    /// <summary>User name is empty.</summary>
    UserRequired,

    /// <summary>Connection timeout is not a number between 1 and 300 seconds.</summary>
    TimeoutInvalid,
}

/// <summary>
/// Everything needed to connect to one MySQL server, as entered by the user.
/// </summary>
public sealed class ConnectionDefinition
{
    /// <summary>Default MySQL port.</summary>
    public const int DefaultPort = 3306;

    /// <summary>Default connection timeout in seconds.</summary>
    public const int DefaultTimeoutSeconds = 15;

    /// <summary>Highest accepted connection timeout in seconds.</summary>
    public const int MaximumTimeoutSeconds = 300;

    /// <summary>Gets or sets the stable identity of this connection.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Gets or sets the optional name shown in the tree.</summary>
    public string? DisplayName { get; set; }

    /// <summary>Gets or sets the server host name or IP address.</summary>
    public string Host { get; set; } = string.Empty;

    /// <summary>Gets or sets the TCP port.</summary>
    public int Port { get; set; } = DefaultPort;

    /// <summary>Gets or sets the MySQL user name.</summary>
    public string UserName { get; set; } = string.Empty;

    /// <summary>Gets or sets the password. Kept in memory only; storage is handled separately (ADR-004).</summary>
    public string? Password { get; set; }

    /// <summary>Gets or sets a value indicating whether the password may be stored.</summary>
    public bool SavePassword { get; set; } = true;

    /// <summary>Gets or sets the optional database selected after connecting.</summary>
    public string? DefaultDatabase { get; set; }

    /// <summary>Gets or sets the SSL mode.</summary>
    public SslMode SslMode { get; set; } = SslMode.Preferred;

    /// <summary>Gets or sets the connection timeout in seconds.</summary>
    public int ConnectionTimeoutSeconds { get; set; } = DefaultTimeoutSeconds;

    /// <summary>
    /// Gets or sets a value indicating whether the client may ask the server for its RSA public key.
    /// Needed for <c>caching_sha2_password</c> accounts without SSL; it allows a man-in-the-middle to read the password.
    /// </summary>
    public bool AllowPublicKeyRetrieval { get; set; }

    /// <summary>
    /// Gets the text shown in the tree: the connection name (<see cref="DisplayName"/>), or
    /// <c>host:port (user)</c> / <c>host:port/database (user)</c> when no name is set.
    /// </summary>
    public string Label
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(DisplayName))
            {
                return DisplayName!.Trim();
            }

            var database = string.IsNullOrWhiteSpace(DefaultDatabase) ? string.Empty : "/" + DefaultDatabase!.Trim();
            return $"{Host}:{Port}{database} ({UserName})";
        }
    }

    /// <summary>Returns every problem with this definition; an empty list means it is valid.</summary>
    public IReadOnlyList<ConnectionValidationError> Validate()
    {
        var errors = new List<ConnectionValidationError>();

        if (string.IsNullOrWhiteSpace(Host))
        {
            errors.Add(ConnectionValidationError.HostRequired);
        }

        if (Port < 1 || Port > 65535)
        {
            errors.Add(ConnectionValidationError.PortInvalid);
        }

        if (string.IsNullOrWhiteSpace(UserName))
        {
            errors.Add(ConnectionValidationError.UserRequired);
        }

        if (ConnectionTimeoutSeconds < 1 || ConnectionTimeoutSeconds > MaximumTimeoutSeconds)
        {
            errors.Add(ConnectionValidationError.TimeoutInvalid);
        }

        return errors;
    }
}
