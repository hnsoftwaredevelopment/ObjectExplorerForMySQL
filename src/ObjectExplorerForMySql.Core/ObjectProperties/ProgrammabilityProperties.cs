using System;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Core.Metadata;

namespace ObjectExplorerForMySql.Core.ObjectProperties;

/// <summary>Properties of a stored procedure or function (US-0402).</summary>
public sealed class RoutineProperties : ServerObjectPropertiesBase
{
    private readonly RoutineInfo _routine;

    /// <summary>Initializes a new instance of the <see cref="RoutineProperties"/> class.</summary>
    public RoutineProperties(RoutineInfo routine, ConnectionDefinition? connection)
        : base(connection)
    {
        _routine = routine ?? throw new ArgumentNullException(nameof(routine));
    }

    /// <summary>Gets the return type of a function; empty for a procedure.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropReturns")]
    public string Returns => _routine.ReturnType ?? string.Empty;

    /// <summary>Gets a value indicating whether the routine is DETERMINISTIC.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropDeterministic")]
    public bool Deterministic => _routine.IsDeterministic;

    /// <summary>Gets the SQL data access.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropSqlDataAccess")]
    public string SqlDataAccess => _routine.SqlDataAccess ?? string.Empty;

    /// <summary>Gets the security type: DEFINER or INVOKER.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropSecurityType")]
    public string SecurityType => _routine.SecurityType ?? string.Empty;

    /// <summary>Gets the definer.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropDefiner")]
    public string Definer => _routine.Definer ?? string.Empty;

    /// <summary>Gets the creation time.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropCreated")]
    public DateTime? Created => _routine.Created;

    /// <summary>Gets the time of the last ALTER.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropModified")]
    public DateTime? Modified => _routine.LastAltered;

    /// <summary>Gets the routine name.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropName")]
    public string Name => _routine.Name;

    /// <summary>Gets the database.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropDatabase")]
    public string Database => _routine.Database;

    /// <summary>Gets the routine comment.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropComment")]
    public string Comment => _routine.Comment ?? string.Empty;

    /// <inheritdoc />
    protected override string TypeKey => _routine.Type == RoutineType.Function ? "TypeFunction" : "TypeStoredProcedure";

    /// <inheritdoc />
    public override string GetObjectName() => _routine.Name;
}

/// <summary>Properties of a routine parameter (US-0402).</summary>
public sealed class ParameterProperties : ServerObjectPropertiesBase
{
    private readonly ParameterInfo _parameter;
    private readonly RoutineInfo? _routine;

    /// <summary>Initializes a new instance of the <see cref="ParameterProperties"/> class.</summary>
    public ParameterProperties(ParameterInfo parameter, RoutineInfo? routine, ConnectionDefinition? connection)
        : base(connection)
    {
        _parameter = parameter ?? throw new ArgumentNullException(nameof(parameter));
        _routine = routine;
    }

    /// <summary>Gets IN, OUT or INOUT.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropMode")]
    public string Mode => _parameter.Mode switch
    {
        ParameterMode.Out => "OUT",
        ParameterMode.InOut => "INOUT",
        _ => "IN",
    };

    /// <summary>Gets the data type.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropDataType")]
    public string DataType => _parameter.DataType;

    /// <summary>Gets the 1-based position.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropOrdinalPosition")]
    public int OrdinalPosition => _parameter.Position;

    /// <summary>Gets the parameter name.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropName")]
    public string Name => _parameter.Name;

    /// <summary>Gets the routine.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropRoutine")]
    public string Routine => _routine?.Name ?? string.Empty;

    /// <summary>Gets the database.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropDatabase")]
    public string Database => _routine?.Database ?? string.Empty;

    /// <inheritdoc />
    protected override string TypeKey => "TypeParameter";

    /// <inheritdoc />
    public override string GetObjectName() => _parameter.Name;
}

/// <summary>Properties of a scheduled event (US-0402).</summary>
public sealed class EventProperties : ServerObjectPropertiesBase
{
    private readonly EventInfo _event;
    private readonly string _database;

    /// <summary>Initializes a new instance of the <see cref="EventProperties"/> class.</summary>
    public EventProperties(EventInfo @event, string database, ConnectionDefinition? connection)
        : base(connection)
    {
        _event = @event ?? throw new ArgumentNullException(nameof(@event));
        _database = database ?? string.Empty;
    }

    /// <summary>Gets ENABLED, DISABLED or REPLICA_SIDE_DISABLED.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropStatus")]
    public string Status => _event.Status;

    /// <summary>Gets the schedule in MySQL syntax.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropSchedule")]
    public string Schedule => _event.Schedule;

    /// <summary>Gets the start of a recurring event.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropStarts")]
    public DateTime? Starts => _event.Starts;

    /// <summary>Gets the end of a recurring event.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropEnds")]
    public DateTime? Ends => _event.Ends;

    /// <summary>Gets PRESERVE or NOT PRESERVE.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropOnCompletion")]
    public string OnCompletion => _event.OnCompletion ?? string.Empty;

    /// <summary>Gets the definer.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropDefiner")]
    public string Definer => _event.Definer ?? string.Empty;

    /// <summary>Gets the creation time.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropCreated")]
    public DateTime? Created => _event.Created;

    /// <summary>Gets the time of the last ALTER.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropModified")]
    public DateTime? Modified => _event.LastAltered;

    /// <summary>Gets the time of the last run.</summary>
    [LocalizedCategory(PropertyCategories.General)]
    [LocalizedDisplayName("PropLastExecuted")]
    public DateTime? LastExecuted => _event.LastExecuted;

    /// <summary>Gets the event name.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropName")]
    public string Name => _event.Name;

    /// <summary>Gets the database.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropDatabase")]
    public string Database => _database;

    /// <summary>Gets the event comment.</summary>
    [LocalizedCategory(PropertyCategories.Description)]
    [LocalizedDisplayName("PropComment")]
    public string Comment => _event.Comment ?? string.Empty;

    /// <inheritdoc />
    protected override string TypeKey => "TypeEvent";

    /// <inheritdoc />
    public override string GetObjectName() => _event.Name;
}
