using Kazo.Os.AppSdk.Blazor;
using Kazo.Os.AppSdk.Craft;
using Kazo.Os.AppSdk.Globalization;
using Kazo.Deck.Hosting;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Localization;
using Microsoft.JSInterop;

namespace Kazo.Deck;

/// <summary>
/// The Kazo Deck window (doc/en/plan.md, work item 2): DeckCraft in an iframe started by the Kazo OS
/// module <c>Kazo.Os.AppSdk.Craft</c> (<see cref="CraftFrame"/>), with a loading screen until it is
/// ready and an error screen with "Retry" when it fails or does not answer within
/// <see cref="FrameStartup.Timeout"/>.
/// </summary>
/// <remarks>
/// "Ready" is DeckCraft's <c>deckcraft:ready</c> event (its host mode, <c>craft-web-host</c>). Once
/// DeckCraft is ready the window gives it the OS theme (decision 10; there is no language to give,
/// DeckCraft is in English only), then opens the file it was started for. Opening, saving, copies,
/// the binding of presentations to their files and the close guard are in
/// <c>DeckWindow.Documents.cs</c> (work item 3); the file pickers, the chords and the theme in
/// <c>DeckWindow.Proxy.cs</c>. The slide show in full screen and the presenter view come with work
/// item 4. The launch argument <c>--webgl</c> forces DeckCraft's WebGL 2 renderer, to try it
/// without WebGPU.
/// </remarks>
public sealed partial class DeckWindow : IAsyncDisposable
{
    private readonly FrameStartup _startup = new();
    private ElementReference _frameHost;
    private CraftFrame? _frame;
    private CancellationTokenSource? _timeout;
    private IStringLocalizer<Strings>? _localizer;
    private OpenRequest _request = new(null, false);

    [Inject]
    private IJSRuntime JavaScript { get; set; } = default!;

    private IStringLocalizer<Strings> L => _localizer ??= AppContext.GetLocalizer<Strings>();

    /// <summary>The error screen's detail: DeckCraft's own error, or the timeout.</summary>
    private string FailureText => _startup.TimedOut
        ? L["TimedOut", (int)FrameStartup.Timeout.TotalSeconds]
        : _startup.Error ?? string.Empty;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        CancelTimeout();
        await StopFrameAsync();
        _nextSave?.TrySetResult(false);
        _saveGate.Dispose();
    }

    /// <inheritdoc />
    /// <remarks>Reads what the window was started for, and names it before it is first shown.</remarks>
    protected override async Task OnAppInitializedAsync()
    {
        _request = OpenRequest.FromArguments(LaunchArguments);
        await SetWindowTitleAsync(DocumentTitle);
    }

    /// <inheritdoc />
    protected override async Task OnAppAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await StartFrameAsync();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// The window came to the front: the keyboard goes to DeckCraft, and the title follows the
    /// presentation in front, which DeckCraft may have changed (Window › Next Window).
    /// </remarks>
    protected override async Task OnWindowActivatedAsync()
    {
        if (_frame is { } frame && _startup.State == FrameState.Running)
        {
            await frame.FocusAsync();
            await RefreshDocumentsAsync(frame);
            await UpdateTitleAsync();
        }
    }

    private async Task StartFrameAsync()
    {
        CraftFrame frame = new(JavaScript);
        frame.Ready += OnReadyAsync;
        frame.Failed += OnFailedAsync;
        frame.DirtyChanged += OnDirtyChangedAsync;
        frame.Saving += OnSavingAsync;
        frame.Exporting += OnExportingAsync;
        ConnectProxy(frame, connect: true);
        _frame = frame;
        ArmTimeout(_startup.Attempt);

        try
        {
            await frame.StartAsync(_frameHost, FrameOptions(), AppContext.CancellationToken);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException)
        {
            await OnFailedAsync(exception.Message);
        }
    }

    // DeckCraft's page, the user's name for comments, the WebGL 2 switch, and the chords the
    // window keeps: the OS's and Ctrl+N, a new window (decision 12, Q1 = A).
    private CraftFrameOptions FrameOptions() =>
        new(CraftFrameOptions.PagePathOf(typeof(DeckWindow).Assembly.GetName().Name!), L["EditorLabel"])
        {
            Parameters = DeckCraftParameters.For(_request.WebGl, AppContext.UserDisplayName),
            ReservedKeys = DeckKeys.All,
        };

    private async Task StopFrameAsync()
    {
        if (_frame is null)
        {
            return;
        }

        _frame.Ready -= OnReadyAsync;
        _frame.Failed -= OnFailedAsync;
        _frame.DirtyChanged -= OnDirtyChangedAsync;
        _frame.Saving -= OnSavingAsync;
        _frame.Exporting -= OnExportingAsync;
        ConnectProxy(_frame, connect: false);
        await _frame.DisposeAsync();
        _frame = null;
    }

    private async Task RetryAsync()
    {
        CancelTimeout();
        await StopFrameAsync();
        _startup.Retry();
        StateHasChanged();
        await StartFrameAsync();
    }

    private async Task OnReadyAsync(string? appVersion)
    {
        if (!_startup.OnReady(appVersion))
        {
            return;
        }

        CancelTimeout();
        await InvokeAsync(StateHasChanged);
        if (_frame is not { } frame)
        {
            return;
        }

        await OnSchemeChangedAsync(await frame.GetColorSchemeAsync());

        // DeckCraft starts on an empty presentation ("Presentation1"); the file the window was
        // started for takes its place.
        await PrepareDocumentAsync();
        if (_frame is { } ready && IsWindowActive)
        {
            await ready.FocusAsync();
        }
    }

    private async Task OnFailedAsync(string? error)
    {
        if (_startup.State == FrameState.Running)
        {
            // Once running, DeckCraft also reports a file it refused (2 GiB or more) as `failed`:
            // the app is still there, so that is a message, not the error screen.
            await ShowNoticeAsync(error ?? string.Empty, error: true);
            return;
        }

        if (_startup.OnFailed(error))
        {
            CancelTimeout();
            await InvokeAsync(StateHasChanged);
        }
    }

    private Task<FrameCommandResult> ExecuteAsync(CraftFrame frame, string command, object parameters, CancellationToken cancellationToken = default) =>
        frame.RequestAsync(
            "engine.execute",
            new { command, @params = parameters },
            cancellationToken: cancellationToken == default ? AppContext.CancellationToken : cancellationToken);

    // Shows the error screen if attempt `attempt` gets no answer in time.
    private void ArmTimeout(int attempt)
    {
        CancelTimeout();
        CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(AppContext.CancellationToken);
        _timeout = timeout;
        _ = WaitForTimeoutAsync(attempt, timeout.Token);
    }

    private async Task WaitForTimeoutAsync(int attempt, CancellationToken cancellationToken)
    {
        try
        {
            await Task.Delay(FrameStartup.Timeout, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            return;
        }

        await InvokeAsync(() =>
        {
            if (_startup.OnTimeout(attempt))
            {
                StateHasChanged();
            }
        });
    }

    private void CancelTimeout()
    {
        _timeout?.Cancel();
        _timeout?.Dispose();
        _timeout = null;
    }
}
