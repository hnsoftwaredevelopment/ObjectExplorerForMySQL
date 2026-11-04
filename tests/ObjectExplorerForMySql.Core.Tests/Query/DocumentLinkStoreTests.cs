using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Query;
using ObjectExplorerForMySql.Core.Scripting;
using ObjectExplorerForMySql.Core.Tests.Explorer;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Query;

public sealed class DocumentLinkStoreTests : IDisposable
{
    private readonly string _folder = Path.Combine(Path.GetTempPath(), "OeMySqlTests", Guid.NewGuid().ToString("N"));
    private readonly ListSink _sink = new();

    public DocumentLinkStoreTests()
    {
        Directory.CreateDirectory(_folder);
    }

    private string FilePath => Path.Combine(_folder, "documents.json");

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private DocumentLinkStore Store(Func<string, bool>? exists = null) =>
        new(FilePath, new ExtensionLogger(_sink, LogLevel.Debug), exists ?? (_ => true));

    [Fact]
    public void SaveAndLoad_RoundTrips_WithAndWithoutConnection()
    {
        var id = Guid.NewGuid();
        Store().Save(
        [
            new KeyValuePair<string, DocumentLink>(@"C:\a.sql", new DocumentLink(id, "sakila")),
            new KeyValuePair<string, DocumentLink>(@"C:\b.sql", new DocumentLink(null, null)),
        ]);

        var links = Store().Load().ToDictionary(p => p.Key, p => p.Value);

        Assert.Equal(new DocumentLink(id, "sakila"), links[@"C:\a.sql"]);
        Assert.Equal(new DocumentLink(null, null), links[@"C:\b.sql"]);
    }

    [Fact]
    public void Load_SkipsFilesThatNoLongerExist()
    {
        Store().Save(
        [
            new KeyValuePair<string, DocumentLink>(@"C:\keep.sql", new DocumentLink(null, "x")),
            new KeyValuePair<string, DocumentLink>(@"C:\gone.sql", new DocumentLink(null, "y")),
        ]);

        var links = Store(path => path.EndsWith("keep.sql", StringComparison.Ordinal)).Load();

        Assert.Equal([@"C:\keep.sql"], links.Select(p => p.Key));
    }

    [Fact]
    public void Load_MissingOrCorruptFile_GivesNoLinks()
    {
        Assert.Empty(Store().Load());

        File.WriteAllText(FilePath, "{ not json");

        Assert.Empty(Store().Load());
        Assert.Contains(_sink.Entries, e => e.Contains("could not be read", StringComparison.Ordinal));
    }

    [Fact]
    public void DocumentConnections_Load_KeepsExistingLinks()
    {
        var documents = new DocumentConnections();
        documents.Register(@"C:\a.sql", null, "new");
        var raised = 0;
        documents.Changed += (_, _) => raised++;

        documents.Load([new KeyValuePair<string, DocumentLink>(@"C:\a.sql", new DocumentLink(null, "old")), new KeyValuePair<string, DocumentLink>(@"C:\b.sql", new DocumentLink(null, "b"))]);

        Assert.Equal("new", documents.Find(@"C:\a.sql")!.Database);
        Assert.Equal("b", documents.Find(@"C:\b.sql")!.Database);
        Assert.Equal(0, raised);
    }
}
