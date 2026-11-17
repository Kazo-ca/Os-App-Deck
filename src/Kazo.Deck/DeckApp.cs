using Kazo.Os.App.Abstractions;
using Kazo.Deck.Documents;

namespace Kazo.Deck;

/// <summary>
/// The app's manifest (doc/en/plan.md, decision 1). The build generates <c>manifest.json</c> from
/// this class (Kazo.Os.App.Build); it is never instantiated at run time.
/// </summary>
/// <remarks>
/// Work item 2 declares the identity, the window and its size, several windows at once (Q1 = A: one
/// presentation per window), a window that stays on its screen (decision 3), the capabilities of
/// Kazo Fig and the file associations of decision 13 (<see cref="DeckFormats"/>). Opening and saving
/// those files come with work item 3.
/// </remarks>
[AppManifest]
public sealed class DeckApp : AppDefinition
{
    /// <summary>
    /// Smallest usable width of DeckCraft, in pixels (decision 1): the ribbon folds its groups, the
    /// slide thumbnails and a slide still fit.
    /// </summary>
    public const int ContentMinWidth = 800;

    /// <summary>Smallest usable height of DeckCraft, in pixels (decision 1): ribbon, slide and status bar.</summary>
    public const int ContentMinHeight = 520;

    /// <inheritdoc />
    public override string Id => "ca.kazo.deck";

    /// <inheritdoc />
    public override string Name => "Kazo Deck";

    /// <inheritdoc />
    public override AppKind Kind => AppKind.Window;

    /// <inheritdoc />
    public override Type EntryPoint => typeof(DeckWindow);

    /// <inheritdoc />
    public override string PublisherId => "pub.kazo";

    /// <inheritdoc />
    public override string Description => "A presentation editor with slides, themes, transitions, animations and slide shows, based on DeckCraft by the ArtCraft team";

    /// <inheritdoc />
    /// <remarks>The category of Kazo Word (decision 1).</remarks>
    public override string Category => "productivity";

    /// <inheritdoc />
    /// <remarks>The tag that publishes to the Kazo OS Store must be v + this version.</remarks>
    public override string Version => "0.1.3-beta.1";

    /// <inheritdoc />
    /// <remarks>
    /// Several windows at once: each one runs its own DeckCraft in its own iframe, because the
    /// DeckCraft canvas id is fixed and allows one instance per HTML document (doc/en/plan.md), and
    /// each presentation gets its own window (Q1 = A).
    /// </remarks>
    public override bool SingleInstancePerUser => false;

    /// <inheritdoc />
    /// <remarks>
    /// Kazo Fig's list (doc/en/plan.md, work item 2): the OS Open and Save dialogs, the user's files,
    /// the app's private folder, and launching a new window of the app (Q1 = A: a presentation opened
    /// from a window that already holds one opens in a new window; decision 8).
    /// </remarks>
    public override IReadOnlyList<string> Capabilities =>
    [
        AppCapabilities.DialogFileOpen,
        AppCapabilities.DialogFileSave,
        AppCapabilities.FileSystemUserHomeRead,
        AppCapabilities.FileSystemUserHomeWrite,
        AppCapabilities.FileSystemPrivateRead,
        AppCapabilities.FileSystemPrivateWrite,
        AppCapabilities.AppsLaunch,
    ];

    /// <inheritdoc />
    /// <remarks>
    /// The presentations with <c>open</c> and <c>edit</c> (Kazo Deck becomes their app); the text
    /// outlines with <c>open</c> only, for "Open with" (decision 13).
    /// </remarks>
    public override IReadOnlyList<FileHandler> FileHandlers => DeckFormats.FileHandlers;

    /// <inheritdoc />
    public override IReadOnlyList<string> Icons => ["icons/app.svg"];

    /// <inheritdoc />
    public override IReadOnlyList<AssetManifest> Assets =>
        [new("icons/app.svg", AssetKind.Image, new string('0', 64), "app-icon")];

    /// <inheritdoc />
    /// <remarks>
    /// Sizes are the outer size of the OS window (title bar included): 1280 × 820 by default, where
    /// DeckCraft's ribbon is readable (doc/en/plan.md, "Reading DeckCraft"); at least DeckCraft's
    /// minimum content (800 × 520, decision 1) plus the frame. <see cref="WindowOptions.CanChangeScreen"/> is
    /// <see langword="false"/>: moving the window to another screen moves its element into another
    /// document, which reloads DeckCraft's iframe and loses the open presentations (Kazo OS ADR 0263,
    /// issue #2).
    /// </remarks>
    public override WindowOptions? Window => new()
    {
        DefaultWidth = 1280,
        DefaultHeight = 820,
        MinWidth = ContentMinWidth + WindowChromeWidth,
        MinHeight = ContentMinHeight + WindowChromeHeight,
        CanChangeScreen = false,
    };

    // Borders and title bar of an OS window (the tallest title bar of the Kazo OS themes, 2.35rem).
    private const int WindowChromeWidth = 2;
    private const int WindowChromeHeight = 40;
}
