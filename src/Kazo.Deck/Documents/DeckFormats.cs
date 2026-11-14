using Kazo.Os.App.Abstractions;
using Kazo.Os.AppSdk.Craft;

namespace Kazo.Deck.Documents;

/// <summary>
/// The files Kazo Deck knows (doc/en/plan.md, decision 13, accepted by Benoit on 2026-11-14): the
/// presentations, which nothing else in Kazo OS opens and Kazo Deck becomes the app of, and the
/// outlines DeckCraft turns into a presentation, which Kazo Deck only offers in "Open with".
/// </summary>
/// <remarks>
/// <para>
/// The manifest makes one file handler per media type, and a media type takes the "default app" flag
/// of its first format (<see cref="CraftDocumentFormats.FileHandlers"/>), so the "Open with" formats
/// never share a media type with a default one.
/// </para>
/// <para>
/// No format is marked as a copy: in host mode DeckCraft sends its exports through the same
/// <c>save</c> message as a save, and the window tells them apart by the name (decision 6, work
/// item 3), not by the extension: a <c>.pptx</c> can be the document or an exported copy.
/// </para>
/// </remarks>
public static class DeckFormats
{
    /// <summary>DeckCraft's own presentation format, the one of a first save (decision 7).</summary>
    public const string NativeExtension = ".deckcraft";

    /// <summary>Gets every format, with what Kazo Deck does with it.</summary>
    public static CraftDocumentFormats Formats { get; } = new(
    [
        // Default app (open and edit): the presentations DeckCraft reads.
        Default(".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation"),
        Default(".ppsx", "application/vnd.openxmlformats-officedocument.presentationml.slideshow"),
        Default(".potx", "application/vnd.openxmlformats-officedocument.presentationml.template"),
        Default(".deckcraft", "application/x-deckcraft"),

        // "Open with" only: DeckCraft makes a presentation outline of them, and the text editor
        // stays their app.
        OpenWith(".txt", "text/plain"),
        OpenWith(".md", "text/markdown"),
        OpenWith(".rtf", "application/rtf"),
    ]);

    /// <summary>Gets the extensions the OS Open dialog proposes, in the order above.</summary>
    public static IReadOnlyList<string> OpenExtensions => Formats.OpenExtensions;

    /// <summary>
    /// Gets the extensions of the presentations DeckCraft keeps under their own name: a file sent with
    /// one of them becomes the presentation's file, so its name is bound to the file (decision 8).
    /// An outline (<c>.txt .md .rtf</c>) opens as a new untitled presentation instead.
    /// </summary>
    public static IReadOnlyList<string> PresentationExtensions { get; } = [".pptx", ".ppsx", ".potx", NativeExtension];

    /// <summary>Gets the extensions of DeckCraft's File › Open picker, which the window answers itself.</summary>
    public static IReadOnlyList<string> PresentationPickerExtensions { get; } = [".deckcraft", ".pptx", ".potx", ".ppsx"];

    /// <summary>Whether a file name is a presentation DeckCraft keeps under that name.</summary>
    /// <param name="name">A file name or path.</param>
    /// <returns><see langword="true"/> for <c>.pptx .ppsx .potx .deckcraft</c>, in any case.</returns>
    public static bool IsPresentation(string? name) =>
        !string.IsNullOrEmpty(name)
        && PresentationExtensions.Contains(CraftDocumentFormats.ExtensionOf(name).ToLowerInvariant(), StringComparer.Ordinal);

    /// <summary>
    /// Whether a picker DeckCraft opened is its File › Open (it accepts presentations), rather than
    /// Insert › Picture, Audio or Video. Reuse Slides needs the desktop app and opens no picker on the web.
    /// </summary>
    /// <param name="request">The picker request the module relayed.</param>
    /// <returns><see langword="true"/> for File › Open.</returns>
    public static bool IsOpenPicker(PickRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.AcceptsAny(PresentationPickerExtensions);
    }

    /// <summary>Gets the file associations of the manifest.</summary>
    public static IReadOnlyList<FileHandler> FileHandlers => Formats.FileHandlers;

    private static CraftDocumentFormat Default(string extension, string mediaType) =>
        new(extension, mediaType, IsDefault: true, IsCopy: false);

    private static CraftDocumentFormat OpenWith(string extension, string mediaType) =>
        new(extension, mediaType, IsDefault: false, IsCopy: false);
}
