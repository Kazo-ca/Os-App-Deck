using System.Globalization;
using Kazo.Os.AppSdk.Craft;

namespace Kazo.Deck.Documents;

/// <summary>
/// Recognises the files of one multi-file export (doc/en/plan.md, work item 3): File › Export as
/// pictures of every slide hands one <c>save</c> per slide, one after the other, named
/// <c>&lt;title&gt;1.png</c>, <c>&lt;title&gt;2.png</c>… The first file asks where to go, the next
/// ones follow it into the same folder under DeckCraft's names, instead of one dialog per slide.
/// </summary>
/// <remarks>
/// A file belongs to the series when it arrives within <see cref="Window"/> of the previous copy
/// written, has the same extension, and its name is the previous one with the number after it
/// (<c>Plan9.png</c> then <c>Plan10.png</c>). Anything else ends the series. A file that already
/// exists in the folder is never rewritten without asking: the window shows the dialog for it.
/// </remarks>
public sealed class CopySeries
{
    /// <summary>
    /// How long after a copy was written the next file of the series may arrive. DeckCraft sends the
    /// whole series in one frame; the files only wait for the dialog of the first one.
    /// </summary>
    public static readonly TimeSpan Window = TimeSpan.FromSeconds(5);

    private string? _lastName;
    private string? _lastFolder;
    private DateTimeOffset _lastWritten;

    /// <summary>
    /// The folder a copy goes to with no dialog: the previous copy's, when this one follows it.
    /// </summary>
    /// <param name="name">The name DeckCraft gave the copy.</param>
    /// <param name="now">The time it arrived.</param>
    /// <returns>The folder, or <see langword="null"/> when the copy is not the next file of a series.</returns>
    public string? FolderFor(string name, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (_lastName is null || _lastFolder is null || now - _lastWritten > Window || now < _lastWritten)
        {
            return null;
        }

        return Follows(_lastName, name) ? _lastFolder : null;
    }

    /// <summary>A copy was written: the next file of its series goes to the same folder.</summary>
    /// <param name="name">The name DeckCraft gave the copy (not the name the user chose).</param>
    /// <param name="path">The file written.</param>
    /// <param name="now">When it was written.</param>
    public void Written(string name, string path, DateTimeOffset now)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        ArgumentException.ThrowIfNullOrEmpty(path);
        _lastName = name;
        _lastFolder = CraftDocumentFormats.FolderOf(path);
        _lastWritten = now;
    }

    /// <summary>Ends the series: the next copy asks where to go.</summary>
    public void Reset()
    {
        _lastName = null;
        _lastFolder = null;
    }

    /// <summary>
    /// Whether <paramref name="next"/> is the file after <paramref name="previous"/>: same extension,
    /// and a stem that is some prefix followed by a number n, the previous stem being the same prefix
    /// followed by n - 1.
    /// </summary>
    /// <param name="previous">The previous file's name.</param>
    /// <param name="next">The new file's name.</param>
    /// <returns><see langword="true"/> when the names follow each other.</returns>
    public static bool Follows(string previous, string next)
    {
        ArgumentNullException.ThrowIfNull(previous);
        ArgumentNullException.ThrowIfNull(next);
        string extension = CraftDocumentFormats.ExtensionOf(next);
        if (!string.Equals(CraftDocumentFormats.ExtensionOf(previous), extension, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        string previousStem = previous[..^extension.Length];
        string nextStem = next[..^extension.Length];
        for (int digits = 1; digits <= Math.Min(nextStem.Length, 9); digits++)
        {
            string number = nextStem[^digits..];
            if (!number.All(char.IsAsciiDigit))
            {
                break;
            }

            int value = int.Parse(number, NumberStyles.None, CultureInfo.InvariantCulture);
            if (value > 0
                && string.Equals(previousStem, nextStem[..^digits] + (value - 1).ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
