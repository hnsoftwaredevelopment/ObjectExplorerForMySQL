using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Scripting;

namespace ObjectExplorerForMySql.Core.Query;

/// <summary>
/// Saves the connection and database of each query document (US-0701), so a document that Visual Studio opens again
/// after a restart, or a .sql file in a project, keeps its connection. File:
/// <c>%LOCALAPPDATA%\ObjectExplorerForMySql\documents.json</c>.
/// </summary>
public sealed class DocumentLinkStore
{
    private readonly string _filePath;
    private readonly IExtensionLogger _logger;
    private readonly Func<string, bool> _fileExists;

    /// <summary>Initializes a new instance of the <see cref="DocumentLinkStore"/> class.</summary>
    public DocumentLinkStore(string filePath, IExtensionLogger logger, Func<string, bool>? fileExists = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath) ? throw new ArgumentException("A file path is required.", nameof(filePath)) : filePath;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _fileExists = fileExists ?? File.Exists;
    }

    /// <summary>Gets the default file location.</summary>
    public static string DefaultFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ObjectExplorerForMySql", "documents.json");

    /// <summary>
    /// Loads the links of documents that still exist. Never throws: a missing or unreadable file gives an empty list
    /// (the links are a convenience; losing them only means choosing the connection again).
    /// </summary>
    public IReadOnlyList<KeyValuePair<string, DocumentLink>> Load()
    {
        if (!File.Exists(_filePath))
        {
            return Array.Empty<KeyValuePair<string, DocumentLink>>();
        }

        try
        {
            var json = File.ReadAllText(_filePath);
            using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(json));
            var file = (DocumentFileDto?)new DataContractJsonSerializer(typeof(DocumentFileDto)).ReadObject(stream);
            var result = new List<KeyValuePair<string, DocumentLink>>();
            foreach (var dto in file?.Documents ?? new List<DocumentDto>())
            {
                if (dto.Path is not { Length: > 0 } path || !_fileExists(path))
                {
                    continue;
                }

                Guid? connectionId = Guid.TryParse(dto.ConnectionId, out var id) ? id : null;
                result.Add(new KeyValuePair<string, DocumentLink>(path, new DocumentLink(connectionId, dto.Database)));
            }

            _logger.Debug($"Loaded {result.Count} query document link(s) from '{_filePath}'.");
            return result;
        }
        catch (Exception ex) when (ex is SerializationException or System.Xml.XmlException or IOException or UnauthorizedAccessException or InvalidCastException or ArgumentException)
        {
            _logger.Warning($"The query document links in '{_filePath}' could not be read; starting without links.", ex);
            return Array.Empty<KeyValuePair<string, DocumentLink>>();
        }
    }

    /// <summary>Saves the links, replacing the file. A write failure is logged, not thrown.</summary>
    public void Save(IEnumerable<KeyValuePair<string, DocumentLink>> links)
    {
        if (links is null)
        {
            throw new ArgumentNullException(nameof(links));
        }

        var file = new DocumentFileDto
        {
            Version = 1,
            Documents = links
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .Select(pair => new DocumentDto { Path = pair.Key, ConnectionId = pair.Value.ConnectionId?.ToString("D"), Database = pair.Value.Database })
                .ToList(),
        };

        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var tempPath = _filePath + ".tmp";
            using (var stream = File.Create(tempPath))
            using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), ownsStream: false, indent: true))
            {
                new DataContractJsonSerializer(typeof(DocumentFileDto)).WriteObject(writer, file);
            }

            if (File.Exists(_filePath))
            {
                File.Replace(tempPath, _filePath, null);
            }
            else
            {
                File.Move(tempPath, _filePath);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"Saving the query document links to '{_filePath}' failed.", ex);
        }
    }

    [DataContract]
    private sealed class DocumentFileDto
    {
        [DataMember(Name = "version", Order = 0)]
        public int Version { get; set; }

        [DataMember(Name = "documents", Order = 1)]
        public List<DocumentDto>? Documents { get; set; }
    }

    [DataContract]
    private sealed class DocumentDto
    {
        [DataMember(Name = "path", Order = 0)]
        public string? Path { get; set; }

        [DataMember(Name = "connectionId", Order = 1, EmitDefaultValue = false)]
        public string? ConnectionId { get; set; }

        [DataMember(Name = "database", Order = 2, EmitDefaultValue = false)]
        public string? Database { get; set; }
    }
}
