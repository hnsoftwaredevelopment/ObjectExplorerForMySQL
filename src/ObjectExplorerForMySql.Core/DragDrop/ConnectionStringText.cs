using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ObjectExplorerForMySql.Core.Connections;

namespace ObjectExplorerForMySql.Core.DragDrop;

/// <summary>How a dragged connection string is written into a file (US-0505).</summary>
public enum ConnectionStringTarget
{
    /// <summary>The plain string.</summary>
    Plain,

    /// <summary>A ConnectionStrings entry of appsettings.json: <c>"Local": "Server=...;"</c>.</summary>
    Json,

    /// <summary>A C# string literal.</summary>
    CSharp,

    /// <summary>A Visual Basic string literal.</summary>
    VisualBasic,

    /// <summary>An <c>&lt;add /&gt;</c> element of the connectionStrings section of app.config or web.config.</summary>
    Config,
}

/// <summary>Builds the connection string for an application (US-0505). The password is never in it.</summary>
public static class ConnectionStringText
{
    /// <summary>Text in place of the password.</summary>
    public const string PasswordPlaceholder = "<password>";

    /// <summary>
    /// Returns <c>Server=db.local;Port=3306;Database=shop;User ID=app;Password=&lt;password&gt;;</c> with the SSL mode
    /// when it is not Preferred (the MySqlConnector default) and AllowPublicKeyRetrieval when it is on.
    /// </summary>
    /// <param name="definition">The connection.</param>
    /// <param name="database">The database; null uses the default database of the connection (left out when empty).</param>
    public static string Build(ConnectionDefinition definition, string? database = null)
    {
        if (definition is null)
        {
            throw new ArgumentNullException(nameof(definition));
        }

        var parts = new List<string>
        {
            Part("Server", definition.Host.Trim()),
            Part("Port", definition.Port.ToString(CultureInfo.InvariantCulture)),
        };

        var db = string.IsNullOrWhiteSpace(database) ? definition.DefaultDatabase?.Trim() : database!.Trim();
        if (!string.IsNullOrEmpty(db))
        {
            parts.Add(Part("Database", db!));
        }

        parts.Add(Part("User ID", definition.UserName.Trim()));
        parts.Add("Password=" + PasswordPlaceholder);
        if (definition.SslMode != SslMode.Preferred)
        {
            parts.Add(Part("SslMode", definition.SslMode.ToString()));
        }

        if (definition.AllowPublicKeyRetrieval)
        {
            parts.Add("AllowPublicKeyRetrieval=true");
        }

        return string.Join(";", parts) + ";";
    }

    /// <summary>Returns the target for a file: .json, .cs, .vb, .config; anything else (or no file) is plain.</summary>
    public static ConnectionStringTarget TargetFor(string? filePath) => Path.GetExtension(filePath ?? string.Empty).ToUpperInvariant() switch
    {
        ".JSON" => ConnectionStringTarget.Json,
        ".CS" => ConnectionStringTarget.CSharp,
        ".VB" => ConnectionStringTarget.VisualBasic,
        ".CONFIG" => ConnectionStringTarget.Config,
        _ => ConnectionStringTarget.Plain,
    };

    /// <summary>Writes <paramref name="connectionString"/> for the target; <paramref name="name"/> is the entry name (the connection label).</summary>
    public static string Format(string connectionString, string name, ConnectionStringTarget target)
    {
        var value = connectionString ?? string.Empty;
        var key = string.IsNullOrWhiteSpace(name) ? "MySql" : name.Trim();
        return target switch
        {
            ConnectionStringTarget.Json => $"{JsonString(key)}: {JsonString(value)}",
            ConnectionStringTarget.CSharp => CSharpString(value),
            ConnectionStringTarget.VisualBasic => "\"" + value.Replace("\"", "\"\"") + "\"",
            ConnectionStringTarget.Config => $"<add name=\"{XmlAttribute(key)}\" connectionString=\"{XmlAttribute(value)}\" providerName=\"MySqlConnector\" />",
            _ => value,
        };
    }

    // A value with ; = quotes or spaces at the ends is put between double quotes, with inner quotes doubled.
    private static string Part(string key, string value)
    {
        var needsQuotes = value.IndexOfAny([';', '=', '"', '\'']) >= 0 || value.Trim().Length != value.Length;
        return key + "=" + (needsQuotes ? "\"" + value.Replace("\"", "\"\"") + "\"" : value);
    }

    private static string JsonString(string value)
    {
        var builder = new StringBuilder("\"");
        foreach (var c in value)
        {
            builder.Append(c switch
            {
                '"' => "\\\"",
                '\\' => "\\\\",
                '\n' => "\\n",
                '\r' => "\\r",
                '\t' => "\\t",
                < ' ' => "\\u" + ((int)c).ToString("x4", CultureInfo.InvariantCulture),
                _ => c.ToString(),
            });
        }

        return builder.Append('"').ToString();
    }

    private static string CSharpString(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    private static string XmlAttribute(string value) =>
        value.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;").Replace("\"", "&quot;");
}
