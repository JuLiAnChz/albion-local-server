using System.Text.Json;
using AlbionLocal.Photon;
using AlbionLocal.Server.Content;
using AlbionLocal.Server.Game;

namespace AlbionLocal.Server.Persistence;

/// <summary>
/// One-time move of the JSON save of the server before step 7 (&lt;Paths:Saves&gt;/characters.json, a list of
/// <see cref="Character"/>) into the repository. It runs at start while the repository holds no character: every
/// character in the file is imported in one transaction (old saves with bare item names too,
/// <see cref="InventoryItemConverter"/>), then the file is renamed to characters.json.migrated, never deleted.
/// </summary>
public static class JsonSaveImport
{
    /// <summary>
    /// Imports <paramref name="path"/> if it exists and the repository is empty; returns the number of characters
    /// imported. A file next to a repository that already has characters is left alone with a warning (it may hold play
    /// the database does not have, e.g. from an older build started since the import).
    /// </summary>
    public static int Run(ICharacterRepository repository, string path, StarterKit starter)
    {
        if (!File.Exists(path)) return 0;
        if (repository.List() is { Count: > 0 } existing)
        {
            Log.Warn("store", $"{path} not imported: the database already has {existing.Count} character(s); "
                + "rename or remove the file to silence this");
            return 0;
        }
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var characters = new List<Character>();
        foreach (var e in doc.RootElement.EnumerateArray())
        {
            var c = e.Deserialize<Character>() ?? throw new InvalidDataException($"{path}: null character");
            // Equipment and inventory left out of an entry meant the starter kit (the defaults of Character until step 7).
            if (!e.TryGetProperty(nameof(Character.Equipment), out _)) c.Equipment = [.. starter.Equipment.Select(i => i?.Copy())];
            if (!e.TryGetProperty(nameof(Character.Inventory), out _)) c.Inventory = [.. starter.Inventory.Select(i => i?.Copy())];
            characters.Add(c);
        }
        var imported = repository.Import(characters);
        var target = path + ".migrated";
        for (var n = 2; File.Exists(target); n++) target = $"{path}.migrated.{n}";
        string? renameError = null;
        try { File.Move(path, target); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { renameError = e.Message; }
        // Counts only: the names stay out of the log.
        var items = characters.Sum(c => c.Equipment.Count(i => i is not null) + c.Inventory.Count(i => i is not null));
        Log.Info("store", $"imported {imported} of {characters.Count} character(s) from {path} ({items} items, "
            + $"{characters.Count(c => c.Progression is not null)} with own destiny board state); "
            + (renameError is null ? $"file renamed to {Path.GetFileName(target)}" : "file not renamed"));
        if (renameError is not null) // imported all the same; from now on the file is skipped with the warning above
            Log.Warn("store", $"renaming {path} failed ({renameError}): it stays and is not imported again");
        if (imported < characters.Count)
            Log.Warn("store", $"{characters.Count - imported} character(s) skipped: id or name used twice in {path}");
        return imported;
    }
}
