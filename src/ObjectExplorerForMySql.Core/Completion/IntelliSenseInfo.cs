using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Metadata;

namespace ObjectExplorerForMySql.Core.Completion;

/// <summary>Texts of quick info. The extension fills them from its resources (ADR-005); the defaults are English.</summary>
public sealed class IntelliSenseTexts
{
    /// <summary>Gets or sets the kind of a column.</summary>
    public string Column { get; set; } = "column";

    /// <summary>Gets or sets the kind of a table.</summary>
    public string Table { get; set; } = "table";

    /// <summary>Gets or sets the kind of a view.</summary>
    public string View { get; set; } = "view";

    /// <summary>Gets or sets the kind of a database.</summary>
    public string Database { get; set; } = "database";

    /// <summary>Gets or sets the text of a primary key column.</summary>
    public string PrimaryKey { get; set; } = "primary key";

    /// <summary>Gets or sets the text of a foreign key column.</summary>
    public string ForeignKey { get; set; } = "foreign key";
}

/// <summary>A parameter in a signature; <see cref="Start"/> and <see cref="Length"/> point into the signature text.</summary>
public sealed record SignatureParameter(string Name, int Start, int Length);

/// <summary>The signature of a procedure or function for parameter info (US-1402).</summary>
/// <param name="Content">For example <c>film_in_stock(IN p_film_id int, OUT p_film_count int)</c>.</param>
/// <param name="Parameters">The parameters with their place in <paramref name="Content"/>.</param>
/// <param name="Documentation">The routine comment, or null.</param>
/// <param name="CurrentParameter">0-based index of the parameter at the caret.</param>
public sealed record RoutineSignature(string Content, IReadOnlyList<SignatureParameter> Parameters, string? Documentation, int CurrentParameter);

/// <summary>Parameter info and quick info from the cached metadata (US-1402). Nothing is shown when a read fails.</summary>
public static class IntelliSenseInfoBuilder
{
    /// <summary>Returns the signature of the routine called at the caret, or null when it is not a known routine.</summary>
    public static async Task<RoutineSignature?> SignatureAsync(
        SignatureContext context, SchemaCache cache, ConnectionDefinition definition, string? database, CancellationToken cancellationToken)
    {
        if (context is null || cache is null || definition is null)
        {
            return null;
        }

        var routine = await FindRoutineAsync(cache, definition, context.Database ?? database, context.Name, cancellationToken).ConfigureAwait(false);
        if (routine is null)
        {
            return null;
        }

        var parameters = await TryAsync(() => cache.GetParametersAsync(definition, routine, cancellationToken)).ConfigureAwait(false);
        return parameters is null ? null : Signature(routine, parameters, context.ArgumentIndex);
    }

    /// <summary>Builds the signature text; procedures show the mode of each parameter, functions their return type.</summary>
    public static RoutineSignature Signature(RoutineInfo routine, IReadOnlyList<ParameterInfo> parameters, int currentParameter)
    {
        if (routine is null)
        {
            throw new ArgumentNullException(nameof(routine));
        }

        var text = new StringBuilder(routine.Name).Append('(');
        var list = new List<SignatureParameter>();
        foreach (var parameter in (parameters ?? []).OrderBy(p => p.Position))
        {
            if (list.Count > 0)
            {
                text.Append(", ");
            }

            var start = text.Length;
            if (routine.Type == RoutineType.Procedure)
            {
                text.Append(parameter.Mode switch { ParameterMode.Out => "OUT", ParameterMode.InOut => "INOUT", _ => "IN" }).Append(' ');
            }

            text.Append(parameter.Name).Append(' ').Append(parameter.DataType);
            list.Add(new SignatureParameter(parameter.Name, start, text.Length - start));
        }

        text.Append(')');
        if (routine.Type == RoutineType.Function && routine.ReturnType is { Length: > 0 } returns)
        {
            text.Append(" RETURNS ").Append(returns);
        }

        return new RoutineSignature(text.ToString(), list, string.IsNullOrWhiteSpace(routine.Comment) ? null : routine.Comment, currentParameter);
    }

    /// <summary>
    /// Returns the quick info text for the name under the mouse: a column (type, NULL or NOT NULL, key, comment), a
    /// table or view (engine, comment), a routine (signature) or a database. Null when the name is not known.
    /// </summary>
    public static async Task<string?> QuickInfoAsync(
        SymbolContext context, SchemaCache cache, ConnectionDefinition definition, string? database, IntelliSenseTexts texts, CancellationToken cancellationToken)
    {
        if (context is null || cache is null || definition is null || texts is null)
        {
            return null;
        }

        var qualifier = context.Qualifier;
        if (qualifier.Count == 2)
        {
            return await ColumnAsync(cache, definition, qualifier[0], qualifier[1], context.Name, texts, cancellationToken).ConfigureAwait(false);
        }

        if (qualifier.Count == 1)
        {
            var q = qualifier[0];
            if ((context.Tables.FirstOrDefault(t => Same(t.Alias, q)) ?? context.Tables.FirstOrDefault(t => t.Alias is null && Same(t.Name, q))) is { } reference)
            {
                return await ColumnAsync(cache, definition, reference.Database ?? database, reference.Name, context.Name, texts, cancellationToken).ConfigureAwait(false);
            }

            if (database is not null && (await SchemaAsync(cache, definition, database, cancellationToken).ConfigureAwait(false))?.Tables.Any(t => Same(t.Name, q)) == true)
            {
                return await ColumnAsync(cache, definition, database, q, context.Name, texts, cancellationToken).ConfigureAwait(false);
            }

            return await ObjectAsync(cache, definition, q, context.Name, texts, cancellationToken).ConfigureAwait(false);
        }

        // A table of the statement can name its database.
        var tableDatabase = context.Tables.FirstOrDefault(t => Same(t.Name, context.Name))?.Database ?? database;
        if (tableDatabase is not null && await ObjectAsync(cache, definition, tableDatabase, context.Name, texts, cancellationToken).ConfigureAwait(false) is { } info)
        {
            return info;
        }

        var databases = await TryAsync(() => cache.GetDatabasesAsync(definition, cancellationToken)).ConfigureAwait(false);
        if (databases?.FirstOrDefault(d => Same(d, context.Name)) is { } name)
        {
            return $"{name} ({texts.Database})";
        }

        foreach (var table in context.Tables)
        {
            if (await ColumnAsync(cache, definition, table.Database ?? database, table.Name, context.Name, texts, cancellationToken).ConfigureAwait(false) is { } column)
            {
                return column;
            }
        }

        return null;
    }

    private static async Task<string?> ObjectAsync(
        SchemaCache cache, ConnectionDefinition definition, string database, string name, IntelliSenseTexts texts, CancellationToken cancellationToken)
    {
        var schema = await SchemaAsync(cache, definition, database, cancellationToken).ConfigureAwait(false);
        if (schema is null)
        {
            return null;
        }

        if (schema.Tables.FirstOrDefault(t => Same(t.Name, name)) is { } table)
        {
            return Lines($"{table.Database}.{table.Name} ({(table.IsView ? texts.View : texts.Table)})", table.Engine, table.Comment);
        }

        if (schema.Routines.FirstOrDefault(r => Same(r.Name, name)) is { } routine
            && await TryAsync(() => cache.GetParametersAsync(definition, routine, cancellationToken)).ConfigureAwait(false) is { } parameters)
        {
            var signature = Signature(routine, parameters, 0);
            return Lines(signature.Content, signature.Documentation);
        }

        return null;
    }

    private static async Task<string?> ColumnAsync(
        SchemaCache cache, ConnectionDefinition definition, string? database, string table, string name, IntelliSenseTexts texts, CancellationToken cancellationToken)
    {
        if (database is null)
        {
            return null;
        }

        var columns = await TryAsync(() => cache.GetColumnsAsync(definition, database, table, cancellationToken)).ConfigureAwait(false);
        if (columns?.FirstOrDefault(c => Same(c.Name, name)) is not { } column)
        {
            return null;
        }

        var details = new List<string> { column.ColumnType + (column.IsNullable ? " NULL" : " NOT NULL") };
        if (column.IsPrimaryKey)
        {
            details.Add(texts.PrimaryKey);
        }

        if (column.IsForeignKey)
        {
            details.Add(texts.ForeignKey);
        }

        if (column.Extra is { Length: > 0 } extra)
        {
            details.Add(extra);
        }

        return Lines($"{table}.{column.Name} ({texts.Column})", string.Join(", ", details), column.Comment);
    }

    private static async Task<RoutineInfo?> FindRoutineAsync(SchemaCache cache, ConnectionDefinition definition, string? database, string name, CancellationToken cancellationToken) =>
        database is null ? null : (await SchemaAsync(cache, definition, database, cancellationToken).ConfigureAwait(false))?.Routines.FirstOrDefault(r => Same(r.Name, name));

    private static Task<DatabaseSchema?> SchemaAsync(SchemaCache cache, ConnectionDefinition definition, string database, CancellationToken cancellationToken) =>
        TryAsync(() => cache.GetDatabaseAsync(definition, database, cancellationToken));

    private static string Lines(params string?[] lines) => string.Join(Environment.NewLine, lines.Where(l => !string.IsNullOrWhiteSpace(l)));

    private static async Task<T?> TryAsync<T>(Func<Task<T>> read)
        where T : class
    {
        try
        {
            return await read().ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static bool Same(string? a, string? b) => string.Equals(a, b, StringComparison.OrdinalIgnoreCase);
}
