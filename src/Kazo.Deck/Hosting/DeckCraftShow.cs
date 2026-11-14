namespace Kazo.Deck.Hosting;

/// <summary>
/// What the window tells DeckCraft about its slide show (doc/en/plan.md, work item 4). The full
/// screen itself is done by the iframe page (<c>wwwroot/host/slide-show.js</c>).
/// </summary>
public static class DeckCraftShow
{
    /// <summary>
    /// The control method that runs one of DeckCraft's commands (<c>engine.execute</c>): commands
    /// such as <c>show.presenter</c> are not control methods of their own.
    /// </summary>
    public const string ExecuteMethod = "engine.execute";

    /// <summary>
    /// The parameters of <see cref="ExecuteMethod"/> sent when DeckCraft is ready:
    /// <c>show.presenter { on: false }</c>, presenter view off in the first version (Benoit's answer
    /// Q3 = A, TODO Kazo-ca/Os-App-Deck#4). DeckCraft turns it on by default and opens it in a
    /// browser popup, outside Kazo OS.
    /// </summary>
    public static object PresenterOff { get; } = new { command = "show.presenter", @params = new { on = false } };
}
