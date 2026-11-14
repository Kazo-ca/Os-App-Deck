namespace Kazo.Deck.Hosting;

/// <summary>The query of DeckCraft's iframe page (<c>CraftFrameOptions.Parameters</c>).</summary>
/// <remarks>
/// DeckCraft installs its command API (<c>window.deckcraft</c>) in every mode, so unlike Kazo Fig
/// and Kazo Photo the page needs no <c>control</c> parameter (doc/en/plan.md, decision 5).
/// </remarks>
public static class DeckCraftParameters
{
    /// <summary>
    /// The parameters for one window: <c>author</c>, the user's name, which DeckCraft writes on new
    /// comments (as Kazo Spreadsheets does); and <c>webgl</c> to force DeckCraft's WebGL 2 renderer
    /// (the <c>--webgl</c> launch argument), which it otherwise only uses when the browser has no
    /// WebGPU. A bare flag is an empty value: DeckCraft only looks for the name.
    /// </summary>
    /// <param name="webGl">Whether to force WebGL 2.</param>
    /// <param name="author">The user's display name, or <see langword="null"/>.</param>
    /// <returns>The parameters, in a stable order.</returns>
    public static IReadOnlyDictionary<string, string> For(bool webGl, string? author = null)
    {
        Dictionary<string, string> parameters = new(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(author))
        {
            parameters["author"] = author.Trim();
        }

        if (webGl)
        {
            parameters["webgl"] = string.Empty;
        }

        return parameters;
    }
}
