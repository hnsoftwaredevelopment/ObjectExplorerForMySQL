using System;
using ObjectExplorerForMySql.Core.Metadata;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Metadata;

public class ProgrammabilityObjectsTests
{
    [Theory]
    [InlineData(null, ParameterMode.In)]
    [InlineData("IN", ParameterMode.In)]
    [InlineData("out", ParameterMode.Out)]
    [InlineData(" INOUT ", ParameterMode.InOut)]
    public void ParseMode_MapsInformationSchemaValues(string? value, ParameterMode expected) =>
        Assert.Equal(expected, ParameterInfo.ParseMode(value));

    [Fact]
    public void Routine_ProcedureLabelIsNameAndTypeIsProcedure()
    {
        var routine = new RoutineInfo("sakila", "film_in_stock", RoutineType.Procedure, "", " ");

        Assert.Equal("film_in_stock", routine.Label);
        Assert.Equal("PROCEDURE", routine.InformationSchemaType);
        Assert.Null(routine.ReturnType);
        Assert.Null(routine.Comment);
    }

    [Fact]
    public void Routine_FunctionLabelShowsReturnType()
    {
        var routine = new RoutineInfo("sakila", "inventory_in_stock", RoutineType.Function, "tinyint(1)", null);

        Assert.Equal("inventory_in_stock (returns tinyint(1))", routine.Label);
        Assert.Equal("FUNCTION", routine.InformationSchemaType);
    }

    [Fact]
    public void Event_RecurringScheduleWithStartAndEnd()
    {
        var e = new EventInfo("e", "ENABLED", null, "15", "MINUTE_SECOND", new DateTime(2026, 1, 1, 8, 0, 0), new DateTime(2026, 12, 31, 23, 59, 59));

        Assert.True(e.IsEnabled);
        Assert.Equal("EVERY 15 MINUTE SECOND STARTS '2026-01-01 08:00:00' ENDS '2026-12-31 23:59:59'", e.Schedule);
    }

    [Theory]
    [InlineData("DISABLED")]
    [InlineData("REPLICA_SIDE_DISABLED")]
    [InlineData("SLAVESIDE_DISABLED")]
    public void Event_NonEnabledStatusShowsDisabled(string status) =>
        Assert.Equal("e (disabled)", new EventInfo("e", status, DateTime.Now, null, null, null, null).Label);
}
