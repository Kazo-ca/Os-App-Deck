using System.Text.Json;
using System.Xml.Linq;

namespace Kazo.Deck.Tests;

/// <summary>
/// The licenses travel with the app (work item 5, THIRD-PARTY-NOTICES.md): the project links the
/// repository's license and notices into the package's web root, DeckCraft's own notices are in
/// its build folder, the files its NOTICE refers to are copied next to them, and the notices name
/// the DeckCraft build actually bundled.
/// </summary>
public sealed class LegalFilesTests
{
    private static readonly string Root = FindRepositoryRoot();

    private static readonly string DeckCraftFolder = Path.Combine(Root, "src", "Kazo.Deck", "wwwroot", "deckcraft");

    [Theory]
    [InlineData("LICENSE", "wwwroot/legal/LICENSE.txt")]
    [InlineData("THIRD-PARTY-NOTICES.md", "wwwroot/legal/THIRD-PARTY-NOTICES.md")]
    [InlineData("THIRD-PARTY-NOTICES-fr.md", "wwwroot/legal/THIRD-PARTY-NOTICES-fr.md")]
    public void The_project_puts_the_repository_legal_files_in_the_package(string file, string link)
    {
        XDocument project = XDocument.Load(Path.Combine(Root, "src", "Kazo.Deck", "Kazo.Deck.csproj"));

        // A Content item linked under wwwroot/ is a static web asset: KazoOsPack packs it.
        XElement? item = project.Descendants("Content")
            .FirstOrDefault(element => (string?)element.Attribute("Link") == link);
        Assert.NotNull(item);
        Assert.Equal($"../../{file}", (string?)item.Attribute("Include"));
        Assert.True(File.Exists(Path.Combine(Root, file)), $"{file} is missing at the repository root");
    }

    [Theory]
    [InlineData("LICENSE-MIT")]
    [InlineData("LICENSE-APACHE")]
    [InlineData("NOTICE")]
    public void DeckCraft_ships_its_own_notices(string file)
    {
        Assert.True(File.Exists(Path.Combine(DeckCraftFolder, file)), $"deckcraft/{file} is missing");
    }

    [Fact]
    public void Every_font_license_of_the_build_is_named_in_the_notices()
    {
        // The build carries one OFL-<family>.txt per embedded family; the notices cover them as a set.
        string[] licenses = Directory.GetFiles(DeckCraftFolder, "OFL-*.txt");
        Assert.NotEmpty(licenses);
        Assert.Contains("`OFL-*.txt`", File.ReadAllText(Path.Combine(Root, "THIRD-PARTY-NOTICES.md")), StringComparison.Ordinal);
        Assert.Contains("`OFL-*.txt`", File.ReadAllText(Path.Combine(Root, "THIRD-PARTY-NOTICES-fr.md")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("ATTRIBUTION.md")]
    [InlineData("LICENSE-ICC.txt")]
    [InlineData("LICENSE-ubuntu-font.txt")]
    [InlineData("LICENSE-hack.txt")]
    [InlineData("OFL-noto-emoji.txt")]
    [InlineData("LICENSE-emoji-icon-font.txt")]
    [InlineData("LICENSE-symphonia-MPL-2.0.txt")]
    public void The_files_DeckCraft_refers_to_ship_in_the_package(string file)
    {
        // DeckCraft's NOTICE names these files and licenses but its web build does not carry them:
        // they are copied unmodified, and listed in THIRD-PARTY-NOTICES.md.
        string path = Path.Combine(Root, "src", "Kazo.Deck", "wwwroot", "legal", "deckcraft", file);
        Assert.True(File.Exists(path), $"legal/deckcraft/{file} is missing");
        Assert.True(new FileInfo(path).Length > 0, $"legal/deckcraft/{file} is empty");
        Assert.Contains($"`{file}`", File.ReadAllText(Path.Combine(Root, "THIRD-PARTY-NOTICES.md")), StringComparison.Ordinal);
        Assert.Contains($"`{file}`", File.ReadAllText(Path.Combine(Root, "THIRD-PARTY-NOTICES-fr.md")), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("THIRD-PARTY-NOTICES.md")]
    [InlineData("THIRD-PARTY-NOTICES-fr.md")]
    public void The_notices_name_the_bundled_DeckCraft_commit_version_and_fonts(string file)
    {
        // The files above were copied at this commit: moving to another build without copying them
        // again and updating the notices fails here.
        using JsonDocument provenance = JsonDocument.Parse(File.ReadAllText(Path.Combine(DeckCraftFolder, "deckcraft.json")));
        JsonElement source = provenance.RootElement.GetProperty("source");
        string version = provenance.RootElement.GetProperty("version").GetString()!;

        string notices = File.ReadAllText(Path.Combine(Root, file));
        Assert.Contains(source.GetProperty("commit").GetString()!, notices, StringComparison.Ordinal);
        Assert.Contains(source.GetProperty("craftFonts").GetString()!, notices, StringComparison.Ordinal);
        Assert.Contains($"| {version}, ", notices, StringComparison.Ordinal);
        Assert.Contains($"kazo/v{version}", notices, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("description.en.md", "DeckCraft")]
    [InlineData("description.fr.md", "DeckCraft")]
    [InlineData("description.en.md", "ArtCraft")]
    [InlineData("description.fr.md", "ArtCraft")]
    [InlineData("description.en.md", "Open Font License")]
    [InlineData("description.fr.md", "Open Font License")]
    public void The_store_listing_credits_DeckCraft_and_the_fonts(string file, string name)
    {
        Assert.Contains(name, File.ReadAllText(Path.Combine(Root, "store", file)), StringComparison.Ordinal);
    }

    [Fact]
    public void The_store_listing_names_the_minimum_Kazo_OS_version_of_the_manifest()
    {
        // The listing tells users which Kazo OS they need: it must follow the manifest.
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "Kazo.Deck", "manifest.json")));
        string minimum = manifest.RootElement.GetProperty("sdkCompatibility").GetProperty("minimumVersion").GetString()!;
        Assert.Contains($"Kazo OS {minimum}", File.ReadAllText(Path.Combine(Root, "store", "description.en.md")), StringComparison.Ordinal);
        Assert.Contains($"Kazo OS {minimum}", File.ReadAllText(Path.Combine(Root, "store", "description.fr.md")), StringComparison.Ordinal);
    }

    [Fact]
    public void The_store_listing_names_every_extension_Kazo_Deck_opens()
    {
        // The listing says which files open in Kazo Deck: it must follow the manifest's handlers.
        using JsonDocument manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "src", "Kazo.Deck", "manifest.json")));
        string en = File.ReadAllText(Path.Combine(Root, "store", "description.en.md"));
        string fr = File.ReadAllText(Path.Combine(Root, "store", "description.fr.md"));
        foreach (JsonElement handler in manifest.RootElement.GetProperty("fileHandlers").EnumerateArray())
        {
            foreach (JsonElement extension in handler.GetProperty("extensions").EnumerateArray())
            {
                Assert.Contains($"`{extension.GetString()}`", en, StringComparison.Ordinal);
                Assert.Contains($"`{extension.GetString()}`", fr, StringComparison.Ordinal);
            }
        }
    }

    [Fact]
    public void The_store_tags_are_ones_the_catalog_accepts()
    {
        // Os-AppMarketPlace refuses any other tag (lower case, digits and '-', 30 characters at most):
        // Kazo Word 0.1.0 had "word processor" and its catalog folder had to be corrected by hand.
        using JsonDocument store = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root, "store", "store.json")));
        foreach (JsonElement tag in store.RootElement.GetProperty("tags").EnumerateArray())
        {
            Assert.Matches("^[a-z0-9][a-z0-9-]{0,29}$", tag.GetString());
        }

        Assert.Equal("https://github.com/Kazo-ca/Os-App-Deck", store.RootElement.GetProperty("repository").GetString());
        Assert.Equal("src/Kazo.Deck/Kazo.Deck.csproj", store.RootElement.GetProperty("project").GetString());
    }

    private static string FindRepositoryRoot()
    {
        for (DirectoryInfo? directory = new(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Kazo.Deck.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("Kazo.Deck.slnx not found above " + AppContext.BaseDirectory);
    }
}
