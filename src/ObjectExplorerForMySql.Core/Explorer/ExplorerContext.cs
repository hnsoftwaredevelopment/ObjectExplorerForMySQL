using System;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Logging;
using ObjectExplorerForMySql.Core.Metadata;

namespace ObjectExplorerForMySql.Core.Explorer;

/// <summary>Texts of fixed tree nodes. The extension fills them from its resources (ADR-005); the defaults are English.</summary>
public sealed class ExplorerTexts
{
    /// <summary>Gets or sets the placeholder shown while children load.</summary>
    public string Loading { get; set; } = "Loading...";

    /// <summary>Gets or sets the name of the folder with all databases.</summary>
    public string Databases { get; set; } = "Databases";

    /// <summary>Gets or sets the name of the folder with the server's own databases.</summary>
    public string SystemDatabases { get; set; } = "System Databases";

    /// <summary>Gets or sets the name of the folder with the tables of a database.</summary>
    public string Tables { get; set; } = "Tables";

    /// <summary>Gets or sets the name of the folder with the columns of a table.</summary>
    public string Columns { get; set; } = "Columns";

    /// <summary>Gets or sets the name of the folder with the keys of a table.</summary>
    public string Keys { get; set; } = "Keys";

    /// <summary>Gets or sets the name of the folder with the check constraints of a table.</summary>
    public string Constraints { get; set; } = "Constraints";

    /// <summary>Gets or sets the name of the folder with the triggers of a table.</summary>
    public string Triggers { get; set; } = "Triggers";

    /// <summary>Gets or sets the name of the folder with the indexes of a table.</summary>
    public string Indexes { get; set; } = "Indexes";

    /// <summary>Gets or sets the name of the folder with the views of a database.</summary>
    public string Views { get; set; } = "Views";

    /// <summary>Gets or sets the name of the folder with the stored procedures of a database.</summary>
    public string StoredProcedures { get; set; } = "Stored Procedures";

    /// <summary>Gets or sets the name of the folder with the functions of a database.</summary>
    public string Functions { get; set; } = "Functions";

    /// <summary>Gets or sets the name of the folder with the events of a database.</summary>
    public string Events { get; set; } = "Events";

    /// <summary>Gets or sets the name of the folder with the parameters of a routine.</summary>
    public string Parameters { get; set; } = "Parameters";

    /// <summary>Gets or sets the name of the folder with users and roles (US-1002).</summary>
    public string Security { get; set; } = "Security";

    /// <summary>Gets or sets the name of the folder with the users.</summary>
    public string Users { get; set; } = "Users";

    /// <summary>Gets or sets the name of the folder with the roles.</summary>
    public string Roles { get; set; } = "Roles";

    /// <summary>Gets or sets the suffix of a grant with WITH GRANT OPTION.</summary>
    public string WithGrantOption { get; set; } = "with grant option";

    /// <summary>Gets or sets the format of a folder label while a filter is active; {0} is the folder name.</summary>
    public string Filtered { get; set; } = "{0} (filtered)";
}

/// <summary>Services shared by all tree nodes.</summary>
public sealed class ExplorerContext
{
    /// <summary>Initializes a new instance of the <see cref="ExplorerContext"/> class.</summary>
    public ExplorerContext(IMetadataProvider metadata, IPasswordPrompt passwordPrompt, ExplorerTexts texts, IExtensionLogger logger)
    {
        Metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        PasswordPrompt = passwordPrompt ?? throw new ArgumentNullException(nameof(passwordPrompt));
        Texts = texts ?? throw new ArgumentNullException(nameof(texts));
        Logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>Gets the metadata reader.</summary>
    public IMetadataProvider Metadata { get; }

    /// <summary>Gets the password prompt.</summary>
    public IPasswordPrompt PasswordPrompt { get; }

    /// <summary>Gets the fixed node texts.</summary>
    public ExplorerTexts Texts { get; }

    /// <summary>Gets the logger.</summary>
    public IExtensionLogger Logger { get; }

    /// <summary>Gets the name filter (US-0308).</summary>
    public ExplorerFilter Filter { get; } = new();

    /// <summary>Gets or sets the privilege check that runs after a connect (US-1001); null skips it.</summary>
    public Security.PrivilegeChecker? PrivilegeChecker { get; set; }

    /// <summary>Gets or sets the reader of users, roles and grants (US-1002); null hides the Security folder.</summary>
    public Security.IAccountReader? AccountReader { get; set; }
}
