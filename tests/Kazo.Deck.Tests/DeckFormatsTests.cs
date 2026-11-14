using Kazo.Os.App.Abstractions;
using Kazo.Deck.Documents;

namespace Kazo.Deck.Tests;

/// <summary>The file associations of decision 13 (doc/en/plan.md), accepted by Benoit on 2026-11-14.</summary>
public sealed class DeckFormatsTests
{
    [Theory]
    [InlineData(".pptx")]
    [InlineData(".ppsx")]
    [InlineData(".potx")]
    [InlineData(".deckcraft")]
    public void Kazo_Deck_is_the_app_of_the_presentations(string extension)
    {
        FileHandler handler = HandlerOf(extension);
        Assert.Equal(["open", "edit"], handler.Actions);
    }

    [Theory]
    [InlineData(".txt")]
    [InlineData(".md")]
    [InlineData(".rtf")]
    public void The_outlines_are_only_in_Open_with(string extension)
    {
        // The text editor stays their app; DeckCraft makes a presentation outline of them.
        FileHandler handler = HandlerOf(extension);
        Assert.Equal(["open"], handler.Actions);
    }

    [Fact]
    public void No_media_type_mixes_a_default_format_with_an_Open_with_one()
    {
        // The manifest takes a media type's flag from its first format: a mix would make Kazo Deck
        // the default app of a text file, or the reverse.
        foreach (IGrouping<string, Os.AppSdk.Craft.CraftDocumentFormat> group in DeckFormats.Formats.All.GroupBy(format => format.MediaType))
        {
            Assert.Single(group.Select(format => format.IsDefault).Distinct());
        }
    }

    [Fact]
    public void Every_format_opens_and_none_is_a_copy_by_its_extension()
    {
        // Decision 6: an export is told apart by its name (an unbound save with saveAs false), not
        // by the extension: a .pptx can be the document or a copy.
        Assert.All(DeckFormats.Formats.All, format =>
        {
            Assert.True(format.Opens);
            Assert.False(format.IsCopy);
        });
        Assert.Equal(7, DeckFormats.OpenExtensions.Count);
    }

    private static FileHandler HandlerOf(string extension) =>
        Assert.Single(DeckFormats.FileHandlers, handler => handler.Extensions.Contains(extension));
}
