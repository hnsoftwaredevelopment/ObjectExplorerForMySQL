using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel.Composition;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.VisualStudio.Utilities;
using ObjectExplorerForMySql.Core.Completion;

namespace ObjectExplorerForMySql.Editor;

/// <summary>Quick info on hover in the MySQL Query Editor (US-1402).</summary>
[Export(typeof(IAsyncQuickInfoSourceProvider))]
[Name("MySqlQuickInfo")]
[ContentType(MySqlContentType.Name)]
internal sealed class MySqlQuickInfoSourceProvider : IAsyncQuickInfoSourceProvider
{
    /// <inheritdoc />
    public IAsyncQuickInfoSource TryCreateQuickInfoSource(ITextBuffer textBuffer) =>
        textBuffer.Properties.GetOrCreateSingletonProperty(() => new MySqlQuickInfoSource(textBuffer));
}

/// <summary>Column: type, NULL, key, comment. Table or view: engine, comment. Routine: signature. Database.</summary>
internal sealed class MySqlQuickInfoSource : IAsyncQuickInfoSource
{
    private readonly ITextBuffer _buffer;

    public MySqlQuickInfoSource(ITextBuffer buffer)
    {
        _buffer = buffer;
    }

    /// <inheritdoc />
    public async Task<QuickInfoItem?> GetQuickInfoItemAsync(IAsyncQuickInfoSession session, CancellationToken cancellationToken)
    {
        if (session.GetTriggerPoint(_buffer.CurrentSnapshot) is not { } point
            || QueryEditorServices.Schema is not { } cache
            || SqlCompletionAnalyzer.SymbolAt(point.Snapshot.GetText(), point.Position) is not { } symbol)
        {
            return null;
        }

        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var (definition, database) = QueryEditorServices.ConnectionOf(_buffer);
        await TaskScheduler.Default;
        if (definition is null)
        {
            return null;
        }

        var text = await IntelliSenseInfoBuilder.QuickInfoAsync(symbol, cache, definition, database, QueryEditorServices.IntelliSenseTexts, cancellationToken);
        if (text is null)
        {
            return null;
        }

        var span = point.Snapshot.CreateTrackingSpan(symbol.Start, symbol.End - symbol.Start, SpanTrackingMode.EdgeInclusive);
        return new QuickInfoItem(span, text);
    }

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

/// <summary>Parameter info for procedures and functions (US-1402). The signature is prepared by <see cref="SignatureHelpCommandFilter"/>.</summary>
[Export(typeof(ISignatureHelpSourceProvider))]
[Name("MySqlSignatureHelp")]
[ContentType(MySqlContentType.Name)]
internal sealed class MySqlSignatureHelpSourceProvider : ISignatureHelpSourceProvider
{
    /// <inheritdoc />
    public ISignatureHelpSource TryCreateSignatureHelpSource(ITextBuffer textBuffer) => new MySqlSignatureHelpSource();
}

/// <summary>Adds the prepared signature of the session.</summary>
internal sealed class MySqlSignatureHelpSource : ISignatureHelpSource
{
    /// <inheritdoc />
    public void AugmentSignatureHelpSession(ISignatureHelpSession session, IList<ISignature> signatures)
    {
        if (session.Properties.TryGetProperty(typeof(MySqlSignature), out MySqlSignature signature))
        {
            signatures.Add(signature);
        }
    }

    /// <inheritdoc />
    public ISignature? GetBestMatch(ISignatureHelpSession session) => session.Signatures.FirstOrDefault();

    /// <inheritdoc />
    public void Dispose()
    {
    }
}

/// <summary>One routine signature with its parameters.</summary>
internal sealed class MySqlSignature : ISignature
{
    private IParameter? _currentParameter;

    public MySqlSignature(RoutineSignature signature, ITrackingSpan applicableToSpan)
    {
        Content = signature.Content;
        PrettyPrintedContent = signature.Content;
        Documentation = signature.Documentation ?? string.Empty;
        ApplicableToSpan = applicableToSpan;
        Parameters = new ReadOnlyCollection<IParameter>(signature.Parameters.Select(p => (IParameter)new MySqlParameter(this, p)).ToList());
        SetCurrent(signature.CurrentParameter);
    }

    /// <inheritdoc />
    public event EventHandler<CurrentParameterChangedEventArgs>? CurrentParameterChanged;

    /// <inheritdoc />
    public ITrackingSpan ApplicableToSpan { get; }

    /// <inheritdoc />
    public string Content { get; }

    /// <inheritdoc />
    public string PrettyPrintedContent { get; }

    /// <inheritdoc />
    public string Documentation { get; }

    /// <inheritdoc />
    public ReadOnlyCollection<IParameter> Parameters { get; }

    /// <inheritdoc />
    public IParameter? CurrentParameter => _currentParameter;

    /// <summary>Marks the parameter at <paramref name="index"/>; past the last one nothing is marked.</summary>
    public void SetCurrent(int index)
    {
        var next = index >= 0 && index < Parameters.Count ? Parameters[index] : null;
        if (!ReferenceEquals(next, _currentParameter))
        {
            var previous = _currentParameter;
            _currentParameter = next;
            CurrentParameterChanged?.Invoke(this, new CurrentParameterChangedEventArgs(previous, next));
        }
    }

    private sealed class MySqlParameter(ISignature signature, SignatureParameter parameter) : IParameter
    {
        public ISignature Signature { get; } = signature;

        public string Name { get; } = parameter.Name;

        public string Documentation => string.Empty;

        public Span Locus { get; } = new(parameter.Start, parameter.Length);

        public Span PrettyPrintedLocus => Locus;
    }
}

/// <summary>Installs <see cref="SignatureHelpCommandFilter"/> on every view of the MySQL Query Editor.</summary>
[Export(typeof(IVsTextViewCreationListener))]
[ContentType(MySqlContentType.Name)]
[TextViewRole(PredefinedTextViewRoles.Editable)]
internal sealed class SignatureHelpCommandFilterInstaller : IVsTextViewCreationListener
{
#pragma warning disable CS0649 // Assigned by MEF.
    [Import]
    private IVsEditorAdaptersFactoryService? _adapters;

    [Import]
    private ISignatureHelpBroker? _broker;
#pragma warning restore CS0649

    /// <inheritdoc />
    public void VsTextViewCreated(IVsTextView textViewAdapter)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_adapters?.GetWpfTextView(textViewAdapter) is not { } view || _broker is null)
        {
            return;
        }

        var filter = new SignatureHelpCommandFilter(view, _broker);
        if (ErrorHandler.Succeeded(textViewAdapter.AddCommandFilter(filter, out var next)))
        {
            filter.Next = next;
        }
    }
}

/// <summary>
/// Opens parameter info after <c>(</c>, <c>,</c> and Ctrl+Shift+Space, and follows the caret: the parameter at the
/// caret is marked; outside the call the tip closes. The parameters are read in the background, so typing never waits.
/// </summary>
internal sealed class SignatureHelpCommandFilter : IOleCommandTarget
{
    private readonly IWpfTextView _view;
    private readonly ISignatureHelpBroker _broker;
    private ISignatureHelpSession? _session;
    private int _sessionParen = -1;
    private int _request;

    public SignatureHelpCommandFilter(IWpfTextView view, ISignatureHelpBroker broker)
    {
        _view = view;
        _broker = broker;
        _view.Caret.PositionChanged += OnCaretMoved;
        _view.Closed += (_, _) => _view.Caret.PositionChanged -= OnCaretMoved;
    }

    public IOleCommandTarget? Next { get; set; }

    public int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return Next?.QueryStatus(ref pguidCmdGroup, cCmds, prgCmds, pCmdText) ?? (int)Constants.OLECMDERR_E_NOTSUPPORTED;
    }

    public int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var typed = pguidCmdGroup == VSConstants.VSStd2K && nCmdID == (uint)VSConstants.VSStd2KCmdID.TYPECHAR && pvaIn != IntPtr.Zero
            ? (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn)
            : '\0';
        var paramInfo = pguidCmdGroup == VSConstants.VSStd2K && nCmdID == (uint)VSConstants.VSStd2KCmdID.PARAMINFO;

        var result = Next?.Exec(ref pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut) ?? (int)Constants.OLECMDERR_E_NOTSUPPORTED;
        if (paramInfo || typed is '(' or ',')
        {
            Trigger();
            if (paramInfo)
            {
                return VSConstants.S_OK;
            }
        }

        return result;
    }

    private void OnCaretMoved(object? sender, CaretPositionChangedEventArgs e) => FollowCaret();

    // Marks the parameter at the caret, or closes the tip when the caret left the call.
    private void FollowCaret()
    {
        if (_session is null)
        {
            return;
        }

        var context = Context();
        if (context is null || context.OpenParen != _sessionParen)
        {
            Dismiss();
        }
        else if (_session.Properties.TryGetProperty(typeof(MySqlSignature), out MySqlSignature signature))
        {
            signature.SetCurrent(context.ArgumentIndex);
        }
    }

    private SignatureContext? Context() =>
        SqlCompletionAnalyzer.SignatureAt(_view.TextSnapshot.GetText(), _view.Caret.Position.BufferPosition.Position);

    private void Trigger()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (QueryEditorServices.Schema is not { } cache || Context() is not { } context)
        {
            return;
        }

        // A comma in the same call only moves the mark.
        if (_session is not null && context.OpenParen == _sessionParen)
        {
            FollowCaret();
            return;
        }

        var (definition, database) = QueryEditorServices.ConnectionOf(_view.TextDataModel.DocumentBuffer);
        if (definition is null)
        {
            return;
        }

        var request = ++_request;
#pragma warning disable VSSDK007 // Fire and forget: a routine that cannot be read just shows no tip.
        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
        {
            await TaskScheduler.Default;
            var signature = await IntelliSenseInfoBuilder.SignatureAsync(context, cache, definition, database, CancellationToken.None);
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (signature is null || request != _request || _view.IsClosed || Context() is not { } now || now.OpenParen != context.OpenParen)
            {
                return;
            }

            Show(signature with { CurrentParameter = now.ArgumentIndex }, context.OpenParen);
        });
#pragma warning restore VSSDK007
    }

    private void Show(RoutineSignature signature, int openParen)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        Dismiss();
        var snapshot = _view.TextSnapshot;
        var caret = _view.Caret.Position.BufferPosition.Position;
        var applicable = snapshot.CreateTrackingSpan(openParen + 1, Math.Max(0, caret - openParen - 1), SpanTrackingMode.EdgeInclusive);
        var session = _broker.CreateSignatureHelpSession(_view, snapshot.CreateTrackingPoint(caret, PointTrackingMode.Positive), true);
        session.Properties.AddProperty(typeof(MySqlSignature), new MySqlSignature(signature, applicable));
        session.Dismissed += OnSessionDismissed;
        _session = session;
        _sessionParen = openParen;
        session.Start();
    }

    private void OnSessionDismissed(object? sender, EventArgs e)
    {
        if (sender is ISignatureHelpSession session)
        {
            session.Dismissed -= OnSessionDismissed;
        }

        if (ReferenceEquals(sender, _session))
        {
            _session = null;
            _sessionParen = -1;
        }
    }

    private void Dismiss()
    {
        if (_session is { IsDismissed: false } session)
        {
            session.Dismiss();
        }

        _session = null;
        _sessionParen = -1;
    }
}
