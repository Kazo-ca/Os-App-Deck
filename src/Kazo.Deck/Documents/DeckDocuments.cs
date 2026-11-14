using Kazo.Os.AppSdk.Craft;

namespace Kazo.Deck.Documents;

/// <summary>
/// The presentations of one Kazo Deck window and their files (doc/en/plan.md, work item 3,
/// decisions 6 to 9, Benoit's Q1 = A): one presentation per window, bound to its file by the name
/// DeckCraft saves it under.
/// </summary>
/// <remarks>
/// <para>
/// The module's <see cref="DocumentSession"/> binds names to files and decides each save. This
/// class adds what DeckCraft needs around it: the presentations it lists (<c>ui.inspect</c>, with
/// the one in front from <c>document.inspect</c>), the copies it hands over through the same
/// <c>save</c> message as a save (decision 6), whether the window still holds only its untouched
/// blank presentation (Q1 = A), the save dialog's folder and the title.
/// </para>
/// <para>
/// DeckCraft counts a presentation saved as soon as it handed the bytes over, before the window
/// wrote anything, and does not listen to <c>saved</c>: a save the window could not finish is
/// remembered by the session (<see cref="DocumentSession.UnsavedNames"/>), so the window stays
/// modified and the next save asks for a path again (§6 of the module's guide).
/// </para>
/// </remarks>
public sealed class DeckDocuments
{
    /// <summary>The window's name for its app, at the end of its title.</summary>
    public const string AppName = "Kazo Deck";

    private readonly DocumentSession _session = new(DeckFormats.Formats, AppName);
    private IReadOnlyList<OpenDocument> _documents = [];

    /// <summary>Gets the presentations as DeckCraft last listed them.</summary>
    public IReadOnlyList<OpenDocument> Documents => _documents;

    /// <summary>Gets the presentation in front, or <see langword="null"/> when it is not known.</summary>
    public OpenDocument? Active => _documents.FirstOrDefault(document => document.Active);

    /// <summary>Gets whether the window has work that is not in a file.</summary>
    public bool IsDirty => _session.FrameDirty || _documents.Any(document => document.Dirty) || _session.UnsavedNames.Count > 0;

    /// <summary>
    /// Gets whether the window still holds only the blank presentation DeckCraft starts with, never
    /// changed and with no file: a file opened from it takes its place (DeckCraft closes the blank
    /// one) instead of opening another window (Q1 = A).
    /// </summary>
    public bool IsUntouchedBlank =>
        _session.IsUntouchedBlank
        && _documents.Count <= 1
        && _documents.All(document => document.Name is null && !document.Dirty);

    /// <summary>Gets the file a name is bound to.</summary>
    /// <param name="name">The name DeckCraft saves a presentation under.</param>
    /// <returns>The file's absolute path, or <see langword="null"/>.</returns>
    public string? PathOf(string name) => _session.PathOf(name);

    /// <summary>Takes DeckCraft's list of presentations (read by <see cref="DeckDocumentList"/>).</summary>
    /// <param name="documents">The presentations DeckCraft has open.</param>
    public void Update(IReadOnlyList<OpenDocument> documents)
    {
        ArgumentNullException.ThrowIfNull(documents);
        _documents = documents;
    }

    /// <summary>DeckCraft reported that unsaved changes appeared, or went away, in its presentations.</summary>
    /// <param name="dirty">The new state.</param>
    public void SetFrameDirty(bool dirty) => _session.SetFrameDirty(dirty);

    /// <summary>
    /// A file was handed to DeckCraft. A presentation keeps the file's name in DeckCraft, so that
    /// name is bound to the file; an outline (<c>.txt .md .rtf</c>) becomes a new untitled
    /// presentation and binds nothing, so an outline export can never rewrite the text file.
    /// </summary>
    /// <param name="path">The file's absolute path.</param>
    /// <returns><see langword="true"/> when the name was bound.</returns>
    public bool Opened(string path)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        string name = CraftDocumentFormats.FileNameOf(path);
        if (!DeckFormats.IsPresentation(name))
        {
            return false;
        }

        _session.Opened(name, path);
        return true;
    }

    /// <summary>
    /// Decides one file DeckCraft hands over (decision 6). <c>saveAs: true</c>, or the name of the
    /// presentation in front when that name is bound here (or its last save failed), is a save of
    /// the presentation: the module's rule (<see cref="DocumentSession.Decide"/>). Any other
    /// <c>saveAs: false</c> is a copy (an export, a print): the dialog asks, the binding stays.
    /// </summary>
    /// <param name="name">The name DeckCraft gave.</param>
    /// <param name="saveAs">DeckCraft's <c>saveAs</c>.</param>
    /// <returns>The decision.</returns>
    public SaveDecision Decide(string name, bool saveAs)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (!saveAs && !IsActiveDocumentSave(name))
        {
            return DecideCopy(name);
        }

        SaveDecision decision = _session.Decide(name, saveAs);
        return decision.Folder is null && ActiveFolder() is { } folder ? decision with { Folder = folder } : decision;
    }

    /// <summary>Decides a copy: always asks, in the folder of the presentation in front.</summary>
    /// <param name="name">The name DeckCraft gave.</param>
    /// <returns>An <see cref="SaveAction.AskForCopy"/> decision.</returns>
    public SaveDecision DecideCopy(string name)
    {
        SaveDecision decision = _session.DecideCopy(name);
        return decision.Folder is null && ActiveFolder() is { } folder ? decision with { Folder = folder } : decision;
    }

    /// <summary>The file was written.</summary>
    /// <param name="decision">The decision carried out.</param>
    /// <param name="path">The file written.</param>
    public void Saved(SaveDecision decision, string path) => _session.Saved(decision, path);

    /// <summary>The file was not written: the dialog was cancelled, or the write failed.</summary>
    /// <param name="decision">The decision that did not complete.</param>
    public void SaveFailed(SaveDecision decision) => _session.SaveFailed(decision);

    /// <summary>Picks the presentations with work that is not in a file, in DeckCraft's order.</summary>
    /// <returns>The presentations to save before the window closes.</returns>
    public IReadOnlyList<OpenDocument> WithUnsavedWork() => _session.WithUnsavedWork(_documents);

    /// <summary>
    /// Builds the window title: <c>*name — Kazo Deck</c>, the name of the file of the presentation
    /// in front (or its name in DeckCraft when it has no file), the asterisk while any presentation
    /// of the window has work that is not in a file.
    /// </summary>
    /// <param name="untitled">The name of a presentation with no name, in the user's language.</param>
    /// <param name="running">Whether DeckCraft is running.</param>
    /// <returns>The title.</returns>
    public string Title(string untitled, bool running = true)
    {
        if (!running)
        {
            return AppName;
        }

        string name = Active is { } active ? NameOf(active, untitled) : untitled;
        return $"{(IsDirty ? "*" : string.Empty)}{name} — {AppName}";
    }

    /// <summary>The name of a presentation for the user: its file's, else its name in DeckCraft, else its title.</summary>
    /// <param name="document">The presentation.</param>
    /// <param name="untitled">The name of a presentation with no name.</param>
    /// <returns>The name.</returns>
    public string NameOf(OpenDocument document, string untitled)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (document.Name is { } saved)
        {
            return _session.PathOf(saved) is { } path ? CraftDocumentFormats.FileNameOf(path) : saved;
        }

        return document.Title.Length > 0 ? document.Title : untitled;
    }

    /// <summary>The folder of the file of the presentation in front, for the dialogs.</summary>
    /// <returns>The folder, or <see langword="null"/>.</returns>
    public string? ActiveFolder() =>
        Active?.Name is { } name && _session.PathOf(name) is { } path ? CraftDocumentFormats.FolderOf(path) : null;

    // A save of the presentation in front: DeckCraft names it after its file (its `path`), and
    // that name is bound here or its last save failed. An export of the same format can carry a
    // name that is bound to an earlier file of the presentation (Plan.pptx after a Save As to
    // Plan.deckcraft): it is not the name in front, so it is a copy, never a rewrite.
    private bool IsActiveDocumentSave(string name) =>
        Active is { Name: { } active }
        && string.Equals(active, name, StringComparison.Ordinal)
        && (_session.PathOf(name) is not null || _session.UnsavedNames.Contains(name));
}
