using System;

namespace ObjectExplorerForMySql.Commands;

/// <summary>GUIDs and IDs from <c>ObjectExplorerCommands.vsct</c>. Keep both files in sync.</summary>
internal static class CommandIds
{
    /// <summary>Command set of the explorer toolbar and context menus.</summary>
    public static readonly Guid CommandSet = new("5c3878cd-0686-4593-baad-3fe106df3682");

    /// <summary>Toolbar of the explorer tool window.</summary>
    public const int ExplorerToolbar = 0x1000;

    /// <summary>Context menu of a server node.</summary>
    public const int ServerContextMenu = 0x2000;

    /// <summary>Context menu of every node other than a server.</summary>
    public const int NodeContextMenu = 0x3000;

    /// <summary>"Add MySQL Server" button.</summary>
    public const int AddServer = 0x0100;

    /// <summary>"Refresh" (F5) on the toolbar and in the context menus; reloads the selected node (US-0307).</summary>
    public const int Refresh = 0x0200;

    /// <summary>"Disconnect" on a server node.</summary>
    public const int DisconnectServer = 0x0201;

    /// <summary>"Remove" on a server node.</summary>
    public const int RemoveServer = 0x0202;

    /// <summary>"Properties" on a server node.</summary>
    public const int ServerProperties = 0x0203;

    /// <summary>"Collapse All" on the toolbar (US-0308).</summary>
    public const int CollapseAll = 0x0204;

    /// <summary>"View Code" on tables, views, routines, triggers and events (US-0601).</summary>
    public const int ViewCode = 0x0205;

    /// <summary>"Script Database..." on a user database (US-0603).</summary>
    public const int ScriptDatabase = 0x0206;

    /// <summary>"New Query" on a server or database node (US-0701).</summary>
    public const int NewQuery = 0x0207;

    /// <summary>"Execute" (Ctrl+Shift+E) in the MySQL Query Editor (US-0703).</summary>
    public const int ExecuteQuery = 0x0208;

    /// <summary>"Cancel Query" (Alt+Break) in the MySQL Query Editor (US-0703).</summary>
    public const int CancelQuery = 0x0209;

    /// <summary>"View Data" on tables and views (US-0801).</summary>
    public const int ViewData = 0x020A;

    /// <summary>"Delete" (Del) on databases, tables, views, routines, triggers, events, indexes and columns (US-0901).</summary>
    public const int DeleteObject = 0x020B;

    /// <summary>"Rename" (F2) on tables, views and table columns (US-0902).</summary>
    public const int RenameObject = 0x020C;

    /// <summary>"New Database..." on the Databases folder (US-0903).</summary>
    public const int NewDatabase = 0x020D;

    /// <summary>"New Table...", "New View...", ... on the object folders; the text follows the folder (US-0904).</summary>
    public const int NewObject = 0x020E;

    /// <summary>"New User..." on the Users folder (US-1003).</summary>
    public const int NewUser = 0x020F;

    /// <summary>"Change Password..." on a user (US-1003).</summary>
    public const int ChangePassword = 0x0210;

    /// <summary>"Grant on Database..." on a user or role (US-1003).</summary>
    public const int GrantOnDatabase = 0x0211;

    /// <summary>"Revoke..." on a grant (US-1003).</summary>
    public const int Revoke = 0x0212;

    /// <summary>"New Role..." on the Roles folder (US-1004).</summary>
    public const int NewRole = 0x0213;

    /// <summary>"Grant Role..." on a user (US-1005).</summary>
    public const int GrantRole = 0x0214;

    /// <summary>"Copy Connection String" on a server or database (US-0505).</summary>
    public const int CopyConnectionString = 0x0215;

    /// <summary>"Explain" (Ctrl+L) in the MySQL Query Editor: the estimated plan (US-1301).</summary>
    public const int ExplainQuery = 0x0216;

    /// <summary>"Explain Analyze" in the MySQL Query Editor: the plan with actual rows and times (US-1302).</summary>
    public const int ExplainAnalyzeQuery = 0x0217;

    /// <summary>"View Dependencies" on a table, view, procedure or function (US-1303).</summary>
    public const int ViewDependencies = 0x0218;

    /// <summary>"Generate Documentation..." on a user database (US-1201).</summary>
    public const int GenerateDocumentation = 0x0219;

    /// <summary>Activity Monitor on a server (US-1501).</summary>
    public const int ActivityMonitor = 0x021A;

    /// <summary>
    /// First Script As command (US-0602). The id of a command is <c>ScriptBase + 2 * (int)ScriptKind + target</c>,
    /// with target 0 = New Query Window and 1 = Clipboard, as generated in the VSCT file.
    /// </summary>
    public const int ScriptBase = 0x0300;
}
