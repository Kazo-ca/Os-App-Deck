using System.Text.Json;
using Kazo.Os.AppSdk.Craft;

namespace Kazo.Deck.Documents;

/// <summary>
/// Reads the presentations DeckCraft has open (doc/en/plan.md, decision 8): its <c>ui.inspect</c>
/// answer lists them as <c>documents[{ title, dirty, path }]</c>, and its <c>document.inspect</c>
/// answer describes the one in front (<c>{ title, path, dirty, … }</c>).
/// </summary>
/// <remarks>
/// <para>
/// DeckCraft's list carries neither an id nor the active document, so the module's
/// <see cref="OpenDocument.ListFrom(JsonElement)"/> (which wants an <c>id</c>) does not read it. Here
/// <see cref="OpenDocument.Id"/> is the document's place in the list, which is what
/// <c>window.next { index }</c> takes: it holds until a presentation opens or closes, which is why
/// the window reads the list again before each use.
/// </para>
/// <para>
/// <see cref="OpenDocument.Name"/> is the document's <c>path</c>: the name DeckCraft saves it under
/// (the file name the window sent, or the name of its last Save As), <see langword="null"/> for a
/// presentation that has no file (a new one, an outline, a repaired copy). The answers come from the
/// iframe: anything malformed is skipped.
/// </para>
/// </remarks>
public static class DeckDocumentList
{
    /// <summary>Reads the presentations of a <c>ui.inspect</c> answer, with the one in front marked.</summary>
    /// <param name="uiInspect">DeckCraft's <c>ui.inspect</c> answer.</param>
    /// <param name="documentInspect">
    /// DeckCraft's <c>document.inspect</c> answer, which names the presentation in front; when it is
    /// missing, or matches nothing, no presentation is marked active.
    /// </param>
    /// <returns>One entry per presentation, in DeckCraft's order; empty when the answer has no list.</returns>
    public static IReadOnlyList<OpenDocument> ListFrom(JsonElement uiInspect, JsonElement documentInspect = default)
    {
        if (uiInspect.ValueKind != JsonValueKind.Object
            || !uiInspect.TryGetProperty("documents", out JsonElement documents)
            || documents.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        List<OpenDocument> list = [];
        long index = 0;
        foreach (JsonElement document in documents.EnumerateArray())
        {
            // The place counts even for an entry that does not read: window.next takes DeckCraft's index.
            long place = index++;
            if (document.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            string? path = Text(document, "path");
            list.Add(new OpenDocument(
                place,
                string.IsNullOrEmpty(path) ? null : path,
                Text(document, "title") ?? string.Empty,
                Flag(document, "dirty"),
                Active: false));
        }

        int active = ActiveIndex(list, documentInspect);
        if (active >= 0)
        {
            list[active] = list[active] with { Active = true };
        }

        return list;
    }

    // The presentation in front: the one with the same file name, else the only one with the same
    // title and no file. DeckCraft names each untitled presentation apart (Presentation1,
    // Presentation2…), so the title only fails to tell two apart when two presentations without a
    // file have the same title; then none is marked.
    private static int ActiveIndex(List<OpenDocument> list, JsonElement documentInspect)
    {
        if (documentInspect.ValueKind != JsonValueKind.Object)
        {
            return -1;
        }

        string? path = Text(documentInspect, "path");
        if (!string.IsNullOrEmpty(path))
        {
            return list.FindIndex(document => string.Equals(document.Name, path, StringComparison.Ordinal));
        }

        string? title = Text(documentInspect, "title");
        if (title is null)
        {
            return -1;
        }

        List<int> matches = [.. Enumerable.Range(0, list.Count)
            .Where(i => list[i].Name is null && string.Equals(list[i].Title, title, StringComparison.Ordinal))];
        return matches.Count == 1 ? matches[0] : -1;
    }

    private static string? Text(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool Flag(JsonElement element, string name) =>
        element.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.True;
}
