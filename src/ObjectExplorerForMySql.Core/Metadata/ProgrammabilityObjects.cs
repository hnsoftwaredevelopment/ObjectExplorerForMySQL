using System;
using System.Globalization;

namespace ObjectExplorerForMySql.Core.Metadata;

/// <summary>Kind of a stored routine.</summary>
public enum RoutineType
{
    /// <summary>Stored procedure (CALL).</summary>
    Procedure,

    /// <summary>Stored function (used in expressions, has a return type).</summary>
    Function,
}

/// <summary>Stored procedure or function.</summary>
public sealed class RoutineInfo
{
    /// <summary>Initializes a new instance of the <see cref="RoutineInfo"/> class.</summary>
    public RoutineInfo(string database, string name, RoutineType type, string? returnType, string? comment)
    {
        Database = database;
        Name = name;
        Type = type;
        ReturnType = string.IsNullOrWhiteSpace(returnType) ? null : returnType;
        Comment = string.IsNullOrWhiteSpace(comment) ? null : comment;
    }

    /// <summary>Gets the database (schema) of the routine.</summary>
    public string Database { get; }

    /// <summary>Gets the routine name.</summary>
    public string Name { get; }

    /// <summary>Gets the routine type.</summary>
    public RoutineType Type { get; }

    /// <summary>Gets the return type of a function, for example <c>decimal(10,2)</c>; null for procedures.</summary>
    public string? ReturnType { get; }

    /// <summary>Gets the routine comment, or null.</summary>
    public string? Comment { get; }

    /// <summary>Gets the label: the name, and for a function <c>name (returns type)</c>.</summary>
    public string Label => Type == RoutineType.Function && ReturnType is not null ? $"{Name} (returns {ReturnType})" : Name;

    /// <summary>Gets the definer, for example root@localhost (US-0402).</summary>
    public string? Definer { get; init; }

    /// <summary>Gets the security type: DEFINER or INVOKER.</summary>
    public string? SecurityType { get; init; }

    /// <summary>Gets a value indicating whether the routine is declared DETERMINISTIC.</summary>
    public bool IsDeterministic { get; init; }

    /// <summary>Gets the SQL data access: CONTAINS SQL, NO SQL, READS SQL DATA or MODIFIES SQL DATA.</summary>
    public string? SqlDataAccess { get; init; }

    /// <summary>Gets the creation time.</summary>
    public DateTime? Created { get; init; }

    /// <summary>Gets the time of the last ALTER.</summary>
    public DateTime? LastAltered { get; init; }

    /// <summary>Gets the value of <c>ROUTINE_TYPE</c> in INFORMATION_SCHEMA.</summary>
    public string InformationSchemaType => Type == RoutineType.Function ? "FUNCTION" : "PROCEDURE";
}

/// <summary>Direction of a routine parameter.</summary>
public enum ParameterMode
{
    /// <summary>Input (also used for function parameters, which have no mode in MySQL).</summary>
    In,

    /// <summary>Output.</summary>
    Out,

    /// <summary>Input and output.</summary>
    InOut,
}

/// <summary>Parameter of a stored routine.</summary>
public sealed class ParameterInfo
{
    /// <summary>Initializes a new instance of the <see cref="ParameterInfo"/> class.</summary>
    public ParameterInfo(string name, ParameterMode mode, string dataType, int position)
    {
        Name = name;
        Mode = mode;
        DataType = dataType ?? string.Empty;
        Position = position;
    }

    /// <summary>Gets the parameter name.</summary>
    public string Name { get; }

    /// <summary>Gets the direction.</summary>
    public ParameterMode Mode { get; }

    /// <summary>Gets the full data type, for example <c>decimal(10,2)</c>.</summary>
    public string DataType { get; }

    /// <summary>Gets the 1-based position.</summary>
    public int Position { get; }

    /// <summary>Gets the label: <c>p_id (IN, int)</c>, <c>p_total (OUT, decimal(10,2))</c>.</summary>
    public string Label => $"{Name} ({ModeText(Mode)}, {DataType})";

    /// <summary>Parses <c>PARAMETER_MODE</c>; null (function parameters) means IN.</summary>
    public static ParameterMode ParseMode(string? mode) => (mode ?? string.Empty).Trim().ToUpperInvariant() switch
    {
        "OUT" => ParameterMode.Out,
        "INOUT" => ParameterMode.InOut,
        _ => ParameterMode.In,
    };

    private static string ModeText(ParameterMode mode) => mode switch
    {
        ParameterMode.Out => "OUT",
        ParameterMode.InOut => "INOUT",
        _ => "IN",
    };
}

/// <summary>Scheduled event of a database.</summary>
public sealed class EventInfo
{
    /// <summary>Initializes a new instance of the <see cref="EventInfo"/> class.</summary>
    public EventInfo(string name, string status, DateTime? executeAt, string? intervalValue, string? intervalField, DateTime? starts, DateTime? ends)
    {
        Name = name;
        Status = status ?? string.Empty;
        ExecuteAt = executeAt;
        IntervalValue = intervalValue;
        IntervalField = intervalField;
        Starts = starts;
        Ends = ends;
    }

    /// <summary>Gets the event name.</summary>
    public string Name { get; }

    /// <summary>Gets the status: ENABLED, DISABLED or REPLICA_SIDE_DISABLED / SLAVESIDE_DISABLED.</summary>
    public string Status { get; }

    /// <summary>Gets the moment of a one-time event.</summary>
    public DateTime? ExecuteAt { get; }

    /// <summary>Gets the interval value of a recurring event, for example "1".</summary>
    public string? IntervalValue { get; }

    /// <summary>Gets the interval unit of a recurring event, for example "DAY".</summary>
    public string? IntervalField { get; }

    /// <summary>Gets the start of a recurring event.</summary>
    public DateTime? Starts { get; }

    /// <summary>Gets the end of a recurring event.</summary>
    public DateTime? Ends { get; }

    /// <summary>Gets what happens after the last run: PRESERVE or NOT PRESERVE (US-0402).</summary>
    public string? OnCompletion { get; init; }

    /// <summary>Gets the definer, for example root@localhost.</summary>
    public string? Definer { get; init; }

    /// <summary>Gets the event comment, or null.</summary>
    public string? Comment { get; init; }

    /// <summary>Gets the creation time.</summary>
    public DateTime? Created { get; init; }

    /// <summary>Gets the time of the last ALTER.</summary>
    public DateTime? LastAltered { get; init; }

    /// <summary>Gets the time of the last run, or null when it never ran.</summary>
    public DateTime? LastExecuted { get; init; }

    /// <summary>Gets a value indicating whether the event runs on this server.</summary>
    public bool IsEnabled => string.Equals(Status, "ENABLED", StringComparison.OrdinalIgnoreCase);

    /// <summary>Gets the label: <c>name (enabled)</c> or <c>name (disabled)</c>.</summary>
    public string Label => IsEnabled ? $"{Name} (enabled)" : $"{Name} (disabled)";

    /// <summary>
    /// Gets the schedule in MySQL syntax, for example <c>EVERY 1 DAY STARTS '2026-01-01 00:00:00'</c>
    /// or <c>AT '2026-10-27 12:00:00'</c>.
    /// </summary>
    public string Schedule
    {
        get
        {
            if (ExecuteAt is { } at)
            {
                return $"AT '{Format(at)}'";
            }

            var schedule = $"EVERY {IntervalValue} {IntervalField?.Replace('_', ' ')}".Trim();
            if (Starts is { } starts)
            {
                schedule += $" STARTS '{Format(starts)}'";
            }

            if (Ends is { } ends)
            {
                schedule += $" ENDS '{Format(ends)}'";
            }

            return schedule;
        }
    }

    private static string Format(DateTime value) => value.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
}
