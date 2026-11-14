using Kazo.Deck.Documents;
using Kazo.Deck.Hosting;
using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk;
using Kazo.Os.AppSdk.Blazor;
using Kazo.Os.AppSdk.Craft;
using Kazo.Os.AppSdk.IO;

namespace Kazo.Deck;

/// <summary>
/// What the window does with the module's proxy (doc/en/plan.md, work item 3): DeckCraft's file
/// pickers, the chords the window keeps, and the OS theme.
/// </summary>
/// <remarks>
/// The proxy's messages arrive through a <c>[JSInvokable]</c> callback, outside the scope the OS opens
/// for app code: each handler that reaches the SDK (files, dialogs, launching) enters the app's
/// context itself. DeckCraft keeps no preference in the browser (decision 11): there is no
/// settings file to keep.
/// </remarks>
public sealed partial class DeckWindow
{
    // The proxy's events of one frame (subscribed in StartFrameAsync, removed in StopFrameAsync).
    private void ConnectProxy(CraftFrame frame, bool connect)
    {
        if (connect)
        {
            frame.KeyPressed += OnKeyPressedAsync;
            frame.PickRequested += OnPickRequestedAsync;
            frame.SchemeChanged += OnSchemeChangedAsync;
        }
        else
        {
            frame.KeyPressed -= OnKeyPressedAsync;
            frame.PickRequested -= OnPickRequestedAsync;
            frame.SchemeChanged -= OnSchemeChangedAsync;
        }
    }

    // DeckCraft follows the OS theme (decision 10): dark for the dark scheme, light for the light
    // one. DeckCraft keeps no preference, so the theme is given again at every start.
    private async Task OnSchemeChangedAsync(string scheme)
    {
        if (_frame is { } frame)
        {
            await frame.RequestAsync("ui.set", new { dark = DeckCraftTheme.IsDark(scheme) }, cancellationToken: AppContext.CancellationToken);
        }
    }

    // Ctrl+N: a new, empty Kazo Deck window (Q1 = A), as File › New does in PowerPoint.
    private Task OnKeyPressedAsync(ReservedKey key) => DeckKeys.ActionOf(key) switch
    {
        KeyAction.NewWindow => InvokeAsync(() => LaunchWindowAsync([], name: null)),
        _ => Task.CompletedTask,
    };

    // DeckCraft opened its file picker: the OS dialog instead. File › Open is not handed to
    // DeckCraft: it hears "cancelled", and the window opens the chosen file itself
    // (OpenPathAsync), here in place of an untouched blank presentation, else in a new window
    // (Q1 = A), always bound to its file. Insert › Picture, Audio and Video get the file read and
    // handed over: DeckCraft reads its bytes itself.
    private Task OnPickRequestedAsync(PickRequest request) => InvokeAsync(async () =>
    {
        using IDisposable scope = KazoExecutionContext.Enter(AppContext);
        CraftFrame? frame = _frame;
        if (frame is null)
        {
            return;
        }

        bool open = DeckFormats.IsOpenPicker(request);
        IReadOnlyList<PickedFile>? files = null;
        string? toOpen = null;
        try
        {
            OpenFileDialogResult choice = await OpenFileAsync(
                new OpenFileDialogRequest
                {
                    Title = L[open ? "OpenTitle" : "PickTitle"],
                    InitialDirectory = VirtualPath.Parse(_documents.ActiveFolder() ?? AppContext.HomeDirectory),
                    AllowMultiple = request.Multiple && !open,
                    Filters = FiltersOf(request, open),
                },
                AppContext.CancellationToken);
            if (choice.Status == FileDialogStatus.Selected && choice.Resources.Count > 0)
            {
                if (open)
                {
                    toOpen = choice.Resources[0].Path.Value;
                }
                else
                {
                    files = await ReadPickedAsync(choice.Resources);
                }
            }
        }
        finally
        {
            // DeckCraft waits on its picker until it hears back: it always does, even on a failure.
            await frame.AnswerPickAsync(request.Id, files);
        }

        // After the answer: DeckCraft's picker is closed before the file opens.
        if (toOpen is not null)
        {
            await OpenPathAsync(toOpen);
        }
    });

    private async Task<IReadOnlyList<PickedFile>?> ReadPickedAsync(IReadOnlyList<SelectedFileResource> resources)
    {
        List<PickedFile> files = [];
        foreach (SelectedFileResource resource in resources)
        {
            string path = resource.Path.Value;
            string name = KazoPath.GetFileName(path) ?? path;
            try
            {
                byte[] bytes = await KazoFile.ReadAllBytesAsync(path, AppContext.CancellationToken);
                files.Add(new PickedFile(name, string.Empty, bytes));
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException)
            {
                await ShowNoticeAsync(L["ReadFailed", name, exception.Message], error: true);
                return null;
            }
        }

        return files;
    }

    // For File › Open, the formats Kazo Deck opens (decision 13), in their order; for the other
    // pickers, the extensions DeckCraft accepts. Then every file (DeckCraft checks what it gets).
    private List<FileDialogFilter> FiltersOf(PickRequest request, bool open)
    {
        List<FileDialogFilter> filters = [];
        if (open)
        {
            filters.Add(new FileDialogFilter(L["PresentationsFilter"], DeckFormats.OpenExtensions, []));
        }
        else if (request.Extensions.Count > 0)
        {
            filters.Add(new FileDialogFilter(L["FilesFilter"], request.Extensions, []));
        }

        filters.Add(new FileDialogFilter(L["AllFiles"], [], []));
        return filters;
    }
}
