namespace Kazo.Deck.Hosting;

/// <summary>
/// The DeckCraft theme for a Kazo OS colour scheme (doc/en/plan.md, decision 10): DeckCraft takes
/// <c>ui.set { dark: true|false }</c> and starts light.
/// </summary>
public static class DeckCraftTheme
{
    /// <summary>
    /// Whether DeckCraft should be dark: <see langword="false"/> for the OS's light scheme,
    /// <see langword="true"/> for anything else (the OS default is dark).
    /// </summary>
    /// <param name="scheme">The scheme <c>CraftFrame</c> reports (<c>light</c> or <c>dark</c>).</param>
    /// <returns>The value of DeckCraft's <c>dark</c> setting.</returns>
    public static bool IsDark(string? scheme) =>
        !string.Equals(scheme, "light", StringComparison.OrdinalIgnoreCase);
}
