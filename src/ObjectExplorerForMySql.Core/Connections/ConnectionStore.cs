using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Security;

namespace ObjectExplorerForMySql.Core.Connections;

/// <summary>
/// Saves and loads connection definitions as JSON (ADR-004). Passwords are only written when
/// <see cref="ConnectionDefinition.SavePassword"/> is set, and then only encrypted.
/// </summary>
public sealed class ConnectionStore
{
    /// <summary>Current file format version.</summary>
    public const int FileVersion = 1;

    private readonly string _filePath;
    private readonly ISecretProtector _protector;
    private readonly IExtensionLogger _logger;
    private readonly Func<DateTimeOffset> _clock;

    /// <summary>Initializes a new instance of the <see cref="ConnectionStore"/> class.</summary>
    public ConnectionStore(string filePath, ISecretProtector protector, IExtensionLogger logger, Func<DateTimeOffset>? clock = null)
    {
        _filePath = string.IsNullOrWhiteSpace(filePath) ? throw new ArgumentException("A file path is required.", nameof(filePath)) : filePath;
        _protector = protector ?? throw new ArgumentNullException(nameof(protector));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _clock = clock ?? (() => DateTimeOffset.Now);
    }

    /// <summary>Gets the default file location: <c>%LOCALAPPDATA%\ObjectExplorerForMySql\connections.json</c>.</summary>
    public static string DefaultFilePath =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ObjectExplorerForMySql", "connections.json");

    /// <summary>Gets the path of the file.</summary>
    public string FilePath => _filePath;

    /// <summary>
    /// Loads all valid connections. Never throws: a missing file gives an empty list; an unreadable
    /// file is renamed to <c>connections.json.corrupt-yyyyMMdd-HHmmss</c>, logged as a warning and
    /// gives an empty list, so the next save does not overwrite the user's data.
    /// </summary>
    public IReadOnlyList<ConnectionDefinition> Load()
    {
        if (!File.Exists(_filePath))
        {
            _logger.Debug($"No connection file at '{_filePath}'.");
            return Array.Empty<ConnectionDefinition>();
        }

        ConnectionFileDto? file;
        try
        {
            // ReadAllText removes a byte order mark (files saved with Notepad); the serializer does not accept one.
            var json = File.ReadAllText(_filePath);
            using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(json));
            file = (ConnectionFileDto?)new DataContractJsonSerializer(typeof(ConnectionFileDto)).ReadObject(stream);
        }
        catch (Exception ex) when (ex is SerializationException or System.Xml.XmlException or IOException or UnauthorizedAccessException or InvalidCastException or ArgumentException or FormatException)
        {
            var backup = MoveCorruptFile();
            _logger.Warning($"The connection file '{_filePath}' could not be read and was renamed to '{backup}'. Starting with an empty list.", ex);
            return Array.Empty<ConnectionDefinition>();
        }

        var result = new List<ConnectionDefinition>();
        foreach (var dto in file?.Connections ?? new List<ConnectionDto>())
        {
            var definition = FromDto(dto);
            var errors = definition.Validate();
            if (errors.Count > 0)
            {
                _logger.Warning($"Skipped connection '{definition.Label}' from the connection file: {string.Join(", ", errors)}.");
                continue;
            }

            result.Add(definition);
        }

        _logger.Info($"Loaded {result.Count} connection(s) from '{_filePath}'.");
        return result;
    }

    /// <summary>
    /// Saves <paramref name="connections"/>, replacing the file. Writes to a temporary file first,
    /// so a failure never leaves a half-written file. Throws <see cref="IOException"/> or
    /// <see cref="UnauthorizedAccessException"/> when the file cannot be written.
    /// </summary>
    public void Save(IEnumerable<ConnectionDefinition> connections)
    {
        if (connections is null)
        {
            throw new ArgumentNullException(nameof(connections));
        }

        var file = new ConnectionFileDto
        {
            Version = FileVersion,
            Connections = connections.Select(ToDto).ToList(),
        };

        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var tempPath = _filePath + ".tmp";
        using (var stream = File.Create(tempPath))
        using (var writer = JsonReaderWriterFactory.CreateJsonWriter(stream, new UTF8Encoding(false), ownsStream: false, indent: true))
        {
            new DataContractJsonSerializer(typeof(ConnectionFileDto)).WriteObject(writer, file);
        }

        if (File.Exists(_filePath))
        {
            File.Replace(tempPath, _filePath, null);
        }
        else
        {
            File.Move(tempPath, _filePath);
        }

        _logger.Debug($"Saved {file.Connections.Count} connection(s) to '{_filePath}'.");
    }

    private string MoveCorruptFile()
    {
        var backup = $"{_filePath}.corrupt-{_clock().ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture)}";
        try
        {
            File.Move(_filePath, backup);
            return backup;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _logger.Warning($"The unreadable connection file could not be renamed to '{backup}'.", ex);
            return _filePath;
        }
    }

    private ConnectionDto ToDto(ConnectionDefinition definition) => new()
    {
        Id = definition.Id.ToString("D"),
        DisplayName = definition.DisplayName,
        Host = definition.Host,
        Port = definition.Port,
        UserName = definition.UserName,
        SavePassword = definition.SavePassword,
        ProtectedPassword = definition.SavePassword && !string.IsNullOrEmpty(definition.Password)
            ? _protector.Protect(definition.Password!)
            : null,
        DefaultDatabase = definition.DefaultDatabase,
        SslMode = definition.SslMode.ToString(),
        ConnectionTimeoutSeconds = definition.ConnectionTimeoutSeconds,
        AllowPublicKeyRetrieval = definition.AllowPublicKeyRetrieval,
    };

    private ConnectionDefinition FromDto(ConnectionDto dto)
    {
        var definition = new ConnectionDefinition
        {
            Id = Guid.TryParse(dto.Id, out var id) ? id : Guid.NewGuid(),
            DisplayName = dto.DisplayName,
            Host = dto.Host ?? string.Empty,
            Port = dto.Port ?? ConnectionDefinition.DefaultPort,
            UserName = dto.UserName ?? string.Empty,
            SavePassword = dto.SavePassword ?? true,
            DefaultDatabase = dto.DefaultDatabase,
            ConnectionTimeoutSeconds = dto.ConnectionTimeoutSeconds ?? ConnectionDefinition.DefaultTimeoutSeconds,
            AllowPublicKeyRetrieval = dto.AllowPublicKeyRetrieval ?? false,
        };

        if (dto.SslMode is not null)
        {
            if (Enum.TryParse<SslMode>(dto.SslMode, ignoreCase: true, out var sslMode))
            {
                definition.SslMode = sslMode;
            }
            else
            {
                _logger.Warning($"Unknown SSL mode '{dto.SslMode}' for connection '{definition.Label}'; using Preferred.");
            }
        }

        if (definition.SavePassword && !string.IsNullOrEmpty(dto.ProtectedPassword))
        {
            definition.Password = _protector.Unprotect(dto.ProtectedPassword!);
            if (definition.Password is null)
            {
                _logger.Warning($"The saved password of connection '{definition.Label}' could not be decrypted (other Windows user or computer). You will be asked for it.");
            }
        }

        return definition;
    }

    [DataContract]
    private sealed class ConnectionFileDto
    {
        [DataMember(Name = "version", Order = 0)]
        public int Version { get; set; }

        [DataMember(Name = "connections", Order = 1)]
        public List<ConnectionDto>? Connections { get; set; }
    }

    [DataContract]
    private sealed class ConnectionDto
    {
        [DataMember(Name = "id", Order = 0)]
        public string? Id { get; set; }

        [DataMember(Name = "displayName", Order = 1, EmitDefaultValue = false)]
        public string? DisplayName { get; set; }

        [DataMember(Name = "host", Order = 2)]
        public string? Host { get; set; }

        [DataMember(Name = "port", Order = 3)]
        public int? Port { get; set; }

        [DataMember(Name = "userName", Order = 4)]
        public string? UserName { get; set; }

        [DataMember(Name = "savePassword", Order = 5)]
        public bool? SavePassword { get; set; }

        [DataMember(Name = "protectedPassword", Order = 6, EmitDefaultValue = false)]
        public string? ProtectedPassword { get; set; }

        [DataMember(Name = "defaultDatabase", Order = 7, EmitDefaultValue = false)]
        public string? DefaultDatabase { get; set; }

        [DataMember(Name = "sslMode", Order = 8)]
        public string? SslMode { get; set; }

        [DataMember(Name = "connectionTimeoutSeconds", Order = 9)]
        public int? ConnectionTimeoutSeconds { get; set; }

        [DataMember(Name = "allowPublicKeyRetrieval", Order = 10)]
        public bool? AllowPublicKeyRetrieval { get; set; }
    }
}
