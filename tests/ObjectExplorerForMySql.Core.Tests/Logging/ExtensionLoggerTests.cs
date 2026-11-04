using System;
using System.Collections.Generic;
using ObjectExplorerForMySql.Core.Logging;
using Xunit;

namespace ObjectExplorerForMySql.Core.Tests.Logging;

public class ExtensionLoggerTests
{
    private static readonly DateTimeOffset _fixedTime = new(2026, 10, 26, 14, 5, 9, 123, TimeSpan.FromHours(1));

    [Fact]
    public void Log_FormatsTimeLevelAndMessage()
    {
        var sink = new ListSink();
        var logger = new ExtensionLogger(sink, LogLevel.Debug, () => _fixedTime);

        logger.Info("Connected to localhost:3306.");

        Assert.Equal("[14:05:09.123] INFO   Connected to localhost:3306.", Assert.Single(sink.Entries));
    }

    [Theory]
    [InlineData(LogLevel.Error, "ERROR  ")]
    [InlineData(LogLevel.Warning, "WARN   ")]
    [InlineData(LogLevel.Info, "INFO   ")]
    [InlineData(LogLevel.Debug, "DEBUG  ")]
    public void Log_UsesFixedWidthLevelText(LogLevel level, string expectedLevelText)
    {
        var sink = new ListSink();
        var logger = new ExtensionLogger(sink, LogLevel.Debug, () => _fixedTime);

        logger.Log(level, "x");

        Assert.Equal("[14:05:09.123] " + expectedLevelText + "x", Assert.Single(sink.Entries));
    }

    [Fact]
    public void Log_DefaultMinimumLevelIsInfo_DebugIsSkipped()
    {
        var sink = new ListSink();
        var logger = new ExtensionLogger(sink, clock: () => _fixedTime);

        logger.Debug("query");
        logger.Info("info");
        logger.Warning("warning");
        logger.Error("error");

        Assert.Equal(3, sink.Entries.Count);
        Assert.DoesNotContain(sink.Entries, e => e.Contains("query"));
    }

    [Fact]
    public void MinimumLevel_CanBeChangedAtRuntime()
    {
        var sink = new ListSink();
        var logger = new ExtensionLogger(sink, LogLevel.Info, () => _fixedTime);

        logger.Debug("hidden");
        logger.MinimumLevel = LogLevel.Debug;
        logger.Debug("visible");

        Assert.Contains("visible", Assert.Single(sink.Entries));
    }

    [Theory]
    [InlineData(LogLevel.Error, LogLevel.Error, true)]
    [InlineData(LogLevel.Error, LogLevel.Warning, false)]
    [InlineData(LogLevel.Info, LogLevel.Warning, true)]
    [InlineData(LogLevel.Info, LogLevel.Debug, false)]
    [InlineData(LogLevel.Debug, LogLevel.Debug, true)]
    public void IsEnabled_ComparesWithMinimumLevel(LogLevel minimum, LogLevel level, bool expected)
    {
        var logger = new ExtensionLogger(new ListSink(), minimum);

        Assert.Equal(expected, logger.IsEnabled(level));
    }

    [Fact]
    public void Log_AppendsExceptionOnNextLine()
    {
        var sink = new ListSink();
        var logger = new ExtensionLogger(sink, LogLevel.Info, () => _fixedTime);

        logger.Error("Refresh failed.", new InvalidOperationException("Table is gone."));

        var entry = Assert.Single(sink.Entries);
        Assert.StartsWith("[14:05:09.123] ERROR  Refresh failed." + Environment.NewLine, entry);
        Assert.Contains("System.InvalidOperationException: Table is gone.", entry);
    }

    [Fact]
    public void Log_MasksPasswordInMessage()
    {
        var sink = new ListSink();
        var logger = new ExtensionLogger(sink, LogLevel.Debug, () => _fixedTime);

        logger.Debug("Opening Server=db;User ID=root;Password=Secret123");

        var entry = Assert.Single(sink.Entries);
        Assert.DoesNotContain("Secret123", entry);
        Assert.Contains("Password=*****", entry);
    }

    [Fact]
    public void Log_MasksPasswordInExceptionMessage()
    {
        var sink = new ListSink();
        var logger = new ExtensionLogger(sink, LogLevel.Info, () => _fixedTime);

        logger.Error("Connect failed.", new ArgumentException("Bad connection string: Server=db;Pwd=Secret123"));

        Assert.DoesNotContain("Secret123", Assert.Single(sink.Entries));
    }

    [Fact]
    public void Log_DoesNotThrowWhenSinkFails()
    {
        var logger = new ExtensionLogger(new ThrowingSink());

        var exception = Record.Exception(() => logger.Error("x"));

        Assert.Null(exception);
    }

    [Fact]
    public void Constructor_RejectsNullSink()
    {
        Assert.Throws<ArgumentNullException>(() => new ExtensionLogger(null!));
    }

    private sealed class ListSink : ILogSink
    {
        public List<string> Entries { get; } = [];

        public void Write(string formattedEntry) => Entries.Add(formattedEntry);
    }

    private sealed class ThrowingSink : ILogSink
    {
        public void Write(string formattedEntry) => throw new InvalidOperationException("Output window gone.");
    }
}
