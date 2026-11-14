using Kazo.Deck.Documents;
using Kazo.Os.AppSdk.Craft;

namespace Kazo.Deck.Tests;

/// <summary>
/// The presentations of a window and their files (doc/en/plan.md, work item 3, decisions 6 to 9,
/// Q1 = A).
/// </summary>
public sealed class DeckDocumentsTests
{
    private const string Home = "/home/neo";

    private static OpenDocument Doc(long id, string? name, string title, bool dirty = false, bool active = true) =>
        new(id, name, title, dirty, active);

    private static DeckDocuments Opened(string path, bool dirty = false)
    {
        DeckDocuments documents = new();
        documents.Opened(path);
        documents.Update([Doc(0, CraftDocumentFormats.FileNameOf(path), "Plan", dirty)]);
        return documents;
    }

    [Fact]
    public void A_new_window_holds_an_untouched_blank_presentation()
    {
        DeckDocuments documents = new();
        documents.Update([Doc(0, null, "Presentation1")]);

        Assert.True(documents.IsUntouchedBlank);
        Assert.Equal("Presentation1 — Kazo Deck", documents.Title("Untitled"));
        Assert.Equal("Kazo Deck", documents.Title("Untitled", running: false));
    }

    [Fact]
    public void A_changed_bound_or_second_presentation_is_no_longer_blank()
    {
        DeckDocuments changed = new();
        changed.Update([Doc(0, null, "Presentation1")]);
        changed.SetFrameDirty(true);
        changed.SetFrameDirty(false);
        Assert.False(changed.IsUntouchedBlank);

        Assert.False(Opened($"{Home}/Plan.pptx").IsUntouchedBlank);

        DeckDocuments two = new();
        two.Update([Doc(0, null, "Presentation1"), Doc(1, null, "Presentation2", active: false)]);
        Assert.False(two.IsUntouchedBlank);
    }

    [Fact]
    public void A_save_of_the_opened_presentation_rewrites_its_file()
    {
        DeckDocuments documents = Opened($"{Home}/Talks/Plan.pptx", dirty: true);

        SaveDecision decision = documents.Decide("Plan.pptx", saveAs: false);

        Assert.Equal(SaveAction.Rewrite, decision.Action);
        Assert.Equal($"{Home}/Talks/Plan.pptx", decision.Path);
        Assert.Equal("*Plan.pptx — Kazo Deck", documents.Title("Untitled"));
    }

    [Fact]
    public void The_first_save_of_a_new_presentation_asks_and_binds()
    {
        // DeckCraft names it <title>.deckcraft with saveAs: true (decision 7).
        DeckDocuments documents = new();
        documents.Update([Doc(0, null, "Presentation1", dirty: true)]);

        SaveDecision decision = documents.Decide("Presentation1.deckcraft", saveAs: true);
        Assert.Equal(SaveAction.AskAndBind, decision.Action);

        documents.Saved(decision, $"{Home}/Talk.deckcraft");
        documents.Update([Doc(0, "Presentation1.deckcraft", "Presentation1")]);
        documents.SetFrameDirty(false);

        Assert.Equal("Talk.deckcraft — Kazo Deck", documents.Title("Untitled"));
        Assert.Equal(SaveAction.Rewrite, documents.Decide("Presentation1.deckcraft", saveAs: false).Action);
        Assert.Equal($"{Home}/Talk.deckcraft", documents.Decide("Presentation1.deckcraft", saveAs: false).Path);
    }

    [Theory]
    [InlineData("Plan.pdf")]
    [InlineData("Plan1.png")]
    [InlineData("Plan.txt")]
    [InlineData("Plan.gif")]
    public void An_export_is_a_copy_in_the_presentation_s_folder(string name)
    {
        // Decision 6: saveAs false under a name that is not the presentation's own.
        DeckDocuments documents = Opened($"{Home}/Talks/Plan.pptx");

        SaveDecision decision = documents.Decide(name, saveAs: false);

        Assert.Equal(SaveAction.AskForCopy, decision.Action);
        Assert.Equal($"{Home}/Talks", decision.Folder);
        Assert.False(decision.SavesTheDocument);
    }

    [Fact]
    public void An_export_named_like_an_earlier_file_of_the_presentation_is_a_copy()
    {
        // Plan.pptx, then Save As Plan.deckcraft: an export to .pptx is named Plan.pptx again, and
        // must not rewrite the first file without asking.
        DeckDocuments documents = Opened($"{Home}/Plan.pptx");
        SaveDecision saveAs = documents.Decide("Plan.deckcraft", saveAs: true);
        documents.Saved(saveAs, $"{Home}/Plan.deckcraft");
        documents.Update([Doc(0, "Plan.deckcraft", "Plan")]);

        Assert.Equal(SaveAction.AskForCopy, documents.Decide("Plan.pptx", saveAs: false).Action);
        Assert.Equal(SaveAction.Rewrite, documents.Decide("Plan.deckcraft", saveAs: false).Action);
    }

    [Fact]
    public void A_save_of_a_presentation_not_in_front_is_a_copy()
    {
        DeckDocuments documents = Opened($"{Home}/Plan.pptx");
        documents.Update([Doc(0, "Plan.pptx", "Plan", active: false), Doc(1, null, "Presentation2")]);

        Assert.Equal(SaveAction.AskForCopy, documents.Decide("Plan.pptx", saveAs: false).Action);
    }

    [Fact]
    public void A_cancelled_save_keeps_the_window_modified_and_asks_again()
    {
        // DeckCraft marks the presentation saved as soon as it handed the bytes over (§6 of the guide).
        DeckDocuments documents = new();
        documents.Update([Doc(0, null, "Presentation1", dirty: true)]);
        SaveDecision first = documents.Decide("Presentation1.deckcraft", saveAs: true);
        documents.SaveFailed(first);
        documents.Update([Doc(0, "Presentation1.deckcraft", "Presentation1")]);
        documents.SetFrameDirty(false);

        Assert.True(documents.IsDirty);
        Assert.StartsWith("*", documents.Title("Untitled"), StringComparison.Ordinal);
        Assert.Single(documents.WithUnsavedWork());
        Assert.Equal(SaveAction.AskAndBind, documents.Decide("Presentation1.deckcraft", saveAs: false).Action);
    }

    [Theory]
    [InlineData("/home/neo/Notes.txt")]
    [InlineData("/home/neo/Notes.md")]
    [InlineData("/home/neo/Notes.rtf")]
    public void An_outline_binds_nothing_so_its_export_never_rewrites_it(string path)
    {
        DeckDocuments documents = new();
        Assert.False(documents.Opened(path));
        documents.Update([Doc(0, null, "Notes")]);

        Assert.Null(documents.PathOf(CraftDocumentFormats.FileNameOf(path)));
        Assert.Equal(SaveAction.AskForCopy, documents.Decide("Notes.txt", saveAs: false).Action);
        Assert.Equal("Notes — Kazo Deck", documents.Title("Untitled"));
    }

    [Theory]
    [InlineData("Plan.pptx", true)]
    [InlineData("Plan.PPSX", true)]
    [InlineData("Plan.potx", true)]
    [InlineData("Plan.deckcraft", true)]
    [InlineData("Plan.txt", false)]
    [InlineData("Plan.pdf", false)]
    [InlineData("", false)]
    public void Presentations_are_kept_under_their_name(string name, bool presentation)
    {
        Assert.Equal(presentation, DeckFormats.IsPresentation(name));
    }

    [Theory]
    [InlineData(".deckcraft,.pptx,.potx,.ppsx,.txt,.md", true)]
    [InlineData(".png,.jpg,.jpeg,.gif", false)]
    [InlineData(".mp4,.m4v,.mov,.webm", false)]
    [InlineData("", false)]
    public void File_Open_is_told_from_the_other_pickers(string accept, bool open)
    {
        Assert.Equal(open, DeckFormats.IsOpenPicker(new PickRequest(1, accept, Multiple: false)));
    }

    [Fact]
    public void The_close_guard_lists_every_modified_presentation_of_the_window()
    {
        DeckDocuments documents = Opened($"{Home}/Plan.pptx");
        documents.Update([Doc(0, "Plan.pptx", "Plan", dirty: true), Doc(1, null, "Presentation2", dirty: true, active: false), Doc(2, null, "Presentation3", active: false)]);

        IReadOnlyList<OpenDocument> unsaved = documents.WithUnsavedWork();

        Assert.Equal([0L, 1L], unsaved.Select(document => document.Id));
        Assert.Equal("Plan.pptx", documents.NameOf(unsaved[0], "Untitled"));
        Assert.Equal("Presentation2", documents.NameOf(unsaved[1], "Untitled"));
    }
}
