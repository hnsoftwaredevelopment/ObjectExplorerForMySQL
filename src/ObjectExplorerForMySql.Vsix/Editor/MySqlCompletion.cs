using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.Composition;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Core.Imaging;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion;
using Microsoft.VisualStudio.Language.Intellisense.AsyncCompletion.Data;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Text.Adornments;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.Utilities;
using ObjectExplorerForMySql.Core.Completion;
using ObjectExplorerForMySql.Core.Connections;
using ObjectExplorerForMySql.Settings;

namespace ObjectExplorerForMySql.Editor;

/// <summary>Adds the MySQL completion list to documents of the MySQL Query Editor (US-1401).</summary>
[Export(typeof(IAsyncCompletionSourceProvider))]
[Name("MySqlCompletion")]
[ContentType(MySqlContentType.Name)]
internal sealed class MySqlCompletionSourceProvider : IAsyncCompletionSourceProvider
{
    /// <inheritdoc />
    public IAsyncCompletionSource GetOrCreate(ITextView textView) =>
        textView.Properties.GetOrCreateSingletonProperty(() => new MySqlCompletionSource(textView));
}

/// <summary>
/// Keywords, databases, tables, views, routines and columns of the linked connection. The metadata comes from
/// <see cref="QueryEditorServices.Schema"/>; the list never asks for a password and typing never waits for it.
/// </summary>
internal sealed class MySqlCompletionSource : IAsyncCompletionSource
{
    private const string DescriptionKey = "MySqlCompletion.Description";

    private static readonly Dictionary<CompletionKind, ImageElement> _icons = new()
    {
        [CompletionKind.Keyword] = Icon(KnownMonikers.IntellisenseKeyword),
        [CompletionKind.Database] = Icon(KnownMonikers.Database),
        [CompletionKind.Table] = Icon(KnownMonikers.Table),
        [CompletionKind.View] = Icon(KnownMonikers.View),
        [CompletionKind.Procedure] = Icon(KnownMonikers.StoredProcedure),
        [CompletionKind.Function] = Icon(KnownMonikers.ScalarFunction),
        [CompletionKind.Column] = Icon(KnownMonikers.Column),
    };

    private readonly ITextView _view;

    public MySqlCompletionSource(ITextView view)
    {
        _view = view;
    }

    /// <inheritdoc />
    public CompletionStartData InitializeCompletion(CompletionTrigger trigger, SnapshotPoint triggerLocation, CancellationToken token)
    {
        var reason = trigger.Reason;
        if (reason == CompletionTriggerReason.Insertion)
        {
            var c = trigger.Character;
            if (!(char.IsLetter(c) || c is '_' or '.' or '`'))
            {
                return CompletionStartData.DoesNotParticipateInCompletion;
            }
        }
        else if (reason is not (CompletionTriggerReason.Invoke or CompletionTriggerReason.InvokeAndCommitIfUnique))
        {
            return CompletionStartData.DoesNotParticipateInCompletion;
        }

        var snapshot = triggerLocation.Snapshot;
        var caret = Caret(trigger, triggerLocation);
        var context = SqlCompletionAnalyzer.Analyze(snapshot.GetText(), caret);
        if (context.Situation == CompletionSituation.None
            || (reason == CompletionTriggerReason.Insertion && trigger.Character == '.' && context.Situation != CompletionSituation.Members))
        {
            return CompletionStartData.DoesNotParticipateInCompletion;
        }

        return new CompletionStartData(CompletionParticipation.ProvidesItems, new SnapshotSpan(snapshot, context.WordStart, caret - context.WordStart));
    }

    /// <inheritdoc />
    public async Task<CompletionContext> GetCompletionContextAsync(IAsyncCompletionSession session, CompletionTrigger trigger, SnapshotPoint triggerLocation, SnapshotSpan applicableToSpan, CancellationToken token)
    {
        var snapshot = applicableToSpan.Snapshot;
        var context = SqlCompletionAnalyzer.Analyze(snapshot.GetText(), applicableToSpan.End.Position);
        if (QueryEditorServices.Schema is not { } cache)
        {
            return CompletionContext.Empty;
        }

        // The link of the document and the server node live on the UI thread.
        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
        var (definition, database) = QueryEditorServices.ConnectionOf(_view.TextDataModel.DocumentBuffer);
        await TaskScheduler.Default;

        var entries = await CompletionBuilder.BuildAsync(context, cache, definition, database, DragDropSettingsMonitor.Current, token);
        var members = context.Situation == CompletionSituation.Members;
        var items = entries.Select((entry, index) =>
        {
            // After a dot the columns keep the order of the table.
            var sort = members ? index.ToString("D5", CultureInfo.InvariantCulture) : entry.Display;
            // After a typed backtick the typed text starts with it, so the list filters on the quoted name.
            var filter = context.InBackticks ? entry.Insert : entry.Display;
            var item = new CompletionItem(entry.Display, this, _icons[entry.Kind], ImmutableArray<CompletionFilter>.Empty, string.Empty, entry.Insert, sort, filter, ImmutableArray<ImageElement>.Empty);
            if (entry.Description is { Length: > 0 } description)
            {
                item.Properties.AddProperty(DescriptionKey, description);
            }

            return item;
        }).ToImmutableArray();

        return new CompletionContext(items);
    }

    /// <inheritdoc />
    public Task<object?> GetDescriptionAsync(IAsyncCompletionSession session, CompletionItem item, CancellationToken token) =>
        Task.FromResult<object?>(item.Properties.TryGetProperty(DescriptionKey, out string description) ? description : null);

    private static ImageElement Icon(Microsoft.VisualStudio.Imaging.Interop.ImageMoniker moniker) => new(new ImageId(moniker.Guid, moniker.Id));

    // For a typed character the trigger point can be before or after it; the list starts after it.
    private static int Caret(CompletionTrigger trigger, SnapshotPoint location)
    {
        var position = location.Position;
        if (trigger.Reason == CompletionTriggerReason.Insertion && position < location.Snapshot.Length && location.Snapshot[position] == trigger.Character)
        {
            position++;
        }

        return position;
    }
}

/// <summary>Commits the chosen name on Tab, Enter, and on the characters that end a name (not on a space: an alias would be replaced).</summary>
[Export(typeof(IAsyncCompletionCommitManagerProvider))]
[Name("MySqlCompletionCommit")]
[ContentType(MySqlContentType.Name)]
internal sealed class MySqlCommitManagerProvider : IAsyncCompletionCommitManagerProvider
{
    /// <inheritdoc />
    public IAsyncCompletionCommitManager GetOrCreate(ITextView textView) =>
        textView.Properties.GetOrCreateSingletonProperty(() => new MySqlCommitManager());
}

/// <summary>See <see cref="MySqlCommitManagerProvider"/>.</summary>
internal sealed class MySqlCommitManager : IAsyncCompletionCommitManager
{
    private static readonly ImmutableArray<char> _commitCharacters = ['.', ',', '(', ')', ';'];

    /// <inheritdoc />
    public IEnumerable<char> PotentialCommitCharacters => _commitCharacters;

    /// <inheritdoc />
    public bool ShouldCommitCompletion(IAsyncCompletionSession session, SnapshotPoint location, char typedChar, CancellationToken token) =>
        _commitCharacters.Contains(typedChar);

    /// <inheritdoc />
    public CommitResult TryCommit(IAsyncCompletionSession session, ITextBuffer buffer, CompletionItem item, char typedChar, CancellationToken token) =>
        CommitResult.Unhandled;
}
