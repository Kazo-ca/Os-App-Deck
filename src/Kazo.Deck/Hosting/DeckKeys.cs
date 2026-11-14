using Kazo.Os.AppSdk.Craft;

namespace Kazo.Deck.Hosting;

/// <summary>
/// The chords the iframe gives back to the window instead of DeckCraft (doc/en/plan.md,
/// decision 12): the OS chords, and <c>Ctrl+N</c> for a new Kazo Deck window (Q1 = A, one
/// presentation per window, as PowerPoint and Kazo Word do).
/// </summary>
/// <remarks>
/// DeckCraft's own <c>Ctrl+N</c> would create a second presentation hidden in the same window (it
/// has no tabs). <c>F5</c>, <c>Shift+F5</c> and <c>Escape</c> stay DeckCraft's (slide show). Of
/// DeckCraft's chords, only <c>Alt+F9</c> (View › Guides) is also an OS chord: it goes to the OS.
/// </remarks>
public static class DeckKeys
{
    /// <summary>Gets every chord the window reserves.</summary>
    public static IReadOnlyList<ReservedKey> All { get; } = ReservedKeys.All;

    /// <summary>
    /// Reads a typed chord. The proxy reports the key as the browser gave it, so the comparison
    /// ignores the case of the key and also takes the physical code.
    /// </summary>
    /// <param name="pressed">The chord the proxy reported.</param>
    /// <returns>What the window does.</returns>
    public static KeyAction ActionOf(ReservedKey pressed)
    {
        ArgumentNullException.ThrowIfNull(pressed);
        ReservedKey chord = ReservedKeys.NewWindow;
        if (pressed.Ctrl != chord.Ctrl || pressed.Alt != chord.Alt || pressed.Shift != chord.Shift || pressed.Meta != chord.Meta)
        {
            return KeyAction.None;
        }

        bool sameKey = pressed.Key is { } key && string.Equals(key, chord.Key, StringComparison.OrdinalIgnoreCase);
        bool sameCode = pressed.Code is { } code && string.Equals(code, chord.Code, StringComparison.Ordinal);
        return sameKey || sameCode ? KeyAction.NewWindow : KeyAction.None;
    }
}

/// <summary>What a chord typed in DeckCraft asks the window for.</summary>
public enum KeyAction
{
    /// <summary>Nothing the window knows.</summary>
    None,

    /// <summary>A new, empty Kazo Deck window.</summary>
    NewWindow,
}
