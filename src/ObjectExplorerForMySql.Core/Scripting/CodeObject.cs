using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using ObjectExplorerForMySql.Core.DragDrop;

namespace ObjectExplorerForMySql.Core.Scripting;

/// <summary>Kind of object whose source can be shown with View Code (US-0601).</summary>
public enum CodeObjectKind
{
    /// <summary>Base table: <c>SHOW CREATE TABLE</c>.</summary>
    Table,

    /// <summary>View: <c>SHOW CREATE VIEW</c>.</summary>
    View,

    /// <summary>Stored procedure: <c>SHOW CREATE PROCEDURE</c>.</summary>
    Procedure,

    /// <summary>Stored function: <c>SHOW CREATE FUNCTION</c>.</summary>
    Function,

    /// <summary>Trigger: <c>SHOW CREATE TRIGGER</c>.</summary>
    Trigger,

    /// <summary>Event: <c>SHOW CREATE EVENT</c>.</summary>
    Event,
}

/// <summary>An object whose CREATE statement can be shown (US-0601).</summary>
public sealed class CodeObject
{
    /// <summary>Initializes a new instance of the <see cref="CodeObject"/> class.</summary>
    public CodeObject(CodeObjectKind kind, string database, string name)
    {
        Kind = kind;
        Database = database ?? throw new ArgumentNullException(nameof(database));
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>Gets the object kind.</summary>
    public CodeObjectKind Kind { get; }

    /// <summary>Gets the database (schema).</summary>
    public string Database { get; }

    /// <summary>Gets the object name.</summary>
    public string Name { get; }

    /// <summary>Gets the keyword after SHOW CREATE: TABLE, VIEW, PROCEDURE, FUNCTION, TRIGGER or EVENT.</summary>
    public string Keyword => Kind.ToString().ToUpperInvariant();

    /// <summary>Gets the statement that reads the source, with quoted names: <c>SHOW CREATE TABLE `sakila`.`film`</c>.</summary>
    public string ShowCreateStatement => $"SHOW CREATE {Keyword} {MySqlIdentifier.Quote(Database)}.{MySqlIdentifier.Quote(Name)}";

    /// <summary>
    /// Gets the name of the result column with the source: "Create Table", "Create View", "Create Procedure",
    /// "Create Function", "SQL Original Statement" (trigger) or "Create Event".
    /// </summary>
    public string SourceColumn => Kind switch
    {
        CodeObjectKind.Trigger => "SQL Original Statement",
        _ => "Create " + Kind.ToString(),
    };

    /// <summary>Gets a value indicating whether the source contains statements with semicolons and needs DELIMITER to run in a client.</summary>
    public bool NeedsDelimiter => Kind is CodeObjectKind.Procedure or CodeObjectKind.Function or CodeObjectKind.Trigger or CodeObjectKind.Event;

    /// <summary>Gets the document file name: <c>sakila.film.sql</c>. Characters not allowed in file names become <c>_</c>.</summary>
    public string FileName => SafeFileName(Database) + "." + SafeFileName(Name) + ".sql";

    private static string SafeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(name.Length);
        foreach (var c in name)
        {
            builder.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        }

        return builder.ToString();
    }
}

/// <summary>Builds the text of a View Code document (US-0601).</summary>
public static class CodeDocument
{
    /// <summary>Delimiter used around routines, triggers and events.</summary>
    public const string Delimiter = "$$";

    /// <summary>
    /// Returns the document text: a comment line with object, connection and database, then the CREATE statement
    /// ending with <c>;</c>, or between <c>DELIMITER $$</c> and <c>DELIMITER ;</c> for routines, triggers and events.
    /// </summary>
    public static string Format(CodeObject codeObject, string createStatement, string? connectionLabel)
    {
        if (codeObject is null)
        {
            throw new ArgumentNullException(nameof(codeObject));
        }

        return Header(codeObject.Kind.ToString(), codeObject, connectionLabel) + Environment.NewLine + Body(codeObject, createStatement);
    }

    /// <summary>
    /// Returns the CREATE statement ready to run: ending with <c>;</c>, or between <c>DELIMITER $$</c> and
    /// <c>DELIMITER ;</c> for routines, triggers and events.
    /// </summary>
    public static string Body(CodeObject codeObject, string createStatement)
    {
        if (codeObject is null)
        {
            throw new ArgumentNullException(nameof(codeObject));
        }

        var statement = (createStatement ?? string.Empty).Trim().TrimEnd(';').TrimEnd();
        var nl = Environment.NewLine;
        return codeObject.NeedsDelimiter
            ? $"DELIMITER {Delimiter}{nl}{nl}{statement}{Delimiter}{nl}{nl}DELIMITER ;{nl}"
            : $"{statement};{nl}";
    }

    /// <summary>Returns the comment line at the top of a document: <c>-- Table sakila.film on Local</c> and a line break.</summary>
    public static string Header(string title, CodeObject codeObject, string? connectionLabel) =>
        string.Format(
            CultureInfo.InvariantCulture,
            "-- {0} {1}.{2}{3}{4}",
            title,
            codeObject.Database,
            codeObject.Name,
            string.IsNullOrEmpty(connectionLabel) ? string.Empty : " on " + connectionLabel,
            Environment.NewLine);
}

/// <summary>Connection and database of a query document (US-0701). Both can be empty: a document without a connection yet.</summary>
public sealed record DocumentLink(Guid? ConnectionId, string? Database);

/// <summary>
/// Remembers which connection and database a document belongs to (US-0601, US-0701), so the query editor can run it on
/// the right server. Keys are full file paths, compared case-insensitively.
/// </summary>
public sealed class DocumentConnections
{
    private readonly ConcurrentDictionary<string, DocumentLink> _documents = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Raised after a link was added, changed, renamed or removed; the argument is the (new) file path.</summary>
    public event EventHandler<string>? Changed;

    /// <summary>Links <paramref name="filePath"/> to a connection and database; a second call replaces the link.</summary>
    public void Register(string filePath, Guid? connectionId, string? database)
    {
        var link = new DocumentLink(connectionId, string.IsNullOrEmpty(database) ? null : database);
        if (_documents.TryGetValue(filePath, out var old) && old == link)
        {
            return;
        }

        _documents[filePath] = link;
        Changed?.Invoke(this, filePath);
    }

    /// <summary>Returns the link of <paramref name="filePath"/>, or null.</summary>
    public DocumentLink? Find(string filePath) =>
        _documents.TryGetValue(filePath, out var link) ? link : null;

    /// <summary>Removes the link.</summary>
    public void Remove(string filePath)
    {
        if (_documents.TryRemove(filePath, out _))
        {
            Changed?.Invoke(this, filePath);
        }
    }

    /// <summary>Moves the link to a new path after Save As or a rename. Does nothing when the old path has no link.</summary>
    public void Rename(string oldPath, string newPath)
    {
        if (string.Equals(oldPath, newPath, StringComparison.OrdinalIgnoreCase) || !_documents.TryRemove(oldPath, out var link))
        {
            return;
        }

        _documents[newPath] = link;
        Changed?.Invoke(this, newPath);
    }

    /// <summary>Returns all links, for saving.</summary>
    public IReadOnlyList<KeyValuePair<string, DocumentLink>> Snapshot() => _documents.ToArray();

    /// <summary>Adds saved links without raising <see cref="Changed"/>. Existing links win.</summary>
    public void Load(IEnumerable<KeyValuePair<string, DocumentLink>> links)
    {
        foreach (var pair in links ?? throw new ArgumentNullException(nameof(links)))
        {
            _documents.TryAdd(pair.Key, pair.Value);
        }
    }
}
