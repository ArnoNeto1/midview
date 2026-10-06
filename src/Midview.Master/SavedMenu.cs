using System.IO;
using System.Text.Json;
using Midview.Protocol;

namespace Midview.Master;

// One item the teacher saved in the "Abrir site" or "Abrir programa" menu:
// the name it shows and what it opens (an address, or one command per line).
public sealed record MenuEntry(string Name, string Target);

// The teacher's saved items for the "Abrir site" and "Abrir programa"
// menus, in the order they were saved, next to the classroom list.
public static class SavedMenu
{
    public static string WebsitesPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Midview", "menu-sites.json");

    public static string ProgramsPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Midview", "menu-programas.json");

    // A name already in the menu gets the new target, in its place, instead
    // of a second item with the same name.
    public static IReadOnlyList<MenuEntry> Add(IReadOnlyList<MenuEntry> entries, MenuEntry entry)
    {
        var added = entry with { Name = entry.Name.Trim() };
        return entries.Any(old => SameName(old, added.Name))
            ? entries.Select(old => SameName(old, added.Name) ? added : old).ToList()
            : entries.Append(added).ToList();
    }

    public static IReadOnlyList<MenuEntry> Remove(IReadOnlyList<MenuEntry> entries, string name) =>
        entries.Where(entry => !SameName(entry, name)).ToList();

    // What the menu calls an item the teacher saved without a name.
    public static string NameFor(Uri website) =>
        website.Host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? website.Host[4..] : website.Host;

    public static string NameFor(string commands)
    {
        var first = commands.Split('\n').Select(line => line.Trim()).FirstOrDefault(line => line.Length > 0) ?? string.Empty;
        return ProgramCommand.TryParse(first, out var program, out _, out _)
            && Path.GetFileNameWithoutExtension(program.Replace('\\', '/').Split('/')[^1]) is { Length: > 0 } name
                ? name
                : first;
    }

    // Missing or unreadable is an empty menu, and an item without a name or
    // a target is left out: the menu is only a convenience.
    public static IReadOnlyList<MenuEntry> Load(string path)
    {
        try
        {
            var entries = File.Exists(path) ? JsonSerializer.Deserialize<List<MenuEntry>>(File.ReadAllText(path)) : null;
            return (entries ?? new List<MenuEntry>())
                .Where(entry => !string.IsNullOrWhiteSpace(entry?.Name) && !string.IsNullOrWhiteSpace(entry.Target))
                .ToList();
        }
        catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException)
        {
            return Array.Empty<MenuEntry>();
        }
    }

    // "Salvar no menu" in the windows: what to tell the teacher.
    public static string AddAndSave(string path, MenuEntry entry)
    {
        try
        {
            Save(path, Add(Load(path), entry));
            return $"Salvo no menu como \"{entry.Name.Trim()}\".";
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return $"Não foi possível salvar no menu: {ErrorText.Describe(ex)}";
        }
    }

    public static void Save(string path, IReadOnlyList<MenuEntry> entries)
    {
        var directory = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }
        File.WriteAllText(path, JsonSerializer.Serialize(entries));
    }

    private static bool SameName(MenuEntry entry, string name) =>
        string.Equals(entry.Name.Trim(), name.Trim(), StringComparison.OrdinalIgnoreCase);
}
