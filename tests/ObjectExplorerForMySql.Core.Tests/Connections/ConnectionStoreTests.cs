using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Security;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Connections;

public sealed class ConnectionStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "oefm-tests-" + Guid.NewGuid().ToString("N"));
    private readonly ListSink _sink = new();

    public ConnectionStoreTests() => Directory.CreateDirectory(_directory);

    private string FilePath => Path.Combine(_directory, "sub", "connections.json");

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private ConnectionStore CreateStore(ISecretProtector? protector = null) =>
        new(FilePath, protector ?? new ReversingProtector(), new ExtensionLogger(_sink, LogLevel.Debug), () => new DateTimeOffset(2026, 10, 26, 9, 30, 0, TimeSpan.Zero));

    private static ConnectionDefinition Sample() => new()
    {
        DisplayName = "Production",
        Host = "db.local",
        Port = 3307,
        UserName = "app",
        Password = "Secret123",
        SavePassword = true,
        DefaultDatabase = "shop",
        SslMode = SslMode.VerifyFull,
        ConnectionTimeoutSeconds = 30,
        AllowPublicKeyRetrieval = true,
    };

    [Fact]
    public void SaveThenLoad_RoundTripsEveryField()
    {
        var original = Sample();
        var store = CreateStore();

        store.Save([original]);
        var loaded = Assert.Single(store.Load());

        Assert.Equal(original.Id, loaded.Id);
        Assert.Equal("Production", loaded.DisplayName);
        Assert.Equal("db.local", loaded.Host);
        Assert.Equal(3307, loaded.Port);
        Assert.Equal("app", loaded.UserName);
        Assert.Equal("Secret123", loaded.Password);
        Assert.True(loaded.SavePassword);
        Assert.Equal("shop", loaded.DefaultDatabase);
        Assert.Equal(SslMode.VerifyFull, loaded.SslMode);
        Assert.Equal(30, loaded.ConnectionTimeoutSeconds);
        Assert.True(loaded.AllowPublicKeyRetrieval);
    }

    [Fact]
    public void Save_WritesProtectedPasswordOnly()
    {
        CreateStore().Save([Sample()]);

        var json = File.ReadAllText(FilePath);
        Assert.DoesNotContain("Secret123", json);
        Assert.Contains("\"protectedPassword\"", json);
    }

    [Fact]
    public void Save_WithSavePasswordOff_WritesNoPasswordAndLoadsNone()
    {
        var definition = Sample();
        definition.SavePassword = false;
        var store = CreateStore();

        store.Save([definition]);

        Assert.DoesNotContain("protectedPassword", File.ReadAllText(FilePath));
        var loaded = Assert.Single(store.Load());
        Assert.Null(loaded.Password);
        Assert.False(loaded.SavePassword);
    }

    [Fact]
    public void Save_WritesIndentedJsonWithVersion()
    {
        CreateStore().Save([Sample()]);

        var json = File.ReadAllText(FilePath);
        Assert.Contains("\"version\":1", json.Replace(" ", string.Empty));
        Assert.Contains("\n", json);
        Assert.False(File.Exists(FilePath + ".tmp"));
    }

    [Fact]
    public void Save_ReplacesExistingFile()
    {
        var store = CreateStore();
        store.Save([Sample(), Sample()]);

        store.Save([Sample()]);

        Assert.Single(store.Load());
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmptyWithoutWarning()
    {
        Assert.Empty(CreateStore().Load());
        Assert.DoesNotContain(_sink.Entries, e => e.Contains("WARN"));
    }

    [Theory]
    [InlineData("this is not json")]
    [InlineData("{\"version\":1,\"connections\":[{\"port\":\"abc\"}]}")]
    [InlineData("")]
    public void Load_CorruptFile_RenamesItAndReturnsEmpty(string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, content);

        var result = CreateStore().Load();

        Assert.Empty(result);
        Assert.False(File.Exists(FilePath));
        Assert.Equal(content, File.ReadAllText(FilePath + ".corrupt-20261026-093000"));
        Assert.Contains(_sink.Entries, e => e.Contains("WARN") && e.Contains("could not be read"));
    }

    [Fact]
    public void Load_SkipsInvalidEntriesAndAppliesDefaults()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
        File.WriteAllText(FilePath, """
            {"version":1,"connections":[
              {"id":"not-a-guid","host":"h1","userName":"u1"},
              {"host":"","userName":"u2"},
              {"host":"h3","userName":"u3","sslMode":"Bogus"}
            ]}
            """, Encoding.UTF8);

        var loaded = CreateStore().Load();

        Assert.Equal(["h1", "h3"], loaded.Select(c => c.Host));
        Assert.Equal(3306, loaded[0].Port);
        Assert.Equal(15, loaded[0].ConnectionTimeoutSeconds);
        Assert.NotEqual(Guid.Empty, loaded[0].Id);
        Assert.Equal(SslMode.Preferred, loaded[1].SslMode);
        Assert.Contains(_sink.Entries, e => e.Contains("Skipped connection"));
        Assert.Contains(_sink.Entries, e => e.Contains("Unknown SSL mode 'Bogus'"));
    }

    [Fact]
    public void Load_PasswordThatCannotBeDecrypted_GivesNullPasswordAndWarning()
    {
        CreateStore().Save([Sample()]);

        var loaded = Assert.Single(CreateStore(new FailingProtector()).Load());

        Assert.Null(loaded.Password);
        Assert.Contains(_sink.Entries, e => e.Contains("could not be decrypted"));
    }

    [Fact]
    public void Logs_NeverContainThePassword()
    {
        var store = CreateStore();
        store.Save([Sample()]);
        store.Load();

        Assert.DoesNotContain(_sink.Entries, e => e.Contains("Secret123"));
    }

    [Fact]
    public void DpapiProtector_RoundTripsAndHidesPlainText()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is only available on Windows.");
        var protector = new DpapiSecretProtector();

        var protectedText = protector.Protect("Secret123");

        Assert.DoesNotContain("Secret123", protectedText);
        Assert.DoesNotContain("Secret123", Encoding.UTF8.GetString(Convert.FromBase64String(protectedText)));
        Assert.Equal("Secret123", protector.Unprotect(protectedText));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not base64 !")]
    [InlineData("AAAA")]
    public void DpapiProtector_UnprotectInvalidInputReturnsNull(string input)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is only available on Windows.");

        Assert.Null(new DpapiSecretProtector().Unprotect(input));
    }

    [Fact]
    public void SaveThenLoad_WithDpapi_RoundTripsPassword()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "DPAPI is only available on Windows.");
        var store = CreateStore(new DpapiSecretProtector());

        store.Save([Sample()]);

        Assert.DoesNotContain("Secret123", File.ReadAllText(FilePath));
        Assert.Equal("Secret123", Assert.Single(store.Load()).Password);
    }

    private sealed class ReversingProtector : ISecretProtector
    {
        public string Protect(string secret) => "enc:" + new string(secret.Reverse().ToArray());

        public string? Unprotect(string protectedSecret) => new string(protectedSecret.Substring(4).Reverse().ToArray());
    }

    private sealed class FailingProtector : ISecretProtector
    {
        public string Protect(string secret) => throw new NotSupportedException();

        public string? Unprotect(string protectedSecret) => null;
    }

    private sealed class ListSink : ILogSink
    {
        public List<string> Entries { get; } = [];

        public void Write(string formattedEntry) => Entries.Add(formattedEntry);
    }
}
