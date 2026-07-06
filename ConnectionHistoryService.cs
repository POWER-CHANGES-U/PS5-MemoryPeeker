using System.IO;
using System.Text.Json;

namespace PS5MemoryPeeker;

public static class ConnectionHistoryService
{
    private const int MaxEntries = 12;

    public static IReadOnlyList<ConnectionHistoryItem> Load()
    {
        try
        {
            string path = GetPath();
            if (!File.Exists(path))
            {
                return [];
            }

            return JsonSerializer.Deserialize<List<ConnectionHistoryItem>>(File.ReadAllText(path)) ?? [];
        }
        catch
        {
            return [];
        }
    }

    public static void Save(IEnumerable<ConnectionHistoryItem> entries)
    {
        try
        {
            string path = GetPath();
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, JsonSerializer.Serialize(entries.Take(MaxEntries), new JsonSerializerOptions { WriteIndented = true }));
        }
        catch
        {
            // History must never block memory operations.
        }
    }

    private static string GetPath()
    {
        string root = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(root, "PS5MemoryPeeker", "connections.json");
    }
}
