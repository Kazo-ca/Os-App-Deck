using Kazo.Os.AppSdk.Craft;
using Kazo.Deck.Hosting;

namespace Kazo.Deck.Tests;

/// <summary>What the window gives DeckCraft: the page's query, the theme and the chords.</summary>
public sealed class HostingTests
{
    [Fact]
    public void The_page_needs_no_parameter_by_default()
    {
        // DeckCraft installs window.deckcraft in every mode (decision 5): no "control" parameter.
        Assert.Empty(DeckCraftParameters.For(webGl: false));
    }

    [Fact]
    public void The_webgl_argument_forces_WebGL_2()
    {
        IReadOnlyDictionary<string, string> parameters = DeckCraftParameters.For(webGl: true);

        Assert.Equal(string.Empty, Assert.Single(parameters, pair => pair.Key == "webgl").Value);
        Assert.Single(parameters);
    }

    [Theory]
    [InlineData("light", false)]
    [InlineData("LIGHT", false)]
    [InlineData("dark", true)]
    [InlineData(null, true)]
    [InlineData("high-contrast", true)]
    public void DeckCraft_follows_the_OS_scheme_not_the_browser(string? scheme, bool dark)
    {
        Assert.Equal(dark, DeckCraftTheme.IsDark(scheme));
    }

    [Fact]
    public void The_theme_goes_to_DeckCraft_with_ui_set_dark()
    {
        // Decision 10: ui.set { dark } at ready and at every scheme change; no language to send.
        string proxy = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Kazo.Deck", "DeckWindow.Proxy.cs"));
        string window = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Kazo.Deck", "DeckWindow.razor.cs"));
        Assert.Contains("\"ui.set\", new { dark = DeckCraftTheme.IsDark(scheme) }", proxy);
        Assert.Contains("await OnSchemeChangedAsync(await frame.GetColorSchemeAsync());", window);
        Assert.DoesNotContain("Language =", window);
    }

    [Fact]
    public void The_window_reserves_the_OS_chords_and_Ctrl_N()
    {
        // Decision 12 and Q1 = A: Ctrl+N opens a new window instead of a hidden presentation.
        string window = File.ReadAllText(Path.Combine(RepositoryRoot(), "src", "Kazo.Deck", "DeckWindow.razor.cs"));
        Assert.Contains("ReservedKeys = DeckKeys.All,", window);
        Assert.Contains(ReservedKeys.NewWindow, DeckKeys.All);
        Assert.All(ReservedKeys.Os, chord => Assert.Contains(chord, DeckKeys.All));
    }

    [Theory]
    [InlineData("n", "KeyN", true, false, KeyAction.NewWindow)]
    [InlineData("N", null, true, false, KeyAction.NewWindow)]
    [InlineData(null, "KeyN", true, false, KeyAction.NewWindow)]
    [InlineData("n", "KeyN", true, true, KeyAction.None)]
    [InlineData("n", "KeyN", false, false, KeyAction.None)]
    [InlineData("s", "KeyS", true, false, KeyAction.None)]
    public void Ctrl_N_asks_for_a_new_window(string? key, string? code, bool ctrl, bool shift, KeyAction expected)
    {
        ReservedKey pressed = new(key, code, ctrl, Alt: false, shift, Meta: false, ReservedKeyTarget.App);

        Assert.Equal(expected, DeckKeys.ActionOf(pressed));
    }

    [Fact]
    public void The_user_name_signs_comments()
    {
        IReadOnlyDictionary<string, string> parameters = DeckCraftParameters.For(webGl: false, author: "  Neo  ");

        Assert.Equal("Neo", Assert.Single(parameters).Value);
        Assert.Empty(DeckCraftParameters.For(webGl: false, author: "   "));
        Assert.Equal(["author", "webgl"], DeckCraftParameters.For(webGl: true, author: "Neo").Keys);
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Kazo.Deck.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("Kazo.Deck.slnx not found above the test binaries.");
    }
}
