using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using Microsoft.VisualStudio.Language.StandardClassification;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Classification;
using Microsoft.VisualStudio.Utilities;
using ObjectExplorerForMySql.Core.Query;

namespace ObjectExplorerForMySql.Editor;

/// <summary>Colors documents of the MySQL Query Editor (US-0702).</summary>
[Export(typeof(IClassifierProvider))]
[ContentType(MySqlContentType.Name)]
internal sealed class MySqlClassifierProvider : IClassifierProvider
{
#pragma warning disable CS0649 // Assigned by MEF.
    [Import]
    private IClassificationTypeRegistryService? _registry;
#pragma warning restore CS0649

    /// <inheritdoc />
    public IClassifier? GetClassifier(ITextBuffer textBuffer) =>
        _registry is null ? null : textBuffer.Properties.GetOrCreateSingletonProperty(() => new MySqlClassifier(textBuffer, _registry));
}

/// <summary>
/// Maps <see cref="MySqlTokenizer"/> tokens to the standard Visual Studio classifications (Keyword, Comment, String,
/// Number, Identifier), so the colors follow the theme and Tools > Options > Fonts and Colors.
/// </summary>
internal sealed class MySqlClassifier : IClassifier
{
    private readonly ITextBuffer _buffer;
    private readonly IClassificationType[] _types;
    private readonly MySqlLineStates _states = new();
    private ITextSnapshot _snapshot;

    // The last tokenized line; Visual Studio asks for the same long line (for example a big INSERT) several times.
    private ITextSnapshotLine? _cachedLine;
    private IReadOnlyList<MySqlToken> _cachedTokens = Array.Empty<MySqlToken>();

    public MySqlClassifier(ITextBuffer buffer, IClassificationTypeRegistryService registry)
    {
        _buffer = buffer;
        _types = new IClassificationType[Enum.GetValues(typeof(MySqlTokenKind)).Length];
        _types[(int)MySqlTokenKind.Keyword] = registry.GetClassificationType(PredefinedClassificationTypeNames.Keyword);
        _types[(int)MySqlTokenKind.Comment] = registry.GetClassificationType(PredefinedClassificationTypeNames.Comment);
        _types[(int)MySqlTokenKind.String] = registry.GetClassificationType(PredefinedClassificationTypeNames.String);
        _types[(int)MySqlTokenKind.Number] = registry.GetClassificationType(PredefinedClassificationTypeNames.Number);
        _types[(int)MySqlTokenKind.QuotedIdentifier] = registry.GetClassificationType(PredefinedClassificationTypeNames.Identifier);
        _types[(int)MySqlTokenKind.Variable] = registry.GetClassificationType(PredefinedClassificationTypeNames.Identifier);

        _snapshot = buffer.CurrentSnapshot;
        Rebuild(_snapshot);

        // High priority: the states must be up to date before the editor asks for the colors of the new text.
        buffer.ChangedHighPriority += OnBufferChanged;
    }

    /// <inheritdoc />
    public event EventHandler<ClassificationChangedEventArgs>? ClassificationChanged;

    /// <inheritdoc />
    public IList<ClassificationSpan> GetClassificationSpans(SnapshotSpan span)
    {
        var result = new List<ClassificationSpan>();
        if (span.Snapshot != _snapshot)
        {
            // Normally impossible (ChangedHighPriority runs first); read everything again to be safe.
            _snapshot = span.Snapshot;
            Rebuild(_snapshot);
        }

        var first = span.Start.GetContainingLine().LineNumber;
        var last = span.End.GetContainingLine().LineNumber;
        for (var number = first; number <= last; number++)
        {
            var line = span.Snapshot.GetLineFromLineNumber(number);
            foreach (var token in TokensOf(line))
            {
                var tokenSpan = new SnapshotSpan(span.Snapshot, line.Start.Position + token.Start, token.Length);
                if (tokenSpan.IntersectsWith(span))
                {
                    result.Add(new ClassificationSpan(tokenSpan, _types[(int)token.Kind]));
                }
            }
        }

        return result;
    }

    private IReadOnlyList<MySqlToken> TokensOf(ITextSnapshotLine line)
    {
        if (_cachedLine is not null && _cachedLine.Snapshot == line.Snapshot && _cachedLine.LineNumber == line.LineNumber)
        {
            return _cachedTokens;
        }

        _cachedTokens = MySqlTokenizer.Tokenize(line.GetText(), _states.StartOf(line.LineNumber), out _);
        _cachedLine = line;
        return _cachedTokens;
    }

    private void Rebuild(ITextSnapshot snapshot)
    {
        _cachedLine = null;
        _states.Rebuild(snapshot.LineCount, i => snapshot.GetLineFromLineNumber(i).GetText());
    }

    private void OnBufferChanged(object? sender, TextContentChangedEventArgs e)
    {
        var after = e.After;
        _snapshot = after;
        _cachedLine = null;
        if (e.Changes.Count == 0)
        {
            return;
        }

        var firstLine = int.MaxValue;
        var lastLine = 0;
        foreach (var change in e.Changes)
        {
            firstLine = Math.Min(firstLine, after.GetLineNumberFromPosition(change.NewPosition));
            lastLine = Math.Max(lastLine, after.GetLineNumberFromPosition(change.NewEnd));
        }

        var lastRead = _states.Update(firstLine, lastLine, after.LineCount, i => after.GetLineFromLineNumber(i).GetText());

        // The editor colors the changed lines itself; lines after them changed only when a comment or string opened
        // or closed.
        if (lastRead > lastLine && ClassificationChanged is { } handler)
        {
            var start = after.GetLineFromLineNumber(lastLine + 1).Start;
            var end = after.GetLineFromLineNumber(lastRead).EndIncludingLineBreak;
            handler(this, new ClassificationChangedEventArgs(new SnapshotSpan(start, end)));
        }
    }
}
