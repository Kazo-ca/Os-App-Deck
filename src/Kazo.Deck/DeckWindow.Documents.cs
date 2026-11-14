using System.Text.Json;
using Kazo.Deck.Documents;
using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk;
using Kazo.Os.AppSdk.Apps;
using Kazo.Os.AppSdk.Blazor;
using Kazo.Os.AppSdk.Craft;
using Kazo.Os.AppSdk.IO;
using Microsoft.JSInterop;

namespace Kazo.Deck;

/// <summary>
/// Opening, saving, copies and closing (doc/en/plan.md, work item 3, decisions 6 to 9): the window
/// reads and writes the files with <see cref="KazoFile"/>, asks the OS dialogs, keeps the title and
/// guards the close. <see cref="DeckDocuments"/> binds each presentation to its file and decides
/// each save.
/// </summary>
/// <remarks>
/// <para>
/// One presentation per window (Benoit's Q1 = A): a file opened from a window that holds anything
/// but its untouched blank presentation opens in a new Kazo Deck window, and so does every file the
/// OS opens with Kazo Deck, since each one starts its own window. DeckCraft's own File › New still
/// creates a second presentation in the same page (it has no tabs); the window lists it, binds it,
/// and saves it on close like the first.
/// </para>
/// <para>
/// DeckCraft hands its saves and its exports through the same <c>deckcraft:save</c> message
/// (decision 6): <see cref="DeckDocuments.Decide"/> tells them apart. The files of a multi-file
/// export (a picture per slide) follow the first one into its folder (<see cref="CopySeries"/>).
/// </para>
/// </remarks>
public sealed partial class DeckWindow : IWindowCloseGuard
{
    /// <summary>The app's id, to start another Kazo Deck window.</summary>
    private const string AppId = "ca.kazo.deck";

    /// <summary>
    /// How long the close guard waits for DeckCraft's save of one presentation: the save dialog may
    /// stay open a while, and a save that never comes must not hold the window open for good.
    /// </summary>
    private static readonly TimeSpan CloseSaveTimeout = TimeSpan.FromMinutes(2);

    /// <summary>How long the window waits for DeckCraft to show a file it sent (a large .pptx takes a while).</summary>
    private static readonly TimeSpan OpenTimeout = TimeSpan.FromSeconds(90);

    /// <summary>How often the window looks for the presentation of a file it sent.</summary>
    private static readonly TimeSpan OpenPollInterval = TimeSpan.FromMilliseconds(150);

    private readonly DeckDocuments _documents = new();
    private readonly CopySeries _copies = new();

    // Saves run one at a time: each may show a dialog, and the order is DeckCraft's.
    private readonly SemaphoreSlim _saveGate = new(1, 1);

    // Completed with the outcome of the next save (true = written), for the close guard.
    private TaskCompletionSource<bool>? _nextSave;

    // A message over DeckCraft: a failed write, a cancelled save, a copy written.
    private string? _notice;
    private bool _noticeIsError;

    /// <summary>Gets the title the OS shows for this window: <c>*name — Kazo Deck</c>.</summary>
    internal string DocumentTitle => _documents.Title(L["Untitled"], _startup.State == FrameState.Running);

    /// <summary>
    /// Asks before closing a window with work that is not in a file (decision 9): DeckCraft lists
    /// its presentations (<c>ui.inspect</c>); "Yes" brings each modified one to the front
    /// (<c>window.next { index }</c>) and saves it, and the window closes only once every write of
    /// the window succeeded. DeckCraft counts a presentation saved as soon as it hands the bytes
    /// over, so the window waits for its own writes, not for DeckCraft.
    /// </summary>
    /// <param name="cancellationToken">Cancels the close request.</param>
    /// <returns><see langword="true"/> to close.</returns>
    public async ValueTask<bool> ConfirmCloseAsync(CancellationToken cancellationToken = default)
    {
        if (_frame is not { } frame || _startup.State != FrameState.Running)
        {
            return true;
        }

        // A save still being written must end first: closing now would lose the write.
        await _saveGate.WaitAsync(cancellationToken);
        _saveGate.Release();

        if (!await RefreshDocumentsAsync(frame, cancellationToken))
        {
            // DeckCraft does not answer, so it cannot save either: closing without saving is the
            // only choice left, and it is the user's.
            if (!_documents.IsDirty)
            {
                return true;
            }

            string name = _documents.Active is { } active ? _documents.NameOf(active, L["Untitled"]) : L["Untitled"];
            MessageBoxDialogResult discard = await MessageBox(
                L["CloseTitle"], L["CloseUnanswered", name], MessageBoxType.YesNo, cancellationToken);
            return discard.Result == MessageBoxResult.Yes;
        }

        IReadOnlyList<OpenDocument> unsaved = _documents.WithUnsavedWork();
        if (unsaved.Count == 0)
        {
            return true;
        }

        string names = string.Join(", ", unsaved.Select(document => _documents.NameOf(document, L["Untitled"])));
        MessageBoxDialogResult answer = await MessageBox(
            L["CloseTitle"], L["CloseUnsaved", names], MessageBoxType.YesNoCancel, cancellationToken);
        return answer.Result switch
        {
            MessageBoxResult.Yes => await SaveAllAsync(frame, unsaved, cancellationToken),
            MessageBoxResult.No => true,
            _ => false,
        };
    }

    // After `ready`: the file the window was started for, in place of DeckCraft's blank
    // presentation. A window with no file keeps the blank presentation.
    private async Task PrepareDocumentAsync()
    {
        // Runs after `ready`, a [JSInvokable] callback: the app's scope must be entered for KazoFile.
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        if (_frame is { } frame)
        {
            await RefreshDocumentsAsync(frame);
            if (_request.Path is { } path)
            {
                await SendFileAsync(frame, path);
            }
        }

        await UpdateTitleAsync();
    }

    /// <summary>
    /// Opens a file where it belongs (Q1 = A): here when the window holds only its untouched blank
    /// presentation, nothing when it is the file this window already shows, a new Kazo Deck window
    /// otherwise.
    /// </summary>
    /// <param name="path">The file's absolute path.</param>
    /// <returns>A task that completes once the file is shown, or a message says why not.</returns>
    private async Task OpenPathAsync(string path)
    {
        if (_frame is not { } frame || _startup.State != FrameState.Running)
        {
            return;
        }

        await RefreshDocumentsAsync(frame);
        if (_documents.Active is { Name: { } name } && string.Equals(_documents.PathOf(name), path, StringComparison.Ordinal))
        {
            return;
        }

        if (_documents.IsUntouchedBlank)
        {
            await SendFileAsync(frame, path);
            return;
        }

        await LaunchWindowAsync([path], CraftDocumentFormats.FileNameOf(path));
    }

    // Reads a file and sends it to DeckCraft, which opens it in place of its untouched blank
    // presentation; a presentation is then bound to its file. A file that does not read leaves a
    // message; one DeckCraft cannot read comes back as `failed` or in its status bar.
    private async Task SendFileAsync(CraftFrame frame, string path)
    {
        string name = CraftDocumentFormats.FileNameOf(path);
        byte[] bytes;
        try
        {
            bytes = await KazoFile.ReadAllBytesAsync(path, AppContext.CancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            await ShowNoticeAsync(L["OpenFailed", name, exception.Message], error: true);
            return;
        }

        IReadOnlyList<OpenDocument> before = _documents.Documents;
        if (!await frame.OpenAsync(name, bytes))
        {
            await ShowNoticeAsync(L["OpenFailed", name, L["NotConnected"]], error: true);
            return;
        }

        // Bound at once: the next save of this name, even one made before the list shows it, is this file's.
        bool bound = _documents.Opened(path);
        await ClearNoticeAsync();
        await WaitForDocumentAsync(frame, name, bound, before);
        await UpdateTitleAsync();
    }

    // DeckCraft opens the file on its next frame, or later for a large one: wait for it, so the
    // title names it. A presentation shows under its own name; an outline as a new presentation,
    // in place of the blank one.
    private async Task WaitForDocumentAsync(CraftFrame frame, string name, bool bound, IReadOnlyList<OpenDocument> before)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow + OpenTimeout;
        while (DateTimeOffset.UtcNow < deadline && !AppContext.CancellationToken.IsCancellationRequested)
        {
            if (await RefreshDocumentsAsync(frame) && Shown(_documents.Documents))
            {
                return;
            }

            try
            {
                await Task.Delay(OpenPollInterval, AppContext.CancellationToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }

        bool Shown(IReadOnlyList<OpenDocument> now) => bound
            ? now.Any(document => string.Equals(document.Name, name, StringComparison.Ordinal))
            : now.Count != before.Count || now.Zip(before).Any(pair => pair.First.Title != pair.Second.Title);
    }

    // Another Kazo Deck window, on a file or empty (Ctrl+N).
    private async Task LaunchWindowAsync(IReadOnlyList<string> arguments, string? name)
    {
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        try
        {
            await KazoApps.LaunchAsync(AppId, arguments, AppContext.CancellationToken);
        }
        catch (Exception exception) when (exception is KazoAppLaunchException or KazoUnauthorizedAccessException or UnauthorizedAccessException)
        {
            await ShowNoticeAsync(
                name is null ? L["NewWindowFailed", exception.Message] : L["OpenFailed", name, exception.Message],
                error: true);
        }
    }

    // Asks DeckCraft for its presentations (ui.inspect) and the one in front (document.inspect);
    // false when it does not answer.
    private async Task<bool> RefreshDocumentsAsync(CraftFrame frame, CancellationToken cancellationToken = default)
    {
        CancellationToken token = cancellationToken == default ? AppContext.CancellationToken : cancellationToken;
        FrameCommandResult list = await frame.RequestAsync("ui.inspect", cancellationToken: token);
        if (!list.Ok)
        {
            return false;
        }

        // No presentation open (File › Close of the last one): document.inspect fails, none is in front.
        FrameCommandResult active = await frame.RequestAsync("document.inspect", cancellationToken: token);
        _documents.Update(DeckDocumentList.ListFrom(list.Value, active.Ok ? active.Value : default(JsonElement)));
        return true;
    }

    private async Task OnDirtyChangedAsync(bool dirty)
    {
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        _documents.SetFrameDirty(dirty);
        if (_frame is { } frame)
        {
            // The presentation in front may have changed too: the title names it.
            await RefreshDocumentsAsync(frame);
        }

        await UpdateTitleAsync();
    }

    // One file DeckCraft hands over: a save of a presentation, or a copy (decision 6). The answer
    // tells the module whether the file was written; DeckCraft does not listen to it.
    private async Task<bool> OnSavingAsync(FrameSave save)
    {
        // Called from JavaScript ([JSInvokable]): the app's scope must be entered for KazoFile.
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        await _saveGate.WaitAsync(AppContext.CancellationToken);
        bool written = false;
        try
        {
            written = await SaveAsync(save);
        }
        finally
        {
            _saveGate.Release();
            Interlocked.Exchange(ref _nextSave, null)?.TrySetResult(written);
            await UpdateTitleAsync();
        }

        return written;
    }

    // A download the page started anyway (ADR 0264 of Kazo OS): DeckCraft's host mode hands every
    // file over with `save`, so this is a safety net. Always a copy.
    private async Task<bool> OnExportingAsync(FrameSave export)
    {
        // Called from JavaScript ([JSInvokable]): the app's scope must be entered for KazoFile.
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        if (string.IsNullOrEmpty(export.Name))
        {
            return false;
        }

        await _saveGate.WaitAsync(AppContext.CancellationToken);
        try
        {
            return await SaveCopyAsync(_documents.DecideCopy(export.Name), export.Bytes);
        }
        finally
        {
            _saveGate.Release();
        }
    }

    private async Task<bool> SaveAsync(FrameSave save)
    {
        if (string.IsNullOrEmpty(save.Name))
        {
            return false;
        }

        if (_frame is { } frame)
        {
            // The save is the presentation's in front: its name and file decide.
            await RefreshDocumentsAsync(frame);
        }

        SaveDecision decision = _documents.Decide(save.Name, save.SaveAs);
        if (decision.Action == SaveAction.AskForCopy)
        {
            return await SaveCopyAsync(decision, save.Bytes);
        }

        _copies.Reset();
        try
        {
            return await CarryOutAsync(decision, save.Bytes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            // Whatever went wrong (a dialog, the file system), the presentation must not pass for saved.
            _documents.SaveFailed(decision);
            await ShowNoticeAsync(L["SaveFailed", save.Name, exception.Message], error: true);
            return false;
        }
    }

    // A copy: the next file of a multi-file export goes where the first one went, unless a file of
    // that name is already there; anything else asks.
    private async Task<bool> SaveCopyAsync(SaveDecision decision, byte[] bytes)
    {
        try
        {
            if (_copies.FolderFor(decision.Name, DateTimeOffset.UtcNow) is { } folder)
            {
                string path = $"{folder.TrimEnd('/')}/{decision.Name}";
                if (!await KazoFile.ExistsAsync(path, AppContext.CancellationToken))
                {
                    return await WriteCopyAsync(decision, path, bytes);
                }

                decision = decision with { Folder = folder };
            }

            _copies.Reset();
            return await CarryOutAsync(decision, bytes);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            _copies.Reset();
            await ShowNoticeAsync(L["SaveFailed", decision.Name, exception.Message], error: true);
            return false;
        }
    }

    // Asks for the path when the decision says so, writes the bytes, and records the outcome.
    private async Task<bool> CarryOutAsync(SaveDecision decision, byte[] bytes)
    {
        string? path = decision.AsksForPath ? await AskSavePathAsync(decision) : decision.Path;
        if (path is null)
        {
            _documents.SaveFailed(decision);
            if (decision.SavesTheDocument)
            {
                await ShowNoticeAsync(L["SaveCancelled", decision.Name], error: false);
            }

            return false;
        }

        if (decision.Action == SaveAction.AskForCopy)
        {
            return await WriteCopyAsync(decision, path, bytes);
        }

        try
        {
            await KazoFile.WriteAllBytesAsync(path, bytes, AppContext.CancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _documents.SaveFailed(decision);
            await ShowNoticeAsync(L["SaveFailed", CraftDocumentFormats.FileNameOf(path), exception.Message], error: true);
            return false;
        }

        // DeckCraft has no command to rename a presentation: it keeps the name it gave, and its next
        // save under that name goes to the file just chosen (the binding, decision 7).
        _documents.Saved(decision, path);
        await ClearNoticeAsync();
        return true;
    }

    private async Task<bool> WriteCopyAsync(SaveDecision decision, string path, byte[] bytes)
    {
        try
        {
            await KazoFile.WriteAllBytesAsync(path, bytes, AppContext.CancellationToken);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
        {
            _copies.Reset();
            await ShowNoticeAsync(L["SaveFailed", CraftDocumentFormats.FileNameOf(path), exception.Message], error: true);
            return false;
        }

        _documents.Saved(decision, path);
        _copies.Written(decision.Name, path, DateTimeOffset.UtcNow);
        await ShowNoticeAsync(L["CopySaved", CraftDocumentFormats.FileNameOf(path)], error: false);
        return true;
    }

    // The OS save dialog, in the presentation's folder, for the format DeckCraft chose.
    private async Task<string?> AskSavePathAsync(SaveDecision decision)
    {
        string extension = CraftDocumentFormats.ExtensionOf(decision.Name);
        CraftDocumentFormat? format = DeckFormats.Formats.Find(decision.Name);
        SaveFileDialogResult result = await SaveFileAsync(new SaveFileDialogRequest
        {
            Title = decision.Action == SaveAction.AskForCopy ? L["SaveCopyTitle"] : L["SaveTitle"],
            InitialDirectory = VirtualPath.Parse(decision.Folder ?? AppContext.HomeDirectory),
            SuggestedFileName = decision.Name,
            DefaultExtension = extension.Length > 0 ? extension : null,
            Filters = extension.Length > 0
                ? [new FileDialogFilter(L["FilterFormat", extension], [extension], format is null ? [] : [format.MediaType])]
                : [],
            ConfirmOverwrite = true,
        });
        return result.Status == FileDialogStatus.Selected && result.Resource is { } chosen
            ? CraftDocumentFormats.WithExtensionOf(chosen.Path.Value, decision.Name)
            : null;
    }

    // "Yes" in the close dialog: each presentation is brought to the front (window.next) and saved
    // by DeckCraft's File › Save; the window closes only when all of them reached a file.
    private async Task<bool> SaveAllAsync(CraftFrame frame, IReadOnlyList<OpenDocument> documents, CancellationToken cancellationToken)
    {
        foreach (OpenDocument document in documents)
        {
            TaskCompletionSource<bool> outcome = new(TaskCreationOptions.RunContinuationsAsynchronously);
            _nextSave = outcome;
            FrameCommandResult result = await ExecuteAsync(frame, "window.next", new { index = document.Id }, cancellationToken);
            if (result.Ok)
            {
                result = await ExecuteAsync(frame, "file.save", new { }, cancellationToken);
            }

            if (!result.Ok)
            {
                _nextSave = null;
                await ShowNoticeAsync(L["SaveFailed", _documents.NameOf(document, L["Untitled"]), result.Error ?? string.Empty], error: true);
                return false;
            }

            bool written;
            try
            {
                written = await outcome.Task.WaitAsync(CloseSaveTimeout, cancellationToken);
            }
            catch (TimeoutException)
            {
                _nextSave = null;
                written = false;
            }

            if (!written)
            {
                return false;
            }
        }

        return true;
    }

    private async Task UpdateTitleAsync()
    {
        try
        {
            await SetWindowTitleAsync(DocumentTitle);
        }
        catch (Exception exception) when (exception is InvalidOperationException or JSDisconnectedException)
        {
            // The window is closing.
        }

        await InvokeAsync(StateHasChanged);
    }

    private async Task ShowNoticeAsync(string text, bool error)
    {
        _notice = text;
        _noticeIsError = error;
        await InvokeAsync(StateHasChanged);
    }

    private async Task ClearNoticeAsync()
    {
        if (_notice is not null)
        {
            _notice = null;
            await InvokeAsync(StateHasChanged);
        }
    }

    private void DismissNotice() => _notice = null;
}
