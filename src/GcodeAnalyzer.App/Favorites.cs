using System.IO;
using System.Text.Json;

namespace GcodeAnalyzer.App;

/// <summary>Favoriten (angeheftete Zeilen der Seitenleiste) als Liste ihrer Beschriftungen in %AppData%\GcodeAnalyzer.</summary>
public static class Favorites
{
    static readonly string FilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GcodeAnalyzer", "favoriten.json");

    public static readonly string[] Defaults = ["Wandschleifen (Soll)", "Schalen oben", "Schalen unten", "Infill"];

    public static HashSet<string> Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<HashSet<string>>(File.ReadAllText(FilePath)) ?? [];
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException) { }
        return [.. Defaults];
    }

    public static void Save(HashSet<string> favorites)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(favorites.Order()));
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { } // Favoriten sind Komfort, kein Absturzgrund
    }
}
