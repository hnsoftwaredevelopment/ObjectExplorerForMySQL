using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using ObjectExplorerForMySql.Core.DragDrop;
using ObjectExplorerForMySql.Core.Metadata;

namespace ObjectExplorerForMySql.Core.Scripting;

/// <summary>Statement of Script As (US-0602).</summary>
public enum ScriptKind
{
    /// <summary>CREATE (from SHOW CREATE).</summary>
    Create,

    /// <summary>DROP.</summary>
    Drop,

    /// <summary>SELECT of all columns; for a function a SELECT of a call.</summary>
    Select,

    /// <summary>INSERT with a user variable per column.</summary>
    Insert,

    /// <summary>UPDATE with a user variable per column and the primary key in WHERE.</summary>
    Update,

    /// <summary>DELETE with the primary key in WHERE.</summary>
    Delete,

    /// <summary>CALL of a procedure.</summary>
    Call,
}

/// <summary>Input of a script: the object and the metadata the statement needs.</summary>
public sealed class ScriptRequest
{
    /// <summary>Initializes a new instance of the <see cref="ScriptRequest"/> class.</summary>
    public ScriptRequest(ScriptKind kind, CodeObject codeObject)
    {
        Kind = kind;
        CodeObject = codeObject ?? throw new ArgumentNullException(nameof(codeObject));
    }

    /// <summary>Gets the statement.</summary>
    public ScriptKind Kind { get; }

    /// <summary>Gets the object.</summary>
    public CodeObject CodeObject { get; }

    /// <summary>Gets the columns in ordinal order (SELECT, INSERT, UPDATE, DELETE).</summary>
    public IReadOnlyList<ColumnInfo> Columns { get; init; } = [];

    /// <summary>Gets the parameter names in order (CALL, function SELECT).</summary>
    public IReadOnlyList<string> Parameters { get; init; } = [];

    /// <summary>Gets the output of SHOW CREATE (CREATE).</summary>
    public string? CreateStatement { get; init; }

    /// <summary>Gets the quoting options (US-0504); scripts use the setting for .sql files.</summary>
    public DropFormatOptions Options { get; init; } = DropFormatOptions.Default;
}

/// <summary>Generates the SQL of Script As (US-0602).</summary>
public static class ScriptGenerator
{
    private const string Indent = "    ";

    /// <summary>Returns true when <paramref name="script"/> exists for <paramref name="kind"/>; see the table in US-0602.</summary>
    public static bool IsAvailable(CodeObjectKind kind, ScriptKind script) => kind switch
    {
        CodeObjectKind.Table => script is ScriptKind.Create or ScriptKind.Drop or ScriptKind.Select or ScriptKind.Insert or ScriptKind.Update or ScriptKind.Delete,
        CodeObjectKind.View => script is ScriptKind.Create or ScriptKind.Drop or ScriptKind.Select,
        CodeObjectKind.Procedure => script is ScriptKind.Create or ScriptKind.Drop or ScriptKind.Call,
        CodeObjectKind.Function => script is ScriptKind.Create or ScriptKind.Drop or ScriptKind.Select,
        _ => false,
    };

    /// <summary>Returns true when the statement needs the columns of the object.</summary>
    public static bool NeedsColumns(ScriptKind script) => script is ScriptKind.Select or ScriptKind.Insert or ScriptKind.Update or ScriptKind.Delete;

    /// <summary>Generates the script. Throws <see cref="InvalidOperationException"/> for a combination that is not available.</summary>
    public static string Generate(ScriptRequest request)
    {
        if (request is null)
        {
            throw new ArgumentNullException(nameof(request));
        }

        var code = request.CodeObject;
        if (!IsAvailable(code.Kind, request.Kind))
        {
            throw new InvalidOperationException($"{request.Kind} is not available for a {code.Kind}.");
        }

        var o = request.Options;
        var target = o.QuoteForSql(code.Database) + "." + o.QuoteForSql(code.Name);
        var nl = Environment.NewLine;

        return request.Kind switch
        {
            // SHOW CREATE quotes every name; "Only when needed" removes the backticks where that is safe.
            ScriptKind.Create => CodeDocument.Body(
                code,
                o.SqlQuoting == QuoteMode.Always
                    ? request.CreateStatement ?? string.Empty
                    : MySqlIdentifier.UnquoteWhereSafe(request.CreateStatement ?? string.Empty)),
            ScriptKind.Drop => $"DROP {code.Keyword} {target};{nl}",
            ScriptKind.Select when code.Kind == CodeObjectKind.Function => CallText(request) + nl,
            ScriptKind.Select => Select(request, target),
            ScriptKind.Insert => Insert(request, target),
            ScriptKind.Update => Update(request, target),
            ScriptKind.Delete => Delete(request, target),
            ScriptKind.Call => CallText(request) + nl,
            _ => throw new InvalidOperationException($"Unknown script {request.Kind}."),
        };
    }

    /// <summary>Returns the document title part: CREATE, DROP, SELECT, INSERT, UPDATE, DELETE or CALL.</summary>
    public static string Title(ScriptKind kind) => kind.ToString().ToUpperInvariant();

    private static string CallText(ScriptRequest request)
    {
        var code = request.CodeObject;
        var item = new DragItem(code.Kind == CodeObjectKind.Function ? DragObjectKind.Function : DragObjectKind.Procedure, code.Name, code.Database);

        // Scripts always name the database, so they run whatever database is active.
        return DropTextBuilder.BuildCall(item, request.Parameters, request.Options with { DatabasePrefixForRoutines = true });
    }

    private static string Select(ScriptRequest request, string target)
    {
        var nl = Environment.NewLine;
        var columns = request.Columns.Select(c => request.Options.QuoteForSql(c.Name)).ToList();
        var list = columns.Count == 0 ? Indent + "*" : string.Join("," + nl, columns.Select(c => Indent + c));
        return $"SELECT{nl}{list}{nl}FROM {target};{nl}";
    }

    private static string Insert(ScriptRequest request, string target)
    {
        var nl = Environment.NewLine;
        var columns = request.Columns.Where(c => !c.IsAutoIncrement && !c.IsGenerated).ToList();
        var names = string.Join(", ", columns.Select(c => request.Options.QuoteForSql(c.Name)));
        var values = string.Join(", ", columns.Select(c => DropTextBuilder.UserVariable(c.Name)));
        return $"INSERT INTO {target}{nl}{Indent}({names}){nl}VALUES{nl}{Indent}({values});{nl}";
    }

    private static string Update(ScriptRequest request, string target)
    {
        var nl = Environment.NewLine;
        var o = request.Options;
        var assignments = request.Columns
            .Where(c => !c.IsPrimaryKey && !c.IsAutoIncrement && !c.IsGenerated)
            .Select(c => $"{Indent}{o.QuoteForSql(c.Name)} = {DropTextBuilder.UserVariable(c.Name)}");
        var builder = new StringBuilder();
        builder.Append("UPDATE ").Append(target).Append(nl);
        builder.Append("SET").Append(nl).Append(string.Join("," + nl, assignments)).Append(nl);
        builder.Append(Where(request));
        return builder.ToString();
    }

    private static string Delete(ScriptRequest request, string target) =>
        $"DELETE FROM {target}{Environment.NewLine}{Where(request)}";

    // WHERE on the primary key. Without a primary key the statement must not touch any row until the user adds a
    // condition: WHERE 1 = 0, with a comment that says so.
    private static string Where(ScriptRequest request)
    {
        var nl = Environment.NewLine;
        var keys = request.Columns.Where(c => c.IsPrimaryKey).ToList();
        if (keys.Count == 0)
        {
            return $"-- No primary key: replace 1 = 0 with a condition before you run this statement.{nl}WHERE 1 = 0;{nl}";
        }

        var conditions = keys.Select(c => $"{request.Options.QuoteForSql(c.Name)} = {DropTextBuilder.UserVariable(c.Name)}");
        return $"WHERE {string.Join($"{nl}{Indent}AND ", conditions)};{nl}";
    }
}
