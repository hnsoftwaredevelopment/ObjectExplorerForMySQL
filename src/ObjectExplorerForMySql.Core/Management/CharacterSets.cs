using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MySqlConnector;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Query;

namespace ObjectExplorerForMySql.Core.Management;

/// <summary>A character set of the server.</summary>
public sealed record CharacterSetInfo(string Name, string Description, string DefaultCollation)
{
    /// <summary>Gets the text in the list: <c>utf8mb4 - UTF-8 Unicode</c>.</summary>
    public string Display => string.IsNullOrEmpty(Description) ? Name : $"{Name} - {Description}";
}

/// <summary>A collation and its character set.</summary>
public sealed record CollationInfo(string Name, string CharacterSet);

/// <summary>Character sets, collations and server defaults for New Database (US-0903).</summary>
public sealed class CharacterSetCatalog
{
    /// <summary>Initializes a new instance of the <see cref="CharacterSetCatalog"/> class.</summary>
    public CharacterSetCatalog(
        IReadOnlyList<CharacterSetInfo> characterSets,
        IReadOnlyList<CollationInfo> collations,
        string? serverCharacterSet,
        string? serverCollation,
        IReadOnlyList<string> existingDatabases)
    {
        CharacterSets = (characterSets ?? throw new ArgumentNullException(nameof(characterSets))).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        Collations = (collations ?? throw new ArgumentNullException(nameof(collations))).OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).ToList();
        ServerCharacterSet = serverCharacterSet;
        ServerCollation = serverCollation;
        ExistingDatabases = existingDatabases ?? throw new ArgumentNullException(nameof(existingDatabases));
    }

    /// <summary>Gets the character sets, sorted by name.</summary>
    public IReadOnlyList<CharacterSetInfo> CharacterSets { get; }

    /// <summary>Gets the collations, sorted by name.</summary>
    public IReadOnlyList<CollationInfo> Collations { get; }

    /// <summary>Gets <c>@@character_set_server</c>.</summary>
    public string? ServerCharacterSet { get; }

    /// <summary>Gets <c>@@collation_server</c>.</summary>
    public string? ServerCollation { get; }

    /// <summary>Gets the names of the databases that exist, for the "already exists" check.</summary>
    public IReadOnlyList<string> ExistingDatabases { get; }

    /// <summary>Returns the collations of <paramref name="characterSet"/>.</summary>
    public IReadOnlyList<CollationInfo> CollationsOf(string? characterSet) =>
        Collations.Where(c => string.Equals(c.CharacterSet, characterSet, StringComparison.OrdinalIgnoreCase)).ToList();
}

/// <summary>Reads the <see cref="CharacterSetCatalog"/> of a server.</summary>
public interface ICharacterSetSource
{
    /// <summary>Reads character sets, collations, server defaults and database names.</summary>
    Task<CharacterSetCatalog> GetAsync(ConnectionDefinition definition, CancellationToken cancellationToken);
}

/// <summary><see cref="ICharacterSetSource"/> on MySqlConnector (INFORMATION_SCHEMA, works on MySQL and MariaDB).</summary>
public sealed class MySqlCharacterSetSource : ICharacterSetSource
{
    private const string Sql =
        "SELECT CHARACTER_SET_NAME, DESCRIPTION, DEFAULT_COLLATE_NAME FROM INFORMATION_SCHEMA.CHARACTER_SETS;" +
        "SELECT COLLATION_NAME, CHARACTER_SET_NAME FROM INFORMATION_SCHEMA.COLLATIONS WHERE CHARACTER_SET_NAME IS NOT NULL;" +
        "SELECT @@character_set_server, @@collation_server;" +
        "SELECT SCHEMA_NAME FROM INFORMATION_SCHEMA.SCHEMATA;";

    /// <inheritdoc />
    public async Task<CharacterSetCatalog> GetAsync(ConnectionDefinition definition, CancellationToken cancellationToken)
    {
        using var connection = new MySqlConnection(MySqlQuerySession.DataConnectionString(definition, pooling: true).ConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        using var command = new MySqlCommand(Sql, connection);
        using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

        var sets = new List<CharacterSetInfo>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            sets.Add(new CharacterSetInfo(reader.GetString(0), reader.IsDBNull(1) ? string.Empty : reader.GetString(1), reader.IsDBNull(2) ? string.Empty : reader.GetString(2)));
        }

        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        var collations = new List<CollationInfo>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            collations.Add(new CollationInfo(reader.GetString(0), reader.GetString(1)));
        }

        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        string? serverSet = null, serverCollation = null;
        if (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            serverSet = reader.IsDBNull(0) ? null : reader.GetString(0);
            serverCollation = reader.IsDBNull(1) ? null : reader.GetString(1);
        }

        await reader.NextResultAsync(cancellationToken).ConfigureAwait(false);
        var databases = new List<string>();
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            databases.Add(reader.GetString(0));
        }

        return new CharacterSetCatalog(sets, collations, serverSet, serverCollation, databases);
    }
}
