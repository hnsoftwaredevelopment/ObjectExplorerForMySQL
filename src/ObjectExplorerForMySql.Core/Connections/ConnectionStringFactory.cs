using System;
using MySqlConnector;

namespace ObjectExplorerForMySql.Core.Connections;

/// <summary>Builds MySqlConnector connection strings from a <see cref="ConnectionDefinition"/>.</summary>
public static class ConnectionStringFactory
{
    /// <summary>Application name reported to the server (visible in <c>SHOW PROCESSLIST</c> via connection attributes).</summary>
    public const string ApplicationName = "Object Explorer for MySQL";

    /// <summary>Builds the connection string. Never log the result unmasked.</summary>
    /// <param name="definition">The connection to build a string for.</param>
    /// <param name="pooling">False for one-off connections such as "Test connection".</param>
    public static string Build(ConnectionDefinition definition, bool pooling = true)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        var builder = new MySqlConnectionStringBuilder
        {
            Server = definition.Host.Trim(),
            Port = (uint)definition.Port,
            UserID = definition.UserName.Trim(),
            Password = definition.Password ?? string.Empty,
            SslMode = ToMySqlSslMode(definition.SslMode),
            ConnectionTimeout = (uint)definition.ConnectionTimeoutSeconds,
            AllowPublicKeyRetrieval = definition.AllowPublicKeyRetrieval,
            Pooling = pooling,
            ApplicationName = ApplicationName,
        };

        if (!string.IsNullOrWhiteSpace(definition.DefaultDatabase))
        {
            builder.Database = definition.DefaultDatabase!.Trim();
        }

        return builder.ConnectionString;
    }

    /// <summary>Maps the extension's SSL mode to the MySqlConnector value.</summary>
    internal static MySqlSslMode ToMySqlSslMode(SslMode mode) => mode switch
    {
        SslMode.None => MySqlSslMode.None,
        SslMode.Preferred => MySqlSslMode.Preferred,
        SslMode.Required => MySqlSslMode.Required,
        SslMode.VerifyCA => MySqlSslMode.VerifyCA,
        SslMode.VerifyFull => MySqlSslMode.VerifyFull,
        _ => throw new ArgumentOutOfRangeException(nameof(mode), mode, "Unknown SSL mode."),
    };
}
