using System.Text.Json;
using Kazo.Deck.Documents;
using Kazo.Os.AppSdk.Craft;

namespace Kazo.Deck.Tests;

/// <summary>Reading DeckCraft's presentations from ui.inspect and document.inspect (decision 8).</summary>
public sealed class DeckDocumentListTests
{
    private static JsonElement Json(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void Each_presentation_is_named_by_its_place_and_file()
    {
        IReadOnlyList<OpenDocument> list = DeckDocumentList.ListFrom(
            Json("""{ "documents": [ { "title": "Presentation1", "dirty": false, "path": null }, { "title": "Plan", "dirty": true, "path": "Plan.pptx" } ] }"""),
            Json("""{ "title": "Plan", "path": "Plan.pptx" }"""));

        Assert.Equal(2, list.Count);
        Assert.Equal(new OpenDocument(0, null, "Presentation1", false, false), list[0]);
        Assert.Equal(new OpenDocument(1, "Plan.pptx", "Plan", true, true), list[1]);
    }

    [Fact]
    public void An_untitled_presentation_in_front_is_found_by_its_title()
    {
        IReadOnlyList<OpenDocument> list = DeckDocumentList.ListFrom(
            Json("""{ "documents": [ { "title": "Presentation1", "path": null }, { "title": "Presentation2", "path": null } ] }"""),
            Json("""{ "title": "Presentation2", "path": null }"""));

        Assert.False(list[0].Active);
        Assert.True(list[1].Active);
    }

    [Fact]
    public void Two_untitled_presentations_with_one_title_leave_none_in_front()
    {
        IReadOnlyList<OpenDocument> list = DeckDocumentList.ListFrom(
            Json("""{ "documents": [ { "title": "Deck", "path": null }, { "title": "Deck", "path": null } ] }"""),
            Json("""{ "title": "Deck" }"""));

        Assert.DoesNotContain(list, document => document.Active);
    }

    [Fact]
    public void No_document_inspect_leaves_none_in_front()
    {
        IReadOnlyList<OpenDocument> list = DeckDocumentList.ListFrom(Json("""{ "documents": [ { "title": "Presentation1" } ] }"""));

        Assert.False(Assert.Single(list).Active);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("""{ "documents": 3 }""")]
    [InlineData("""{ "showing": null }""")]
    public void A_malformed_answer_is_an_empty_list(string json)
    {
        Assert.Empty(DeckDocumentList.ListFrom(Json(json)));
    }

    [Fact]
    public void A_malformed_entry_is_skipped_but_keeps_its_place()
    {
        IReadOnlyList<OpenDocument> list = DeckDocumentList.ListFrom(
            Json("""{ "documents": [ 7, { "title": "B", "path": "", "dirty": "yes" } ] }"""));

        OpenDocument only = Assert.Single(list);
        Assert.Equal(1, only.Id);
        Assert.Null(only.Name);
        Assert.False(only.Dirty);
    }
}
