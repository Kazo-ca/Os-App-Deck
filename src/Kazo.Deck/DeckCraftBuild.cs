namespace Kazo.Deck;

/// <summary>
/// The DeckCraft web build this app carries in <c>wwwroot/deckcraft/</c>, described by its
/// provenance file <c>deckcraft.json</c> (written by <c>tools/vendor-deckcraft.sh</c>, checked by
/// the tests).
/// </summary>
public static class DeckCraftBuild
{
    /// <summary>
    /// The DeckCraft version of the build. Kept equal to the <c>version</c> of
    /// <c>wwwroot/deckcraft/deckcraft.json</c> by a test.
    /// </summary>
    public const string Version = "0.18.1";

    /// <summary>Folder of the build under the app's static files (<c>_content/Kazo.Deck/</c>).</summary>
    public const string Folder = "deckcraft";
}
